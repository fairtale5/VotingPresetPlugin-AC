# VotingPresetPlugin docs

Preset (track/config) voting: **timer every 60 min** plus **on-demand votes any time**. In-game chat only; no client app required.

---

## Behaviour (target)

1. **Timer (e.g. every 60 min):** Server starts a vote with N preset options. Players vote by number (`/vt 0`, `/vt 1`, …). Winner applied when vote ends. (Existing behaviour.)
2. **On-demand (any time):** Any player can start a vote with `/preset <number>`. Server broadcasts: “Vote to change to &lt;PresetName&gt;. Type /yes or /no. Ends in Ns.” Others reply **/yes** or **/no**. One vote at a time.
3. **After a rejected vote:** The player who started it cannot start another on-demand vote for **15 minutes** (configurable per server: `RequesterCooldownMinutes`).

---

## Docs in this folder

| Doc | Purpose |
|-----|--------|
| **COMMANDS.md** | Command reference (current vs target). |
| **STRUCTURE.md** | File structure and separation of concerns (C# and docs). |
| **TASKS.md** | Task list and objectives. Start here to implement. |
| **AC_VOTE_SHORTCUTS.md** | Why we use chat only (in-game vote shortcuts didn’t send in our tests). |

---

## Quick links

- **What to build:** COMMANDS.md (target commands) + TASKS.md (checklist).
- **Where to put code:** STRUCTURE.md.
- **Config:** RequesterCooldownMinutes (and timer interval, vote duration, pass rule) in one config; see TASKS.md and STRUCTURE.md.
