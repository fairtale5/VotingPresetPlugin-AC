/// <summary>
/// VotingPresetPlugin – timer-based preset (track/config) voting.
///
/// Purpose:
/// Runs a recurring timer vote: every IntervalMinutes the server offers a set of presets;
/// players vote by number (/vt 0, /vt 1, …). Winner is applied after the vote ends.
/// Admins can list presets, set preset directly, start/finish/cancel/extend the timer vote.
///
/// How it works:
/// 1. Preset lists: PresetConfigurationManager provides votable and all presets; this plugin partitions
///    public vs admin-only, sorts each group by display name, and builds one contiguous index list.
/// 2. Timer loop: ExecuteAsync waits (IntervalMilliseconds − VotingDurationMilliseconds), then
///    calls VotingAsync(stoppingToken) if EnableVote is true. So the vote window is the last
///    VotingDurationSeconds of each interval.
/// 3. Starting a vote: VotingAsync builds _availablePresets from _publicPresetsOrdered (current preset
///    removed; optionally "stay on track" as option 0; then random others up to VoteChoices).
///    It broadcasts "Vote for next track:" and "/vt i - Name" for each option via
///    _entryCarManager.BroadcastChat. Then it calls WaitVoting to hold the vote open.
/// 4. Collecting votes: WaitVoting sets _votingOpen = true and loops once per second for
///    VotingDurationSeconds (and optionally extra time if ExtendVote added seconds to
///    _extendVotingSeconds). Admin can set _finishVote = 1 (finish) or -1 (cancel). When
///    time is up, _votingOpen is set false and the method returns true if finished, false if
///    canceled.
/// 5. Result: VotingAsync takes the option(s) with the highest vote count in _availablePresets,
///    picks a random winner if tie, then either broadcasts "staying on track" or calls
///    _presetManager.SetPreset with PresetData(current, winner) after TransitionDelayMilliseconds.
/// 6. State: _voteState (Idle | TimerVote | OnDemandVote) ensures only one vote at a time; _alreadyVoted prevents double-vote per client in timer votes. _finishVote and _extendVotingSeconds are the only way admin can end or extend the wait inside WaitVoting. On-demand: _onDemandVoteByClient maps each voter (ACTcpClient) to yes=true or no=false; StartOnDemandVote sets OnDemandVote, RunOnDemandVoteAsync runs reminders; /yes and /no call VoteOnDemand (users may switch yes/no; tallies adjust). Cooldown applied to requester when on-demand vote fails or is canceled.
/// </summary>
using System.Linq;
using System.Reflection;
using AssettoServer.Commands.Contexts;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using AssettoServer.Utils;
using Microsoft.Extensions.Hosting;
using Serilog;
using VotingPresetPlugin.Packets;
using VotingPresetPlugin.Preset;

namespace VotingPresetPlugin;

/// <summary>Current vote state: no vote, timer vote (multi-option by number), or on-demand vote (yes/no for one preset). Only one can be active at a time.</summary>
internal enum VoteState
{
    Idle,
    TimerVote,
    OnDemandVote
}

public class VotingPresetPlugin : BackgroundService
{
    private readonly EntryCarManager _entryCarManager;
    private readonly PresetManager _presetManager;
    private readonly VotingPresetConfiguration _configuration;

    /// <summary>Public (votable) presets only, sorted by <see cref="PresetType.Name"/> so /preset indices stay 0..N-1 with no gaps.</summary>
    private readonly List<PresetType> _publicPresetsOrdered;
    /// <summary>Clients who have already cast a vote in the current timer vote (used to block double-vote).</summary>
    private readonly List<ACTcpClient> _alreadyVoted = new();
    /// <summary>Current vote options: each PresetChoice holds a PresetType and its vote count; built at vote start and updated by CountVote.</summary>
    private readonly List<PresetChoice> _availablePresets = new();
    /// <summary>True while WaitVoting is running; when true, CountVote accepts votes and _alreadyVoted is used. Used only for the timer vote (interval or admin-started); the on-demand vote does not use this flag.</summary>
    private bool _votingOpen = false;

    /// <summary>All presets (votable + admin-only) from PresetConfigurationManager.AllPresetTypes; used for /presetlist and admin SetPreset/RandomPreset by index.</summary>
    private readonly List<PresetType> _adminPresets;

    /// <summary>Public presets first (alphabetically by name), then admin-only presets (alphabetically by name). Same list drives /presets, /preset, and /presetset.</summary>
    private readonly List<PresetType> _unifiedPresets;

    /// <summary>Count of public (non–admin-only) presets at the start of _unifiedPresets. /preset &lt;n&gt; only allows n &lt; this.</summary>
    private readonly int _publicPresetCount;

    /// <summary>Current vote state. Idle = no vote; TimerVote = timer vote in progress (VotingAsync + WaitVoting); OnDemandVote = on-demand yes/no vote in progress (StartOnDemandVote + RunOnDemandVoteAsync).</summary>
    private VoteState _voteState = VoteState.Idle;
    /// <summary>Extra seconds to add to the current wait in WaitVoting; admin adds via ExtendVote; consumed and reset inside the while loop in WaitVoting.</summary>
    private int _extendVotingSeconds = 0;
    /// <summary>0 = no signal; 1 = admin requested finish (WaitVoting returns true); -1 = admin requested cancel (WaitVoting returns false). Reset to 0 when WaitVoting exits.</summary>
    private short _finishVote = 0;
    /// <summary>Stored from ExecuteAsync so StartVote can pass it to VotingAsync when admin starts a vote manually.</summary>
    private CancellationToken _cancellationToken = CancellationToken.None;

    // --- On-demand vote state (used when _voteState == OnDemandVote) ---
    /// <summary>Client who started the current on-demand vote; used to apply cooldown on fail.</summary>
    private ACTcpClient? _onDemandRequester;
    /// <summary>Preset being voted on in the current on-demand vote.</summary>
    private PresetType? _onDemandTargetPreset;
    /// <summary>When the on-demand vote ends (used to know if vote is still open and for display).</summary>
    private DateTime _onDemandEndTime;
    /// <summary>Number of yes votes in the current on-demand vote.</summary>
    private int _onDemandYesCount;
    /// <summary>Number of no votes in the current on-demand vote.</summary>
    private int _onDemandNoCount;
    /// <summary>On-demand ballot: client → true=yes, false=no. Key present iff that client has cast a vote this round.</summary>
    private readonly Dictionary<ACTcpClient, bool> _onDemandVoteByClient = new();
    /// <summary>Last time we broadcasted an on-demand vote status line (start message, vote update, or reminder). Used for 30s reminder timing.</summary>
    private DateTime _onDemandLastBroadcastTime;
    /// <summary>Votable list index for the current on-demand vote (same as /preset &lt;n&gt;). Used for CSP open packet and UI.</summary>
    private int _onDemandPresetIndex;

    /// <summary>True when extra_cfg allows CSP client messages (packet registration and vote UI broadcast).</summary>
    private readonly bool _enableClientMessages;

    // --- Cooldown: block requester from starting another on-demand vote after a failed one ---
    /// <summary>Maps client to UTC time when their cooldown ends (after they started an on-demand vote that failed). Checked before starting a new on-demand vote.</summary>
    private readonly Dictionary<ACTcpClient, DateTime> _requesterCooldownUntil = new();

    /// <summary>One option in a timer vote: the preset and how many players voted for it (Votes updated by CountVote).</summary>
    private class PresetChoice
    {
        public PresetType? Preset { get; init; }
        public int Votes { get; set; }
    }

    /// <summary>
    /// Constructor – wires config and preset lists, applies initial preset, optionally registers reconnect Lua.
    /// 1) Store configuration, entryCarManager, presetManager.
    /// 2) Build sorted public list, sorted admin-only list, and _unifiedPresets from PresetConfigurationManager.
    /// 3) Require CSP ≥ 0.2.0 (2651); throw ConfigurationException otherwise.
    /// 4) Apply initial preset: call _presetManager.SetPreset with presetConfigurationManager.CurrentConfiguration.ToPresetType(), IsInit = true, TransitionDuration = 0.
    /// 5) If acServerConfiguration.Extra.EnableClientMessages and _configuration.EnableReconnect: load embedded resource "VotingPresetPlugin.lua.reconnectclient.lua" via Assembly.GetExecutingAssembly().GetManifestResourceStream, then scriptProvider.AddScript(stream, "reconnectclient.lua"); add CSP feature "FREQUENT_TRACK_CHANGES" to cspFeatureManager.
    /// 6) If EnableClientMessages: register VotingPresetVoteCastPacket so clients can send yes/no via CSP (same rules as chat).
    /// </summary>
    public VotingPresetPlugin(VotingPresetConfiguration configuration,
        PresetConfigurationManager presetConfigurationManager,
        ACServerConfiguration acServerConfiguration,
        EntryCarManager entryCarManager,
        PresetManager presetManager,
        CSPServerScriptProvider scriptProvider,
        CSPFeatureManager cspFeatureManager,
        CSPClientMessageTypeManager cspClientMessageTypeManager)
    {
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _presetManager = presetManager;
        _enableClientMessages = acServerConfiguration.Extra.EnableClientMessages;

        // Preset lists from PresetConfigurationManager; partition public vs admin-only, sort each by display name, then one contiguous index space.
        _adminPresets = presetConfigurationManager.AllPresetTypes;
        var voteFromManager = presetConfigurationManager.VotingPresetTypes;

        _publicPresetsOrdered = voteFromManager
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var adminOnlyOrdered = _adminPresets
            .Where(p => !voteFromManager.Any(v => v.Equals(p)))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _unifiedPresets = new List<PresetType>(_publicPresetsOrdered.Count + adminOnlyOrdered.Count);
        _unifiedPresets.AddRange(_publicPresetsOrdered);
        _unifiedPresets.AddRange(adminOnlyOrdered);
        _publicPresetCount = _publicPresetsOrdered.Count;

        if (acServerConfiguration.CSPTrackOptions.MinimumCSPVersion < CSPVersion.V0_2_0)
        {
            throw new ConfigurationException("VotingPresetPlugin needs a minimum required CSP version of 0.2.0 (2651)");
        }

        // Apply initial preset so server starts with CurrentConfiguration; PresetManager will handle restart path
        _presetManager.SetPreset(new PresetData(presetConfigurationManager.CurrentConfiguration.ToPresetType(), null)
        {
            IsInit = true,
            TransitionDuration = 0
        });

        // Reconnect script: when client messages enabled and EnableReconnect true, inject reconnectclient.lua so clients can reconnect on track change instead of being kicked
        if (acServerConfiguration.Extra.EnableClientMessages && _configuration.EnableReconnect)
        {
            scriptProvider.AddScript(Assembly.GetExecutingAssembly().GetManifestResourceStream("VotingPresetPlugin.lua.reconnectclient.lua")!, "reconnectclient.lua");
            cspFeatureManager.Add(new CSPFeature { Name = "FREQUENT_TRACK_CHANGES" });
        }

        // Client → server: preset vote cast (v1 on-demand yes/no). Chat commands still work if Lua is not installed.
        if (_enableClientMessages)
            cspClientMessageTypeManager.RegisterOnlineEvent<VotingPresetVoteCastPacket>(OnVoteCastFromClient);
    }

    /// <summary>
    /// CSP client sent VotingPreset_VoteCast. Route on-demand yes/no into VoteOnDemand (same trust as /yes /no via ACTcpClient).
    /// </summary>
    private void OnVoteCastFromClient(ACTcpClient client, VotingPresetVoteCastPacket packet)
    {
        if (packet.VoteKind != VotingPresetVoteCastPacket.VoteKindOnDemandYesNo)
            return;

        var context = new ChatCommandContext(client, _entryCarManager);
        VoteOnDemand(context, packet.IsYes);
    }

    /// <summary>
    /// Push VotingPreset_VoteOpen to all clients when client messages are on (mirrors on-demand chat tallies for Lua UI).
    /// </summary>
    private void BroadcastOnDemandVoteOpenUi(int secondsRemaining)
    {
        if (!_enableClientMessages || _onDemandTargetPreset == null)
            return;

        var abstained = _entryCarManager.ConnectedCars.Count - _onDemandVoteByClient.Count;
        var name = _onDemandTargetPreset.Name ?? "";
        if (name.Length > 128)
            TruncatePresetName(ref name);

        _entryCarManager.BroadcastPacket(new VotingPresetVoteOpenPacket
        {
            VoteKind = VotingPresetVoteOpenPacket.VoteKindOnDemandYesNo,
            SecondsRemaining = (ushort)Math.Clamp(secondsRemaining, 0, ushort.MaxValue),
            YesCount = (ushort)Math.Clamp(_onDemandYesCount, 0, ushort.MaxValue),
            NoCount = (ushort)Math.Clamp(_onDemandNoCount, 0, ushort.MaxValue),
            AbstainedCount = (ushort)Math.Clamp(abstained, 0, ushort.MaxValue),
            VotableIndex = (byte)Math.Clamp(_onDemandPresetIndex, 0, byte.MaxValue),
            PresetName = name,
        });
    }

    private static void TruncatePresetName(ref string name)
    {
        name = name[..128];
    }

    /// <summary>
    /// Background loop: wait (IntervalMilliseconds − VotingDurationMilliseconds), then run one timer vote if EnableVote.
    /// 1) Store stoppingToken in _cancellationToken so StartVote can pass it to VotingAsync.
    /// 2) Loop: await Task.Delay(IntervalMilliseconds − VotingDurationMilliseconds, stoppingToken); then if _configuration.EnableVote, call VotingAsync(stoppingToken). Log "Starting preset vote" before the call. Swallow TaskCanceledException; log other exceptions.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _cancellationToken = stoppingToken;
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(_configuration.IntervalMilliseconds - _configuration.VotingDurationMilliseconds,
                stoppingToken);
            try
            {
                Log.Information("Starting preset vote");
                if (_configuration.EnableVote)
                    await VotingAsync(stoppingToken);
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during voting preset update");
            }
        }
    }

    /// <summary>
    /// Reply with votable presets using unified indices 0..public-1 for /preset, then (if admin) admin-only presets with /presetset using continuing indices public..total-1.
    /// /presetset accepts any unified index for an instant switch (same numbering as the two blocks together).
    /// </summary>
    internal void ListAllPresets(BaseCommandContext context)
    {
        context.Reply("To start a vote: /preset <number>");
        for (int i = 0; i < _publicPresetCount; i++)
            context.Reply($" /preset {i} - {_unifiedPresets[i].Name}");

        if (!context.IsAdministrator) return;

        context.Reply("Restricted presets use: /presetset <number>");
        for (int i = _publicPresetCount; i < _unifiedPresets.Count; i++)
            context.Reply($" /presetset {i} - {_unifiedPresets[i].Name}");

        context.Reply(
            $"Admins: /presetset 0–{_unifiedPresets.Count - 1} switches instantly (same numbers as above; /preset n only starts a vote for public presets).");
    }

    /// <summary>
    /// Reply with current preset: read _presetManager.CurrentPreset.Type.Name and .PresetFolder, log them and send same text to context.Reply; then send "Use /presets to list all presets." Used by /preset with no args.
    /// </summary>
    internal void GetPreset(BaseCommandContext context)
    {
        Log.Information("Current preset: {Name} - {PresetFolder}", _presetManager.CurrentPreset.Type!.Name, _presetManager.CurrentPreset.Type!.PresetFolder);
        context.Reply($"Current preset: {_presetManager.CurrentPreset.Type!.Name} - {_presetManager.CurrentPreset.Type!.PresetFolder}");
        context.Reply("Use /presets to list all presets.");
    }

    /// <summary>
    /// Reply with current preset, optional vote status (time left when on-demand vote is running), and list of votable presets (index + name). Used by /preset with no args.
    /// </summary>
    internal void GetPresetListAndHelp(BaseCommandContext context)
    {
        var current = _presetManager.CurrentPreset.Type!;
        context.Reply($"Current preset: {current.Name} - {current.PresetFolder}");

        if (_voteState == VoteState.OnDemandVote && _onDemandTargetPreset != null)
        {
            var remaining = (_onDemandEndTime - DateTime.UtcNow).TotalSeconds;
            var s = Math.Max(0, (int)remaining);
            var abstained = _entryCarManager.ConnectedCars.Count - _onDemandVoteByClient.Count;
            context.Reply($"Vote in progress for {_onDemandTargetPreset.Name}. {s}s left. /yes /no — Yes: {_onDemandYesCount}, No: {_onDemandNoCount}, —: {abstained}");
        }
        else if (_voteState == VoteState.TimerVote)
        {
            context.Reply("Timer vote in progress. Use /vote <number> to vote.");
        }

        context.Reply("Votable presets:");
        for (int i = 0; i < _publicPresetCount; i++)
            context.Reply($" {i} - {_unifiedPresets[i].Name}");
        context.Reply("Use /preset <number> to start a vote.");
    }

    /// <summary>
    /// Start an on-demand vote for the preset at the given index in the votable list. Require Idle, valid index, requester not on cooldown; then set state, broadcast, and run the vote loop in the background.
    /// </summary>
    internal void StartOnDemandVote(ChatCommandContext context, int presetIndex)
    {
        if (_voteState != VoteState.Idle)
        {
            context.Reply("A vote is already in progress.");
            return;
        }
        if (presetIndex < 0 || presetIndex >= _publicPresetCount)
        {
            context.Reply("Invalid preset number. Use /presets for public presets, or /presetset (admin) for any track.");
            return;
        }
        if (_requesterCooldownUntil.TryGetValue(context.Client, out var until) && until > DateTime.UtcNow)
        {
            var minutes = (int)Math.Ceiling((until - DateTime.UtcNow).TotalMinutes);
            context.Reply($"You cannot start another vote yet. Cooldown ends in {minutes} min.");
            return;
        }

        var target = _unifiedPresets[presetIndex];
        if (target.Equals(_presetManager.CurrentPreset.Type))
        {
            context.Reply("Current preset is already this track.");
            return;
        }

        _voteState = VoteState.OnDemandVote;
        _onDemandRequester = context.Client;
        _onDemandTargetPreset = target;
        _onDemandPresetIndex = presetIndex;
        _onDemandEndTime = DateTime.UtcNow.AddSeconds(_configuration.VotingDurationSeconds);
        _onDemandVoteByClient.Clear();
        _onDemandVoteByClient[context.Client] = true;
        _onDemandYesCount = 1;
        _onDemandNoCount = 0;

        var abstained = _entryCarManager.ConnectedCars.Count - 1;
        _onDemandLastBroadcastTime = DateTime.UtcNow;
        _entryCarManager.BroadcastChat($"Change to {target.Name}? /yes /no ({_configuration.VotingDurationSeconds}s) — Yes: 1, No: 0, —: {abstained}");
        BroadcastOnDemandVoteOpenUi(_configuration.VotingDurationSeconds);
        _ = RunOnDemandVoteAsync();
    }

    /// <summary>
    /// Background loop for the current on-demand vote: wait until _onDemandEndTime, send a reminder when 30s have passed since the last broadcast, and end early when the result is decided (yes has majority or no cannot lose).
    /// Then evaluate yes vs no, apply preset or cooldown, and set _voteState = Idle.
    /// </summary>
    private async Task RunOnDemandVoteAsync()
    {
        while (DateTime.UtcNow < _onDemandEndTime && _voteState == VoteState.OnDemandVote)
        {
            await Task.Delay(1000, _cancellationToken);
            if (_voteState != VoteState.OnDemandVote) return;
            if (_onDemandTargetPreset != null)
            {
                var total = _entryCarManager.ConnectedCars.Count;
                var yesCount = _onDemandYesCount;
                var noCount = _onDemandNoCount;

                if (total > 0)
                {
                    // Early end: yes has strict majority, or no has at least half so yes cannot reach majority.
                    if (yesCount * 2 > total || noCount * 2 >= total)
                    {
                        break;
                    }
                }

                if ((DateTime.UtcNow - _onDemandLastBroadcastTime).TotalSeconds >= 30)
                {
                    _onDemandLastBroadcastTime = DateTime.UtcNow;
                    var s = Math.Max(0, (int)(_onDemandEndTime - DateTime.UtcNow).TotalSeconds);
                    var abstained = total - _onDemandVoteByClient.Count;
                    _entryCarManager.BroadcastChat($"{_onDemandTargetPreset.Name} vote: {s}s left — Yes: {_onDemandYesCount}, No: {_onDemandNoCount}, —: {abstained}");
                    BroadcastOnDemandVoteOpenUi(s);
                }
            }
        }
        if (_voteState != VoteState.OnDemandVote) return;

        var yes = _onDemandYesCount;
        var no = _onDemandNoCount;
        var target = _onDemandTargetPreset;
        var requester = _onDemandRequester;
        var last = _presetManager.CurrentPreset;

        _voteState = VoteState.Idle;
        _onDemandRequester = null;
        _onDemandTargetPreset = null;
        _onDemandPresetIndex = 0;
        _onDemandVoteByClient.Clear();

        if (target == null || last.Type == null) return;

        if (yes > no)
        {
            _entryCarManager.BroadcastChat($"Vote passed. Next track: {target.Name}");
            _entryCarManager.BroadcastChat($"Track will change in {(_configuration.TransitionDelaySeconds < 60 ? $"{_configuration.TransitionDelaySeconds} second(s)" : $"{(int)Math.Ceiling(_configuration.TransitionDelaySeconds / 60.0)} minute(s)")}.");
            try
            {
                await Task.Delay(_configuration.TransitionDelayMilliseconds, _cancellationToken);
                _presetManager.SetPreset(new PresetData(last.Type, target) { TransitionDuration = _configuration.TransitionDurationSeconds });
            }
            catch (OperationCanceledException) { }
        }
        else
        {
            _entryCarManager.BroadcastChat("Vote ended. Preset unchanged.");
            if (requester != null)
                _requesterCooldownUntil[requester] = DateTime.UtcNow.AddMinutes(_configuration.RequesterCooldownMinutes);
        }
    }

    /// <summary>
    /// Record one player's yes or no during an on-demand vote. New voters add to tallies; returning voters overwrite their map entry and move one count from the old side to the new when the choice differs.
    /// Broadcasts only after a tally change (not when the same choice is sent again).
    /// </summary>
    internal void VoteOnDemand(ChatCommandContext context, bool isYes)
    {
        if (_voteState != VoteState.OnDemandVote)
        {
            context.Reply("There is no ongoing preset vote.");
            return;
        }

        if (!_onDemandVoteByClient.TryGetValue(context.Client, out var previous))
        {
            _onDemandVoteByClient[context.Client] = isYes;
            if (isYes)
                _onDemandYesCount++;
            else
                _onDemandNoCount++;
            context.Reply("Your vote has been counted.");
        }
        else if (previous == isYes)
        {
            context.Reply("Your vote is unchanged.");
            return;
        }
        else
        {
            _onDemandVoteByClient[context.Client] = isYes;
            if (previous)
                _onDemandYesCount--;
            else
                _onDemandNoCount--;
            if (isYes)
                _onDemandYesCount++;
            else
                _onDemandNoCount++;
            context.Reply("Your vote has been updated.");
        }

        if (_onDemandTargetPreset != null)
        {
            _onDemandLastBroadcastTime = DateTime.UtcNow;
            var s = Math.Max(0, (int)(_onDemandEndTime - DateTime.UtcNow).TotalSeconds);
            var abstained = _entryCarManager.ConnectedCars.Count - _onDemandVoteByClient.Count;
            _entryCarManager.BroadcastChat($"{_onDemandTargetPreset.Name} vote: {s}s left — Yes: {_onDemandYesCount}, No: {_onDemandNoCount}, —: {abstained}");
            BroadcastOnDemandVoteOpenUi(s);
        }
    }

    /// <summary>
    /// Cancel the current on-demand vote (admin). Treat as failed: apply requester cooldown, set _voteState = Idle, clear on-demand fields, broadcast.
    /// </summary>
    internal void CancelOnDemandVote(BaseCommandContext context)
    {
        if (_voteState != VoteState.OnDemandVote)
        {
            context.Reply("No on-demand vote in progress.");
            return;
        }
        var requester = _onDemandRequester;
        _voteState = VoteState.Idle;
        _onDemandRequester = null;
        _onDemandTargetPreset = null;
        _onDemandPresetIndex = 0;
        _onDemandVoteByClient.Clear();
        if (requester != null)
            _requesterCooldownUntil[requester] = DateTime.UtcNow.AddMinutes(_configuration.RequesterCooldownMinutes);
        _entryCarManager.BroadcastChat("On-demand vote canceled by admin.");
    }

    /// <summary>
    /// Cancel whatever vote is currently running. If TimerVote call CancelVote; if OnDemandVote call CancelOnDemandVote; else reply "No vote in progress."
    /// </summary>
    internal void CancelCurrentVote(BaseCommandContext context)
    {
        if (_voteState == VoteState.TimerVote)
            CancelVote(context);
        else if (_voteState == VoteState.OnDemandVote)
            CancelOnDemandVote(context);
        else
            context.Reply("No vote in progress.");
    }

    /// <summary>
    /// Admin sets preset by unified index (same order as /presets: public first, then admin-only).
    /// </summary>
    internal void SetPreset(BaseCommandContext context, int choice)
    {
        var last = _presetManager.CurrentPreset;

        if (choice < 0 || choice >= _unifiedPresets.Count)
        {
            Log.Information("Invalid preset choice");
            context.Reply("Invalid preset choice.");
            return;
        }

        var next = _unifiedPresets[choice];

        if (last.Type!.Equals(next))
        {
            Log.Information("No change made, admin tried setting the current preset");
            context.Reply("No change made, you tried setting the current preset.");
        }
        else
        {
            context.Reply($"Switching to preset: {next.Name}");
            _ = AdminPreset(new PresetData(_presetManager.CurrentPreset.Type, next)
            {
                TransitionDuration = _configuration.TransitionDurationSeconds,
            });
        }
    }

    /// <summary>
    /// Admin picks a random other preset from the unified list.
    /// </summary>
    internal void RandomPreset(BaseCommandContext context)
    {
        var last = _presetManager.CurrentPreset;

        PresetType next;
        do
        {
            next = _unifiedPresets[Random.Shared.Next(_unifiedPresets.Count)];
        } while (last.Type!.Equals(next));
        context.Reply($"Switching to random preset: {next.Name}");
        _ = AdminPreset(new PresetData(_presetManager.CurrentPreset.Type, next)
        {
            TransitionDuration = _configuration.TransitionDurationSeconds,
        });
    }

    /// <summary>
    /// Record one player's vote during a timer vote. 1) If _voteState != TimerVote or !_votingOpen, reply "There is no ongoing track vote." and return. 2) If choice &lt; 0 or choice >= _availablePresets.Count, reply "Invalid choice." and return. 3) If context.Client is already in _alreadyVoted, reply "You voted already." and return. 4) Add context.Client to _alreadyVoted. 5) Increment _availablePresets[choice].Votes. 6) Reply "Your vote for {Name} has been counted." using votedPreset.Preset.Name.
    /// </summary>
    internal void CountVote(ChatCommandContext context, int choice)
    {
        if (_voteState != VoteState.TimerVote || !_votingOpen)
        {
            context.Reply("There is no ongoing track vote.");
            return;
        }

        if (choice >= _availablePresets.Count || choice < 0)
        {
            context.Reply("Invalid choice.");
            return;
        }

        if (_alreadyVoted.Contains(context.Client))
        {
            context.Reply("You voted already.");
            return;
        }

        _alreadyVoted.Add(context.Client);

        var votedPreset = _availablePresets[choice];
        votedPreset.Votes++;

        context.Reply($"Your vote for {votedPreset.Preset!.Name} has been counted.");
    }

    /// <summary>
    /// Admin starts a timer vote immediately. 1) If _voteState != Idle, reply "Vote already ongoing." and return. 2) Log "Starting preset vote" and call VotingAsync(_cancellationToken, manualVote: true). Catch TaskCanceledException silently; log other exceptions.
    /// </summary>
    internal void StartVote(BaseCommandContext context)
    {
        if (_voteState != VoteState.Idle)
        {
            context.Reply("Vote already ongoing.");
            return;
        }

        try
        {
            Log.Information("Starting preset vote");
            _ = VotingAsync(_cancellationToken, true);
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during voting preset update");
        }
    }

    /// <summary>
    /// Admin requests vote to finish: set _finishVote = 1 so the running WaitVoting loop will exit and return true (finish). Reply "Finishing vote." to context.
    /// </summary>
    internal void FinishVote(BaseCommandContext context)
    {
        _finishVote = 1;
        context.Reply("Finishing vote.");
    }

    /// <summary>
    /// Admin requests vote to cancel: set _finishVote = -1 so WaitVoting will exit and return false (cancel). Reply "Canceling vote." to context.
    /// </summary>
    internal void CancelVote(BaseCommandContext context)
    {
        _finishVote = -1;
        context.Reply("Canceling vote.");
    }

    /// <summary>
    /// Admin adds extra time to the current vote window: add seconds to _extendVotingSeconds (consumed inside WaitVoting's while loop). Reply to context with the added seconds.
    /// </summary>
    internal void ExtendVote(BaseCommandContext context, int seconds)
    {
        _extendVotingSeconds += seconds;
        context.Reply($"Extending vote for {seconds} more seconds.");
    }

    /// <summary>
    /// Hold the vote window open and let admin finish/cancel or extend. Returns true if vote was finished, false if canceled.
    /// 1) Set _votingOpen = true so CountVote accepts votes. 2) Loop for VotingDurationSeconds: each second check _finishVote; if != 0 break; else await Task.Delay(1000, stoppingToken). 3) While _extendVotingSeconds != 0: copy extend = _extendVotingSeconds, set _extendVotingSeconds = 0, then loop extend seconds (same check _finishVote each second, Delay(1000)). 4) In finally, set _votingOpen = false. 5) result = (_finishVote >= 0); set _finishVote = 0; return result. So 1 = admin called FinishVote, -1 = admin called CancelVote.
    /// </summary>
    private async Task<bool> WaitVoting(CancellationToken stoppingToken)
    {
        try
        {
            _votingOpen = true;

            for (var s = 0; s <= _configuration.VotingDurationSeconds; s++)
            {
                if (_finishVote != 0)
                    break;
                await Task.Delay(1000, stoppingToken);
            }

            while (_extendVotingSeconds != 0)
            {
                var extend = _extendVotingSeconds;
                _extendVotingSeconds = 0;
                for (var s = 0; s <= extend; s++)
                {
                    if (_finishVote != 0)
                        break;
                    await Task.Delay(1000, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException ex)
        {
            Log.Error(ex, "Error while waiting for preset votes");
        }
        finally
        {
            _votingOpen = false;
        }
        var result = _finishVote >= 0;
        _finishVote = 0;
        return result;
    }

    /// <summary>
    /// Run one full timer vote: build options, broadcast, wait for votes, pick winner, apply or stay.
    /// 1) If _voteState != Idle, return immediately. 2) Set _voteState = TimerVote. 3) last = _presetManager.CurrentPreset. 4) Clear _availablePresets and _alreadyVoted. 5) presetsLeft = copy of _publicPresetsOrdered with last.Type removed (so we only offer other presets). If presetsLeft.Count == 0: log "Not enough presets", set Idle, return. If presetsLeft.Count == 1: log and broadcast "only one other preset, changing without a vote", delay and SetPreset, set Idle, return. 6) If EnableVote or manualVote: broadcast "Vote for next track:". 7) If EnableStayOnTrack: add PresetChoice(last.Type, Votes=0) to _availablePresets; if voting enabled broadcast " /vt 0 - Stay on current track.". 8) For i from _availablePresets.Count to VoteChoices-1: if presetsLeft empty break; nextPreset = random from presetsLeft; add PresetChoice(nextPreset, 0) to _availablePresets; remove nextPreset from presetsLeft; if voting enabled broadcast " /vt {i} - {nextPreset.Name}". 9) If EnableVote or manualVote: await WaitVoting(stoppingToken); if it returns false, set _voteState = Idle and return (vote canceled). 10) maxVotes = max of w.Votes in _availablePresets; presets = all PresetChoice where Votes == maxVotes; winner = random from presets. 11) If winner.Preset equals last.Type, or (maxVotes == 0 and !ChangePresetWithoutVotes): broadcast "Staying on track for {IntervalMinutes} more minutes.". 12) Else: broadcast winner and "Track will change in ..."; await Delay(TransitionDelayMilliseconds); call _presetManager.SetPreset(PresetData(last.Type, winner.Preset, TransitionDurationSeconds)). 13) Set _voteState = Idle.
    /// </summary>
    private async Task VotingAsync(CancellationToken stoppingToken, bool manualVote = false)
    {
        if (_voteState != VoteState.Idle) return;

        _voteState = VoteState.TimerVote;

        var last = _presetManager.CurrentPreset;

        _availablePresets.Clear();
        _alreadyVoted.Clear();

        var presetsLeft = new List<PresetType>(_publicPresetsOrdered);
        presetsLeft.RemoveAll(t => t.Equals(last.Type!));
        if (presetsLeft.Count == 0)
        {
            Log.Warning("Not enough presets to start vote");
            _voteState = VoteState.Idle;
            return;
        }
        if (presetsLeft.Count == 1)
        {
            var next = presetsLeft[0];
            Log.Information("Only one other preset found, changing without a vote to {Preset}", next.Name);
            _entryCarManager.BroadcastChat($"Only one other track available. Changing to {next.Name} without a vote.");
            _entryCarManager.BroadcastChat($"Track will change in {(_configuration.TransitionDelaySeconds < 60 ?
                    $"{_configuration.TransitionDelaySeconds} second(s)" :
                    $"{(int)Math.Ceiling(_configuration.TransitionDelaySeconds / 60.0)} minute(s)")}.");
            try
            {
                await Task.Delay(_configuration.TransitionDelayMilliseconds, stoppingToken);
                _presetManager.SetPreset(new PresetData(last.Type!, next) { TransitionDuration = _configuration.TransitionDurationSeconds });
            }
            catch (OperationCanceledException) { }
            _voteState = VoteState.Idle;
            return;
        }

        if (_configuration.EnableVote || manualVote)
            _entryCarManager.BroadcastChat("Vote for next track:");

        if (_configuration.EnableStayOnTrack)
        {
            _availablePresets.Add(new PresetChoice { Preset = last.Type, Votes = 0 });
            if (_configuration.EnableVote || manualVote)
            {
                _entryCarManager.BroadcastChat(" /vote 0 - Stay on current track.");
            }
        }
        for (int i = _availablePresets.Count; i < _configuration.VoteChoices; i++)
        {
            if (presetsLeft.Count < 1)
                break;
            var nextPreset = presetsLeft[Random.Shared.Next(presetsLeft.Count)];
            _availablePresets.Add(new PresetChoice { Preset = nextPreset, Votes = 0 });
            presetsLeft.Remove(nextPreset);

            if (_configuration.EnableVote || manualVote)
            {
                _entryCarManager.BroadcastChat($" /vote {i} - {nextPreset.Name}");
            }
        }

        if (_configuration.EnableVote || manualVote)
        {
            if (!await WaitVoting(stoppingToken))
            {
                _voteState = VoteState.Idle;
                return;
            }
        }

        int maxVotes = _availablePresets.Max(w => w.Votes);
        List<PresetChoice> presets = _availablePresets.Where(w => w.Votes == maxVotes).ToList();

        var winner = presets[Random.Shared.Next(presets.Count)];

        if (last.Type!.Equals(winner.Preset!) || (maxVotes == 0 && !_configuration.ChangePresetWithoutVotes))
        {
            _entryCarManager.BroadcastChat($"Track vote ended. Staying on track for {_configuration.IntervalMinutes} more minutes.");
        }
        else
        {
            _entryCarManager.BroadcastChat($"Track vote ended. Next track: {winner.Preset!.Name} - {winner.Votes} votes");
            _entryCarManager.BroadcastChat($"Track will change in {(_configuration.TransitionDelaySeconds < 60 ?
                    $"{_configuration.TransitionDelaySeconds} second(s)" :
                    $"{(int)Math.Ceiling(_configuration.TransitionDelaySeconds / 60.0)} minute(s)")}.");

            await Task.Delay(_configuration.TransitionDelayMilliseconds, stoppingToken);

            _presetManager.SetPreset(new PresetData(last.Type, winner.Preset)
            {
                TransitionDuration = _configuration.TransitionDurationSeconds,
            });
        }
        _voteState = VoteState.Idle;
    }

    /// <summary>
    /// Apply an admin-requested preset change: broadcast messages then call PresetManager.SetPreset after delay.
    /// 1) If preset.Type equals preset.UpcomingType, return (no change). 2) Log and broadcast "Next track: {UpcomingType.Name}" and "Track will change in {TransitionDelaySeconds or minutes}.". 3) Await Task.Delay(TransitionDelayMilliseconds). 4) Call _presetManager.SetPreset(preset). PresetManager handles the actual restart path (reconnect/kick per config). Catch exceptions and log.
    /// </summary>
    private async Task AdminPreset(PresetData preset)
    {
        try
        {
            if (preset.Type!.Equals(preset.UpcomingType!)) return;
            Log.Information("Next preset: {Preset}", preset.UpcomingType!.Name);
            _entryCarManager.BroadcastChat($"Next track: {preset.UpcomingType!.Name}");
            _entryCarManager.BroadcastChat($"Track will change in {(_configuration.TransitionDelaySeconds < 60 ?
                    $"{_configuration.TransitionDelaySeconds} second(s)" :
                    $"{(int)Math.Ceiling(_configuration.TransitionDelaySeconds / 60.0)} minute(s)")}.");

            await Task.Delay(_configuration.TransitionDelayMilliseconds);

            _presetManager.SetPreset(preset);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during admin preset update");
        }
    }
}
