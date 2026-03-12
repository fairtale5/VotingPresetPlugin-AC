/// <summary>
/// Chat command module for preset voting. All commands delegate to VotingPresetPlugin; this class only maps command names to plugin methods.
///
/// Commands and what they do (final names, no aliases):
/// - preset (no args): reply with current preset name only via _votingPreset.GetPreset(Context); include hint to use /presets.
/// - preset (int choice): start on-demand vote for preset at that index via _votingPreset.StartOnDemandVote(Context, choice).
/// - yes / y: during on-demand vote, record yes via _votingPreset.VoteOnDemand(Context, true).
/// - no / n: during on-demand vote, record no via _votingPreset.VoteOnDemand(Context, false).
/// - vote (int choice): during timer vote only, record vote for option via _votingPreset.CountVote(Context, choice).
/// - presets: reply with votable list (and if admin, full list for presetset) via _votingPreset.ListAllPresets(Context).
/// - votestart (admin): start timer vote now via _votingPreset.StartVote(Context).
/// - voteend (admin): set finish flag so vote ends and result is applied via _votingPreset.FinishVote(Context).
/// - votecancel (admin): cancel whatever vote is running (timer or on-demand) via _votingPreset.CancelCurrentVote(Context).
/// - votetimeadd (admin, int seconds): add seconds to vote window via _votingPreset.ExtendVote(Context, seconds).
/// - presetset (admin, int choice): set preset by index via _votingPreset.SetPreset(Context, choice).
/// - presetrandom (admin): switch to a random other preset via _votingPreset.RandomPreset(Context).
/// </summary>
using AssettoServer.Commands;
using AssettoServer.Commands.Attributes;
using AssettoServer.Commands.Contexts;
using Qmmands;

namespace VotingPresetPlugin;

public class VotingPresetCommandModule : ACModuleBase
{
    private readonly VotingPresetPlugin _votingPreset;

    public VotingPresetCommandModule(VotingPresetPlugin votingPreset)
    {
        _votingPreset = votingPreset;
    }

    [Command("preset"), RequireConnectedPlayer]
    public void Preset(int? choice = null)
    {
        if (choice == null)
        {
            _votingPreset.GetPreset(Context);
        }
        else
        {
            _votingPreset.StartOnDemandVote((ChatCommandContext)Context, choice.Value);
        }
    }

    [Command("yes", "y"), RequireConnectedPlayer]
    public void VoteYes()
    {
        _votingPreset.VoteOnDemand((ChatCommandContext)Context, true);
    }

    [Command("no", "n"), RequireConnectedPlayer]
    public void VoteNo()
    {
        _votingPreset.VoteOnDemand((ChatCommandContext)Context, false);
    }

    [Command("vote"), RequireConnectedPlayer]
    public void VotePreset(int choice)
    {
        _votingPreset.CountVote((ChatCommandContext)Context, choice);
    }

    [Command("presets"), RequireConnectedPlayer]
    public void PresetList()
    {
        _votingPreset.ListAllPresets(Context);
    }

    [Command("votestart"), RequireAdmin]
    public void AdminPresetVoteStart()
    {
        _votingPreset.StartVote(Context);
    }

    [Command("voteend"), RequireAdmin]
    public void AdminPresetVoteFinish()
    {
        _votingPreset.FinishVote(Context);
    }

    [Command("votecancel"), RequireAdmin]
    public void AdminCancelVote()
    {
        _votingPreset.CancelCurrentVote(Context);
    }

    [Command("votetimeadd"), RequireAdmin]
    public void AdminPresetVoteExtend(int seconds)
    {
        _votingPreset.ExtendVote(Context, seconds);
    }

    [Command("presetset"), RequireAdmin]
    public void AdminPresetSet(int choice)
    {
        _votingPreset.SetPreset(Context, choice);
    }

    [Command("presetrandom"), RequireAdmin]
    public void AdminPresetRandom()
    {
        _votingPreset.RandomPreset(Context);
    }
}
