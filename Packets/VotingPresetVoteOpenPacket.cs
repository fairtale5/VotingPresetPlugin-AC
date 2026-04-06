// =============================================================================
// VotingPresetVoteOpenPacket — server → client (CSP OnlineEvent)
// =============================================================================
// Purpose:
//   Creates and sends a packet to the client to notify that a vote is open.
//   Tells the client that a preset vote is active and the UI may prompt for input.
//   This is sent together with the chat line messages as fallback.
//
// When to send (logical flow — wire-up lives in VotingPresetPlugin later):
//   1. On-demand vote: after StartOnDemandVote validates and sets state, same moment
//      as the public BroadcastChat vote line.
//   2. (Later) Timer vote: when VotingAsync opens options, alongside /vote broadcast.
//   3. (Later) Optional follow-up packets for count/seconds updates if we avoid spamming chat.
//
// Lua (later):
//   Receiver: ac.OnlineEvent({ ac.StructItem.key("VotingPreset_VoteOpen"), ... }, cb, ac.SharedNamespace.ServerScript)
//   Hash / layout: docs/technical-docs/client-server-communication.md
//
// Lua mirror (later):
//   ac.OnlineEvent({ ac.StructItem.key("<same as C# Key>"), ...fields... }, callback, ac.SharedNamespace.ServerScript)
//   Hash must match: see docs/technical-docs/client-server-communication.md
//
// Field layout:
//   Primitives first, then fixed-size strings — AssettoServer reorders; Lua struct must match generated layout.
// =============================================================================

using AssettoServer.Network.ClientMessages;

namespace VotingPresetPlugin.Packets;

// #region = editor fold only
#region VotingPresetVoteOpenPacket — real fields for on-demand yes/no; end of class has COMMENTS for future timer vote (no extra fields yet)

// This type = server → client message: "a preset vote is open; here is the data for the UI."
// [OnlineEvent(Key = ...)] tells AssettoServer the CSP packet id. Lua receivers must use the same key:
//   ac.StructItem.key("VotingPreset_VoteOpen")
// or the hash will not match (see docs/technical-docs/client-server-communication.md).
[OnlineEvent(Key = "VotingPreset_VoteOpen")]
public class VotingPresetVoteOpenPacket : OnlineEvent<VotingPresetVoteOpenPacket>
{
    // #########################################################################
    // V1 — On-demand yes/no only (implement this phase first)
    // Task doc: docs/TASKS.md → §8 “First” + “Agreed layout (packets & scope)”
    // Wire-up: VotingPresetPlugin fills fields when broadcasting (same numbers as the public chat line).
    // #########################################################################

    /// <summary>0 = on-demand yes/no (v1). Other values reserved for timer phase (TASKS §8).</summary>
    public const byte VoteKindOnDemandYesNo = 0;

    // VoteKind (byte): 0 = OnDemandYesNo for v1. Other values reserved until timer phase (see “Soon” block below).
    [OnlineEventField(Name = "voteKind")]
    public byte VoteKind;

    // SecondsRemaining: UI countdown; set from VotingDurationSeconds at start or from (_onDemandEndTime - now).
    [OnlineEventField(Name = "secondsRemaining")]
    public ushort SecondsRemaining;

    // Yes / no / abstained: same meaning as chat (“Yes: …, No: …, —: …”).
    [OnlineEventField(Name = "yesCount")]
    public ushort YesCount;

    [OnlineEventField(Name = "noCount")]
    public ushort NoCount;

    [OnlineEventField(Name = "abstainedCount")]
    public ushort AbstainedCount;

    // VotableIndex: same index as /preset <n> for UI labels.
    [OnlineEventField(Name = "votableIndex")]
    public byte VotableIndex;

    // Preset display name: fixed-size on wire (128); target preset for this on-demand vote.
    [OnlineEventField(Name = "presetName", Size = 128)]
    public string PresetName = "";


    // #########################################################################
    // Soon — Timer / multi-option vote (comments only; no code below this banner)
    // Full scope & checklist: docs/TASKS.md §8 First (timer = separate from on-demand code path).
    // #########################################################################
    //
    // When VoteKind indicates timer vote (value TBD, e.g. 1 = TimerMultiChoice):
    //   - Send from VotingAsync when timer vote opens, alongside existing /vote N chat lines.
    //   - Payload ideas (pick one design when implementing — document in TASKS before coding):
    //       • optionCount (byte) + repeated (optionIndex, fixed name) in one packet, or
    //       • optionCount + first batch only + follow-up “option line” packets, or
    //       • minimal open packet + “names in chat only” for v2 (product choice).
    //   - Include seconds remaining for the timer vote window (same idea as on-demand).
    //   - Optional: “stay on track” as index 0 when EnableStayOnTrack — mirror chat semantics.
    //
    // Do not implement timer fields or branches until §8 First timer rows are checked off in TASKS.
    //
    // --- No fields here yet (Soon). ---
}

#endregion
