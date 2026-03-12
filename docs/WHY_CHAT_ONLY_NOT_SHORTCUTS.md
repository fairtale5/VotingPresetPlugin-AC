# Why we use chat only (and haven’t used keyboard shortcuts yet)

This doc explains why preset voting uses **chat commands** (/yes, /no) instead of in-game vote shortcuts (e.g. Y/N or Ctrl+Y/Ctrl+N). We couldn’t get the shortcuts to show up in our packet captures; we may revisit this later.

---

## The two vote shortcuts

1. **Vanilla AC**  
   Default keys: **Ctrl+Y** (yes) and **Ctrl+N** (no). Built into the game.

2. **Patch (Content Manager / CSP)**  
   Often **Y** and **N** without Ctrl. Comes from the patch (CSP/CM), not from the Lua SDK.

We tried both. In multiple captures, **neither shortcut produced any client→server vote packets** when we pressed them (with or without an active server vote).

---

## What we know about the protocol

- **Votes are sent over TCP**, not UDP.  
  AssettoServer handles client vote packets (types 0x64 = next session, 0x65 = restart, 0x66 = kick) on the TCP path (`ACTcpClient`).

- **Server “vote started”:**  
  The server has the three built-in vote types. When someone votes, it broadcasts `VoteResponse` (TCP). There is no separate “vote started” packet; the first such broadcast is how clients learn that a vote of that type is running.

- **Client behaviour (observed):**  
  The client shortcuts appear to **only send when the game believes a vote of that type is already in progress** (i.e. after the client has seen a server VoteResponse). Pressing Y/N or Ctrl+Y/Ctrl+N when no vote was active did not produce any TCP packets with 0x64/0x65/0x66 in our captures.

---

## What we did

- Captured traffic (Wireshark) with both shortcut schemes.
- Checked **TCP** client→server payloads for first byte 0x64, 0x65, 0x66 (and server→client for 0x64/0x65/0x66/0x67).
- Result: **no** such packets. So in our tests, the client never sent a vote over TCP when we pressed the shortcuts.

---

## CSP SDK and votes

The CSP Lua SDK exposes:

- `ac.getCurrentVoteDetails()` – current vote or nil  
- `ac.canCastVote(voteType)` – whether casting is allowed  
- `ac.castVote(voteType, vote, carIndex)` – cast or start a vote from Lua (`'restart'|'skip'|'kick'`, yes/no)

So **Lua apps can send votes** via `ac.castVote()`. The built-in shortcuts (vanilla and patch) are **not** defined in the SDK; they are in the game/patch. The SDK does not expose “when the built-in shortcut fires” or “bind key to vote.”

---

## Conclusion

- We **could not get the in-game vote shortcuts to work** in the sense of seeing their packets in a capture.
- For our **preset vote** we use **chat commands** (/yes, /no). A future task is to re-investigate whether shortcuts or a CSP Lua UI can be used once a preset vote is in progress (see main README and TASKS.md).
