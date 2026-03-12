# Assetto Corsa vote shortcuts – why we couldn’t capture them

Summary of what we tried and why the in-game vote shortcuts never showed up in our packet captures.

---

## The two vote shortcuts

1. **Vanilla AC**
   Default keys: **Ctrl+Y** (yes) and **Ctrl+N** (no). Built into the game.

2. **Patch (Content Manager / CSP)**
   Often **Y** and **N** without Ctrl. Comes from the “patch” side (e.g. Custom Shaders Patch or Content Manager), not from the Lua SDK.

We tried both. In multiple captures, **neither shortcut produced any client→server vote packets** when we pressed them (with or without an active server vote).

---

## What we know about the protocol

- **Votes are sent over TCP**, not UDP.
  AssettoServer handles client vote packets (types 0x64 = next session, 0x65 = restart, 0x66 = kick) only on the TCP path (`ACTcpClient`). UDP is not used for these.

- **Server “vote started”:**
  The server only has the three built-in vote types (next session, restart, kick). When someone votes, it broadcasts `VoteResponse` (TCP). There is no separate “vote started” packet; the first such broadcast is how clients learn that a vote of that type is running.

- **Client behaviour (observed):**
  The client shortcuts appear to **only send when the game believes a vote of that type is already in progress** (i.e. after the client has seen a server VoteResponse). Pressing Y/N or Ctrl+Y/Ctrl+N when no vote is active did not produce any TCP packets with 0x64/0x65/0x66 in our captures.

---

## What we did

- Captured traffic (Wireshark) with both shortcut schemes (vanilla and patch).
- Checked **TCP** client→server payloads for first byte 0x64, 0x65, 0x66 (and server→client for 0x64/0x65/0x66/0x67).
- Result: **no** such packets in the capture. So in our tests, the client never sent a vote over TCP when we pressed the shortcuts.

Earlier we had only looked at **UDP** (e.g. grouping by UDP payload). That was the wrong protocol; after switching to TCP we still found zero vote packets, which matches “shortcuts don’t send unless a vote has started.”

---

## CSP SDK and votes

The CSP Lua SDK (e.g. `tools/lua-sdk-csp`, `ac_apps`) exposes:

- `ac.getCurrentVoteDetails()` – current vote or nil
- `ac.canCastVote(voteType)` – whether casting is allowed (online, not cooldown)
- `ac.castVote(voteType, vote, carIndex)` – cast or start a vote from Lua (`'restart'|'skip'|'kick'`, yes/no)

So **Lua apps can send votes** via `ac.castVote()`. The two shortcuts (vanilla and patch) are **not** defined in the SDK; they are implemented in the game / patch. The SDK does not expose “when the built-in shortcut fires” or “bind key to vote.” We have no way from Lua to make those exact shortcuts send a packet on keypress; we only know they didn’t send in our captures.

---

## Conclusion

- We **could not get the in-game vote shortcuts to work** in the sense of seeing their packets in a capture.
- Most likely the client (and patch) only send yes/no **after** a vote of that type has been started by the server (VoteResponse received).
- For our **preset vote** we cannot rely on the vanilla or patch Y/N keys to send anything for a custom vote type. Implementation should use **chat commands** (/yes, /no) and, if we add a client UI later, a **CSP app** that calls `ac.castVote()` or a custom client message—not the built-in vote shortcuts.
