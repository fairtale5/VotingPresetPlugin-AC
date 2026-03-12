/// <summary>
/// VotingPresetPlugin – timer-based preset (track/config) voting.
///
/// Purpose:
/// Runs a recurring timer vote: every IntervalMinutes the server offers a set of presets;
/// players vote by number (/vt 0, /vt 1, …). Winner is applied after the vote ends.
/// Admins can list presets, set preset directly, start/finish/cancel/extend the timer vote.
///
/// How it works:
/// 1. Preset lists: PresetConfigurationManager (injected) provides VotingPresetTypes (_votePresets)
///    and AllPresetTypes (_adminPresets). Those are loaded from the presets folder and
///    plugin_voting_preset_cfg.yml by the manager; this plugin only reads them.
/// 2. Timer loop: ExecuteAsync waits (IntervalMilliseconds − VotingDurationMilliseconds), then
///    calls VotingAsync(stoppingToken) if EnableVote is true. So the vote window is the last
///    VotingDurationSeconds of each interval.
/// 3. Starting a vote: VotingAsync builds _availablePresets from _votePresets (current preset
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
/// 6. State: _voteState (Idle | TimerVote | OnDemandVote) ensures only one vote at a time; _alreadyVoted prevents double-vote per client. _finishVote and _extendVotingSeconds are the only way admin can end or extend the wait inside WaitVoting. On-demand: StartOnDemandVote (from /preset &lt;number&gt;) sets OnDemandVote, RunOnDemandVoteAsync runs the timer and reminders; /yes and /no call VoteOnDemand. Cooldown applied to requester when on-demand vote fails or is canceled.
/// </summary>
using System.Reflection;
using AssettoServer.Commands.Contexts;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using AssettoServer.Utils;
using Microsoft.Extensions.Hosting;
using Serilog;
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

    /// <summary>Presets that may appear in a timer vote (from PresetConfigurationManager.VotingPresetTypes).</summary>
    private readonly List<PresetType> _votePresets;
    /// <summary>Clients who have already cast a vote in the current timer vote (used to block double-vote).</summary>
    private readonly List<ACTcpClient> _alreadyVoted = new();
    /// <summary>Current vote options: each PresetChoice holds a PresetType and its vote count; built at vote start and updated by CountVote.</summary>
    private readonly List<PresetChoice> _availablePresets = new();
    /// <summary>True while WaitVoting is running; when true, CountVote accepts votes and _alreadyVoted is used. Used only for the timer vote (interval or admin-started); the on-demand vote does not use this flag.</summary>
    private bool _votingOpen = false;

    /// <summary>All presets (votable + admin-only) from PresetConfigurationManager.AllPresetTypes; used for /presetlist and admin SetPreset/RandomPreset by index.</summary>
    private readonly List<PresetType> _adminPresets;

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
    /// <summary>Clients who have already voted in the current on-demand vote (one vote per client).</summary>
    private readonly List<ACTcpClient> _onDemandVoted = new();

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
    /// 2) Load _votePresets from presetConfigurationManager.VotingPresetTypes and _adminPresets from presetConfigurationManager.AllPresetTypes (manager loads from presets folder and plugin config).
    /// 3) Require CSP ≥ 0.2.0 (2651); throw ConfigurationException otherwise.
    /// 4) Apply initial preset: call _presetManager.SetPreset with presetConfigurationManager.CurrentConfiguration.ToPresetType(), IsInit = true, TransitionDuration = 0.
    /// 5) If acServerConfiguration.Extra.EnableClientMessages and _configuration.EnableReconnect: load embedded resource "VotingPresetPlugin.lua.reconnectclient.lua" via Assembly.GetExecutingAssembly().GetManifestResourceStream, then scriptProvider.AddScript(stream, "reconnectclient.lua"); add CSP feature "FREQUENT_TRACK_CHANGES" to cspFeatureManager.
    /// </summary>
    public VotingPresetPlugin(VotingPresetConfiguration configuration,
        PresetConfigurationManager presetConfigurationManager, 
        ACServerConfiguration acServerConfiguration,
        EntryCarManager entryCarManager,
        PresetManager presetManager,
        CSPServerScriptProvider scriptProvider,
        CSPFeatureManager cspFeatureManager)
    {
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _presetManager = presetManager;

        // Preset lists come from PresetConfigurationManager (loads presets folder / plugin_voting_preset_cfg.yml)
        _votePresets = presetConfigurationManager.VotingPresetTypes;
        _adminPresets = presetConfigurationManager.AllPresetTypes;

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
    /// Reply to context with all admin presets: one line "List of all presets:", then for each index i in _adminPresets reply " /presetuse {i} - {Name}" (Name = _adminPresets[i].Name).
    /// </summary>
    internal void ListAllPresets(BaseCommandContext context)
    {
        context.Reply("List of all presets:");
        for (int i = 0; i < _adminPresets.Count; i++)
        {
            var pt = _adminPresets[i];
            context.Reply($" /presetuse {i} - {pt.Name}");
        }
    }

    /// <summary>
    /// Reply with current preset: read _presetManager.CurrentPreset.Type.Name and .PresetFolder, log them and send same text to context.Reply.
    /// </summary>
    internal void GetPreset(BaseCommandContext context)
    {
        Log.Information("Current preset: {Name} - {PresetFolder}", _presetManager.CurrentPreset.Type!.Name, _presetManager.CurrentPreset.Type!.PresetFolder);
        context.Reply($"Current preset: {_presetManager.CurrentPreset.Type!.Name} - {_presetManager.CurrentPreset.Type!.PresetFolder}");
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
            var abstained = _entryCarManager.ConnectedCars.Count - _onDemandVoted.Count;
            context.Reply($"Vote in progress for {_onDemandTargetPreset.Name}. {s}s left. /yes /no — Yes: {_onDemandYesCount}, No: {_onDemandNoCount}, —: {abstained}");
        }
        else if (_voteState == VoteState.TimerVote)
        {
            context.Reply("Timer vote in progress. Use /vt <number> to vote.");
        }

        context.Reply("Votable presets:");
        for (int i = 0; i < _votePresets.Count; i++)
            context.Reply($" {i} - {_votePresets[i].Name}");
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
        if (presetIndex < 0 || presetIndex >= _votePresets.Count)
        {
            context.Reply("Invalid preset number. Use /preset to see the list.");
            return;
        }
        if (_requesterCooldownUntil.TryGetValue(context.Client, out var until) && until > DateTime.UtcNow)
        {
            var minutes = (int)Math.Ceiling((until - DateTime.UtcNow).TotalMinutes);
            context.Reply($"You cannot start another vote yet. Cooldown ends in {minutes} min.");
            return;
        }

        var target = _votePresets[presetIndex];
        if (target.Equals(_presetManager.CurrentPreset.Type))
        {
            context.Reply("Current preset is already this track.");
            return;
        }

        _voteState = VoteState.OnDemandVote;
        _onDemandRequester = context.Client;
        _onDemandTargetPreset = target;
        _onDemandEndTime = DateTime.UtcNow.AddSeconds(_configuration.VotingDurationSeconds);
        _onDemandVoted.Clear();
        _onDemandVoted.Add(context.Client);
        _onDemandYesCount = 1;
        _onDemandNoCount = 0;

        var abstained = _entryCarManager.ConnectedCars.Count - 1;
        _entryCarManager.BroadcastChat($"Change to {target.Name}? /yes /no ({_configuration.VotingDurationSeconds}s) — Yes: 1, No: 0, —: {abstained}");
        _ = RunOnDemandVoteAsync();
    }

    /// <summary>
    /// Background loop for the current on-demand vote: wait until _onDemandEndTime, broadcast a reminder every 30s with time left and counts; then evaluate yes vs no, apply preset or cooldown, set _voteState = Idle.
    /// </summary>
    private async Task RunOnDemandVoteAsync()
    {
        var lastReminder = DateTime.UtcNow;
        while (DateTime.UtcNow < _onDemandEndTime && _voteState == VoteState.OnDemandVote)
        {
            await Task.Delay(1000, _cancellationToken);
            if (_voteState != VoteState.OnDemandVote) return;
            if ((DateTime.UtcNow - lastReminder).TotalSeconds >= 30 && _onDemandTargetPreset != null)
            {
                lastReminder = DateTime.UtcNow;
                var s = Math.Max(0, (int)(_onDemandEndTime - DateTime.UtcNow).TotalSeconds);
                var abstained = _entryCarManager.ConnectedCars.Count - _onDemandVoted.Count;
                _entryCarManager.BroadcastChat($"{_onDemandTargetPreset.Name} vote: {s}s left — Yes: {_onDemandYesCount}, No: {_onDemandNoCount}, —: {abstained}");
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
        _onDemandVoted.Clear();

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
    /// Record one player's yes or no during an on-demand vote. Reject if not OnDemandVote or already voted; else add to _onDemandVoted and increment _onDemandYesCount or _onDemandNoCount.
    /// </summary>
    internal void VoteOnDemand(ChatCommandContext context, bool isYes)
    {
        if (_voteState != VoteState.OnDemandVote)
        {
            context.Reply("There is no ongoing preset vote.");
            return;
        }
        if (_onDemandVoted.Contains(context.Client))
        {
            context.Reply("You voted already.");
            return;
        }
        _onDemandVoted.Add(context.Client);
        if (isYes)
            _onDemandYesCount++;
        else
            _onDemandNoCount++;
        context.Reply("Your vote has been counted.");
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
        _onDemandVoted.Clear();
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
    /// Admin sets preset by index. 1) last = _presetManager.CurrentPreset. 2) If choice &lt; 0 or choice >= _adminPresets.Count, reply "Invalid preset choice." and return. 3) next = _adminPresets[choice]. 4) If last.Type equals next, reply no change. 5) Else reply "Switching to preset: {next.Name}" and call AdminPreset(PresetData(last.Type, next, TransitionDurationSeconds from config)).
    /// </summary>
    internal void SetPreset(BaseCommandContext context, int choice)
    {
        var last = _presetManager.CurrentPreset;

        if (choice < 0 && choice >= _adminPresets.Count)
        {
            Log.Information("Invalid preset choice");
            context.Reply("Invalid preset choice.");

            return;
        }

        var next = _adminPresets[choice];

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
    /// Admin picks a random other preset. 1) last = _presetManager.CurrentPreset. 2) Draw next from _adminPresets at Random.Shared.Next(_adminPresets.Count) until next is not equal to last.Type. 3) Reply "Switching to random preset: {next.Name}" and call AdminPreset(PresetData(current, next, TransitionDurationSeconds)).
    /// </summary>
    internal void RandomPreset(BaseCommandContext context)
    {
        var last = _presetManager.CurrentPreset;

        PresetType next;
        do
        {
            next = _adminPresets[Random.Shared.Next(_adminPresets.Count)];
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
    /// 1) If _voteState != Idle, return immediately. 2) Set _voteState = TimerVote. 3) last = _presetManager.CurrentPreset. 4) Clear _availablePresets and _alreadyVoted. 5) presetsLeft = copy of _votePresets with last.Type removed (so we only offer other presets). If presetsLeft.Count == 0: log "Not enough presets", set Idle, return. If presetsLeft.Count == 1: log and broadcast "only one other preset, changing without a vote", delay and SetPreset, set Idle, return. 6) If EnableVote or manualVote: broadcast "Vote for next track:". 7) If EnableStayOnTrack: add PresetChoice(last.Type, Votes=0) to _availablePresets; if voting enabled broadcast " /vt 0 - Stay on current track.". 8) For i from _availablePresets.Count to VoteChoices-1: if presetsLeft empty break; nextPreset = random from presetsLeft; add PresetChoice(nextPreset, 0) to _availablePresets; remove nextPreset from presetsLeft; if voting enabled broadcast " /vt {i} - {nextPreset.Name}". 9) If EnableVote or manualVote: await WaitVoting(stoppingToken); if it returns false, set _voteState = Idle and return (vote canceled). 10) maxVotes = max of w.Votes in _availablePresets; presets = all PresetChoice where Votes == maxVotes; winner = random from presets. 11) If winner.Preset equals last.Type, or (maxVotes == 0 and !ChangePresetWithoutVotes): broadcast "Staying on track for {IntervalMinutes} more minutes.". 12) Else: broadcast winner and "Track will change in ..."; await Delay(TransitionDelayMilliseconds); call _presetManager.SetPreset(PresetData(last.Type, winner.Preset, TransitionDurationSeconds)). 13) Set _voteState = Idle.
    /// </summary>
    private async Task VotingAsync(CancellationToken stoppingToken, bool manualVote = false)
    {
        if (_voteState != VoteState.Idle) return;

        _voteState = VoteState.TimerVote;

        var last = _presetManager.CurrentPreset;

        _availablePresets.Clear();
        _alreadyVoted.Clear();

        var presetsLeft = new List<PresetType>(_votePresets);
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
                _entryCarManager.BroadcastChat(" /vt 0 - Stay on current track.");
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
                _entryCarManager.BroadcastChat($" /vt {i} - {nextPreset.Name}");
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
