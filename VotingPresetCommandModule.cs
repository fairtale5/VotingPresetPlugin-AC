/// <summary>
/// Chat command module for preset voting. All commands delegate to VotingPresetPlugin; this class only maps command names to plugin methods.
///
/// Commands and what they do:
/// - preset (no args): reply with current preset, time left when a vote is in progress, and list of votable presets via _votingPreset.GetPresetListAndHelp(Context).
/// - preset (int choice): start on-demand vote for preset at that index via _votingPreset.StartOnDemandVote(Context, choice).
/// - yes / y: during on-demand vote, record yes via _votingPreset.VoteOnDemand(Context, true).
/// - no / n: during on-demand vote, record no via _votingPreset.VoteOnDemand(Context, false).
/// - votetrack / vt / votepreset / vp / presetvote / pv (int choice): during timer vote only, record vote for option via _votingPreset.CountVote(Context, choice).
/// - presetshow / currentpreset / currentrack: reply with current preset name and folder via _votingPreset.GetPreset(Context).
/// - presetlist / presetget / presets (admin): reply with "List of all presets" and one line per preset via _votingPreset.ListAllPresets(Context).
/// - presetstartvote / presetvotestart (admin): start timer vote now via _votingPreset.StartVote(Context).
/// - presetfinishvote / presetvotefinish (admin): set finish flag so vote ends and result is applied via _votingPreset.FinishVote(Context).
/// - presetcancelvote / presetvotecancel / cancelvote (admin): cancel whatever vote is running (timer or on-demand) via _votingPreset.CancelCurrentVote(Context).
/// - presetextendvote / presetvoteextend (admin, int seconds): add seconds to vote window via _votingPreset.ExtendVote(Context, seconds).
/// - presetset / presetchange / presetuse / presetupdate (admin, int choice): set preset by index via _votingPreset.SetPreset(Context, choice).
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
            _votingPreset.GetPresetListAndHelp(Context);
        else
            _votingPreset.StartOnDemandVote((ChatCommandContext)Context, choice.Value);
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

    [Command("votetrack", "vt", "votepreset", "vp", "presetvote", "pv"), RequireConnectedPlayer]
    public void VotePreset(int choice)
    {
        _votingPreset.CountVote((ChatCommandContext)Context, choice);
    }

    [Command("presetshow", "currentpreset", "currentrack"), RequireConnectedPlayer]
    public void GetCurrentPreset()
    {
        _votingPreset.GetPreset(Context);
    }

    [Command("presetlist", "presetget", "presets"), RequireAdmin]
    public void AdminPresetList()
    {
        _votingPreset.ListAllPresets(Context);
    }

    [Command("presetstartvote", "presetvotestart"), RequireAdmin]
    public void AdminPresetVoteStart()
    {
        _votingPreset.StartVote(Context);
    }

    [Command("presetfinishvote", "presetvotefinish"), RequireAdmin]
    public void AdminPresetVoteFinish()
    {
        _votingPreset.FinishVote(Context);
    }

    [Command("presetcancelvote", "presetvotecancel", "cancelvote"), RequireAdmin]
    public void AdminCancelVote()
    {
        _votingPreset.CancelCurrentVote(Context);
    }

    [Command("presetextendvote", "presetvoteextend"), RequireAdmin]
    public void AdminPresetVoteExtend(int seconds)
    {
        _votingPreset.ExtendVote(Context, seconds);
    }

    [Command("presetset", "presetchange", "presetuse", "presetupdate"), RequireAdmin]
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
