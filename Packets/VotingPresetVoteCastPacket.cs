// =============================================================================
// VotingPresetVoteCastPacket — client → server (CSP OnlineEvent)
// =============================================================================
// Purpose:
//   Lua sends the player’s vote (yes/no for v1). Identity comes only from ACTcpClient in the
//   RegisterOnlineEvent callback — never from packet payload (so nobody can fake another player’s id).
//
// Registration (logical flow — implement in VotingPresetPlugin constructor or startup):
//   1. Inject CSPClientMessageTypeManager (see FastTravelPlugin RegisterOnlineEvent pattern:
//      serv-game/AssettoServer/FastTravelPlugin/FastTravelPlugin.cs).
//   2. If EnableClientMessages (and a plugin flag if we add one), call:
//        cspClientMessageTypeManager.RegisterOnlineEvent<VotingPresetVoteCastPacket>(OnVoteCastFromClient);
//   3. OnVoteCastFromClient lives in VotingPresetPlugin, not in this file. Sketch of what it does:
//        - If state != OnDemandVote (for v1 yes/no) → ignore or log and return.
//        - Else if voteKind is on-demand → read isYes → call VoteOnDemand (same rules as chat /yes /no).
//        - Timer (Soon): voteKind + option index → CountVote (same rules as /vote N); see “Soon” block
//          at bottom of this class.
//        Goal: one truth in VoteOnDemand / CountVote — chat and packet are just two ways in.
//
// Lua sender (later):
//   ac.OnlineEvent({ ac.StructItem.key("VotingPreset_VoteCast"), ... }, nil, ac.SharedNamespace.ServerScript)
//   Third arg = mandatory for client-installed scripts (Solution 1 — see technical doc).
//
// Hash / layout: docs/technical-docs/client-server-communication.md
// =============================================================================

using AssettoServer.Network.ClientMessages;

namespace VotingPresetPlugin.Packets;

// #region = editor fold only
#region VotingPresetVoteCastPacket — real fields for on-demand yes/no; end of class has COMMENTS for future timer vote (no extra fields yet)

// This type = client → server message: "here is my yes or no for the current on-demand vote."
// [OnlineEvent(Key = ...)] tells AssettoServer the CSP packet id. Lua senders must use the same key:
//   ac.StructItem.key("VotingPreset_VoteCast")
// or the hash will not match (see docs/technical-docs/client-server-communication.md).
[OnlineEvent(Key = "VotingPreset_VoteCast")]
public class VotingPresetVoteCastPacket : OnlineEvent<VotingPresetVoteCastPacket>
{
    // #########################################################################
    // V1 — On-demand yes/no only (implement this phase first)
    // Task doc: docs/TASKS.md → §8 “First” + “Agreed layout (packets & scope)”
    // Wire-up: RegisterOnlineEvent handler reads these fields; who voted = ACTcpClient only, not payload.
    // #########################################################################

    /// <summary>0 = on-demand yes/no (v1). Timer phase will use another value (TASKS §8).</summary>
    public const byte VoteKindOnDemandYesNo = 0;

    // VoteKind (byte): 0 = OnDemandYesNo for v1. Other values reserved until timer phase (see “Soon” block below).
    [OnlineEventField(Name = "voteKind")]
    public byte VoteKind;

    // IsYes: true = yes, false = no. Only meaningful when voteKind is on-demand; ignore for timer (Soon).
    [OnlineEventField(Name = "isYes")]
    public bool IsYes;


    // #########################################################################
    // Soon — Timer / multi-option vote (comments only; no code below this banner)
    // Full scope & checklist: docs/TASKS.md §8 First (timer = separate code path from yes/no).
    // #########################################################################
    //
    // When VoteKind = timer (value TBD):
    //   OptionIndex (byte or ushort):
    //     Same numbering as chat /vote <number> and _availablePresets order.
    //   Handler:
    //     If state != TimerVote or !_votingOpen → ignore or log (mirror CountVote guards).
    //     Else CountVote(client, OptionIndex) — one code path with chat.
    //   Optional:
    //     If one packet carries both modes, treat OptionIndex as unused when VoteKind = OnDemandYesNo.
    //
    // Do not implement timer cast handling until §8 First timer checklist items exist and are ready.
    //
    // --- No fields here yet (Soon). ---
}

#endregion
