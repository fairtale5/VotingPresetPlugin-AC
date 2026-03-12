# VotingPresetPlugin – tasks and objectives

Single checklist. Order is a suggested implementation order.

---

## Command availability: /yes and /no

- **This repo:** No other plugin registers `/yes` or `/no`. Safe to use.
- **Your server:** If you run other plugins (not in this repo), check they don’t use `/yes` or `/no`. If conflicted, use `/vote yes` and `/vote no` instead and document in config/COMMANDS.

---

## Config

- [x] Add **RequesterCooldownMinutes** (default 15). Applied when an on-demand vote **fails**; that requester cannot start another on-demand vote for this many minutes. *(Done: added to VotingPresetConfiguration.cs and VotingPresetConfigurationValidator.cs.)*
- [ ] Keep timer interval (e.g. 60 min), vote duration, pass rule in one config file. Tweakable per server.

---

## Command simplification (what to remove and what to keep)

When editing `VotingPresetCommandModule.cs`, apply this exactly:

**Remove from the module (do not keep as aliases):**
- `[Command("votetrack", "vt", "votepreset", "vp", "presetvote", "pv")]` → **replace with** a single **/vote** command that takes an optional argument (number or name). Timer vote uses /vote &lt;n&gt;; on-demand start uses /vote &lt;n&gt; or /vote &lt;name&gt;.
- `[Command("presetshow", "currentpreset", "currentrack")]` → **remove**. Behaviour moves into **/vote** (no args).
- `[Command("presetlist", "presetget", "presets")]` → **keep only** `[Command("presets")]` (drop presetlist, presetget).
- `[Command("presetset", "presetchange", "presetuse", "presetupdate")]` → **keep only** `[Command("presetset")]` (drop presetchange, presetuse, presetupdate).

**Keep (single name each):** Timer admin: presetstartvote, presetfinishvote, presetcancelvote, presetextendvote, presetrandom — no change.

**Add:** `[Command("vote")]` (no args / number / name); `[Command("yes", "y")]`, `[Command("no", "n")]`; `[Command("cancelvote")]` (admin, on-demand only).

**Final command list in code (no extra aliases):** vote, yes, y, no, n, presets, presetset, presetstartvote, presetfinishvote, presetcancelvote, presetextendvote, presetrandom, cancelvote.

---

## Commands to implement / change

- [x] **/preset** (no args): Reply with current preset, list of votable presets (number + name), help: “/vote &lt;number or name&gt; to start a vote. /yes or /no to vote.”
- [x] **/preset &lt;number&gt;**: Start on-demand vote for preset at that index (votable list). Reject if vote already running, invalid index, or requester on cooldown. *(Done: StartOnDemandVote + RunOnDemandVoteAsync.)*
- [ ] **/preset &lt;name&gt;** (optional later): Start on-demand vote by preset name (case-insensitive, single match).
- [x] **/yes** (alias **/y**): During on-demand vote only: vote yes. *(Done: VoteOnDemand(context, true).)*
- [x] **/no** (alias **/n**): During on-demand vote only: vote no. *(Done: VoteOnDemand(context, false).)*
- [ ] **/presets** (admin): List all presets (votable + admin-only). Single name only (see "Command simplification" above).
- [ ] **/presetset &lt;number&gt;** (admin): Set preset by index. No vote. Single name only (see "Command simplification" above).
- [x] **/cancelvote** (admin): Cancel current on-demand vote. *(Done: CancelOnDemandVote.)*
- [x] Timer admin (unchanged): presetstartvote, presetfinishvote, presetcancelvote, presetextendvote, presetrandom.
- [ ] **Apply simplification:** In the command module, remove old aliases and folded commands exactly as in "Command simplification" above.

---

## Vote flow

- [x] **One vote at a time:** Either timer vote is live, or on-demand is live, or idle. Never both. *(Done: _voteState enum and checks in StartOnDemandVote and CountVote.)*
- [x] **Timer (interval):** Existing behaviour. On interval, start timer vote with N presets; players use /vt &lt;n&gt; to pick option; admin can finish/cancel/extend.
- [x] **On-demand (any time):** Any connected player can start with /preset &lt;number&gt; or /vote &lt;name&gt;. When started: broadcast chat “Vote to change to &lt;PresetName&gt;. Type /yes or /no. Ends in Ns.” Count /yes and /no. On end: pass → PresetManager.SetPreset + message; fail → “Vote failed. Preset unchanged.” Apply **requester cooldown** (RequesterCooldownMinutes) to the player who started the vote.
- [x] **Cooldown:** Only after a **rejected** (failed) on-demand vote or admin cancel. Block that player from starting a new on-demand vote for RequesterCooldownMinutes. *(Done: _requesterCooldownUntil in RunOnDemandVoteAsync and CancelOnDemandVote.)*

---

## Preset resolution

- [ ] By number: index in votable list (same as current).
- [ ] By name: case-insensitive match; must match **exactly one** preset; else error “No unique preset matching ‘…’”.

---

## File structure and wiring

- [ ] One command module (all commands in one place). See STRUCTURE.md.
- [x] One vote state/service: Idle | TimerVoteLive | OnDemandLive. No duplicate state. *(Done: VoteState enum and _voteState in VotingPresetPlugin.cs; on-demand and cooldown fields added; _voteStarted replaced with _voteState; early returns in VotingAsync set _voteState = Idle.)*
- [x] Cooldown logic in one place, driven by config RequesterCooldownMinutes. *(Done: applied in RunOnDemandVoteAsync on fail and in CancelOnDemandVote.)*

---

## Optional later

- [ ] Implement some form of quorum or proper voting method to avoid abuse.
- [ ] Requester confirm step (“Confirm start vote? (yes/no)”) before going live.
- [x] Periodic status message during on-demand vote (e.g. “30s left, Yes: 3, No: 1”).
- [ ] Client Lua UI for yes/no (chat commands remain primary).
- [ ] Allow cancel command (presetcancelvote / cancelvote) for non-admin users when they initiated the current on-demand vote.
