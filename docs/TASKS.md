# VotingPresetPlugin – plan and tasks

One document: plan (command simplification, 30s reminder, early end) and implementation checklist.

---

## 1. Command simplification (plan)

**Before (old names, many aliases):** votetrack/vt/votepreset/vp/presetvote/pv, presetshow/currentpreset/currentrack, presetlist/presetget/presets, presetstartvote/presetvotestart, presetfinishvote/presetvotefinish, presetcancelvote/presetvotecancel/cancelvote, presetextendvote/presetvoteextend, presetset/presetchange/presetuse/presetupdate, presetrandom.

**After (single names, no aliases):**

| Old | New | Notes |
|-----|-----|--------|
| votetrack, vt, votepreset, vp, presetvote, pv | **/vote &lt;number&gt;** | Timer vote only: cast vote for option by that number. |
| presetshow, currentpreset, currentrack | **/preset** (no args) | Show **current preset only**. Include help: “Use /presets to list all presets.” Do **not** list votable presets. |
| presetlist, presetget, presets | **/presets** | List all presets (number + name). Include help: “Use /preset &lt;number&gt; to start a vote to switch to that preset.” |
| /preset &lt;number&gt; | **/preset &lt;number&gt;** | Unchanged. Start on-demand vote. |
| presetstartvote, presetvotestart | **/votestart** | Admin: start timer vote now. |
| presetfinishvote, presetvotefinish | **/voteend** | Admin: end timer vote and apply result. |
| presetcancelvote, presetvotecancel, cancelvote | **/votecancel** | Admin: cancel current vote (timer or on-demand). |
| presetextendvote, presetvoteextend | **/votetimeadd &lt;seconds&gt;** | Admin: add seconds to vote window. |
| presetset (drop presetchange, presetuse, presetupdate) | **/presetset** | Admin: set preset by index. No aliases. |
| presetrandom | **/presetrandom** | Admin: random other preset. Keep as is. |

**/yes** and **/no** (and **/y**, **/n**) unchanged for on-demand vote.

**Code change:** In `VotingPresetCommandModule.cs`, register only the new command names above. Remove all old aliases. Wire each new name to the existing plugin method (CountVote, GetPresetListAndHelp → preset no-args, ListAllPresets, StartVote, FinishVote, CancelVote/CancelOnDemandVote, ExtendVote, SetPreset, RandomPreset, VoteOnDemand).

**Behaviour change for /preset (no args):** Reply with current preset name only and “Use /presets to list all presets.” Do **not** include the list of votable presets with numbers. (Who can call /presets and whether players need to see numbers elsewhere is an implementation choice; see “Open point” below.)

---

## 2. On-demand vote: 30s reminder (plan)

**Current:** Reminder is sent every 30s from **vote start** (loop checks `(DateTime.UtcNow - lastReminder).TotalSeconds >= 30` and sets `lastReminder = DateTime.UtcNow` when sending). So it’s already “30s since last reminder” in code, but **we don’t broadcast when someone votes** — only a private reply to the voter. So the “last update” for everyone is only the previous reminder or the vote-start message.

**Target:** “30 seconds since the **last update**” — where “update” = any **broadcast** to all (vote start, or a status line with counts). So:
- When a player votes (**/yes** or **/no**), **broadcast** the current counts to everyone (e.g. “Yes: 2, No: 1, 45s left.”). That broadcast is the “last update”; reset the 30s reminder timer.
- Reminder logic: send the reminder only when `(now - lastBroadcastTime) >= 30` seconds. Update `lastBroadcastTime` whenever we broadcast (vote start, vote-count update, or the reminder itself).

**Implementation:** In `VoteOnDemand`, after updating counts, broadcast one line (time left + Yes/No counts). In `RunOnDemandVoteAsync`, maintain `lastBroadcastTime` (or equivalent): set it at vote start (when we broadcast “Vote to change to…”), set it when we broadcast from `VoteOnDemand`, and set it when we send the 30s reminder. Send reminder only when `(DateTime.UtcNow - lastBroadcastTime).TotalSeconds >= 30`.

---

## 3. On-demand vote: early end (plan)

**Target:** End the on-demand vote as soon as the result cannot change:
- **Yes wins early:** `yes_count > total_online / 2` (strict majority of connected players).
- **No wins early:** It is impossible for yes to reach majority even if all remaining players vote yes. With `remaining = total_online - yes_count - no_count`, that is when `yes_count + remaining <= total_online / 2`, i.e. when **no_count >= total_online / 2** (no already has half or more).

**Definition of total_online:** Use **connected cars count** (e.g. `_entryCarManager.ConnectedCars.Count`), i.e. everyone who could vote.

**Implementation:** In the on-demand loop (and/or when we process a vote in `VoteOnDemand`), after updating counts:
1. Compute `total_online`, `yes_count`, `no_count`, `remaining`.
2. If `yes_count > total_online / 2` → treat as yes won; exit loop, apply preset, set Idle.
3. If `no_count >= total_online / 2` → treat as no won; exit loop, apply cooldown, set Idle.
4. Otherwise continue until end time or next early-end check.

**Tie / exact half:** If we ever have `yes_count == no_count == total_online/2`, we can either end early as “no wins” (no change) or wait for timer; plan is to end when result is decided, so “no wins” when `no_count >= total_online/2` is consistent.

---

## 4. Open point (to decide in implementation)

- **/presets** today is admin-only. If it stays admin-only, players don’t see which **number** to use for **/preset &lt;number&gt;** unless we show votable numbers somewhere else (e.g. in **/preset** no-args, or in the on-demand vote start message). Options: (A) Make **/presets** available to all so everyone can see the list and the numbers, or (B) Keep **/presets** admin-only and add votable preset numbers (and names) to **/preset** no-args or to the “Vote to change to &lt;Name&gt;” message. Decide when implementing.

---

## 5. Implementation checklist

- [ ] **Commands:** In `VotingPresetCommandModule.cs`, apply the mapping in section 1. Single name per command; remove all aliases. Rename handler references if needed (e.g. StartVote → still called from **votestart**).
- [ ] **/preset (no args):** Change reply to current preset only + “Use /presets to list all presets.” No list of votable presets.
- [ ] **/presets:** Ensure reply includes “Use /preset &lt;number&gt; to start a vote to switch to that preset.” Decide admin-only vs all (see open point).
- [ ] **VoteOnDemand:** After recording a vote, **broadcast** one line with time left and Yes/No counts. Ensure this broadcast is used as “last update” for the 30s reminder.
- [ ] **RunOnDemandVoteAsync:** Use “30s since last broadcast” for reminder: maintain last-broadcast time; set it at vote start and on every broadcast (vote update or reminder); send reminder only when ≥30s since last broadcast.
- [ ] **Early end:** In on-demand loop (and optionally in VoteOnDemand after broadcast), check `yes_count > total_online/2` (yes wins) and `no_count >= total_online/2` (no wins). If either, exit loop and apply result (preset change or cooldown).
- [ ] **Timer vote broadcast:** When broadcasting timer vote options, use **/vote &lt;number&gt;** in the text (e.g. “/vote 0 - Stay on current track”, “/vote 1 - &lt;Name&gt;”) instead of /vt.
- [ ] **Docs:** README and this file are the only two; no new docs. (COMMANDS, STRUCTURE, AC_VOTE_SHORTCUTS merged or removed.)

---

## 6. Done / unchanged

- RequesterCooldownMinutes; one vote at a time (Idle | TimerVote | OnDemandVote); cooldown only after failed or canceled on-demand vote.
- Timer vote flow; on-demand start with /preset &lt;number&gt;; /yes, /no; auto-switch when only one other preset (no vote).
- Config in one file; single command module; single vote state in plugin.

---

## 7. Future / optional

- **Vote shortcuts:** Re-investigate whether in-game Y/N (or Ctrl+Y/N) or a CSP Lua UI can be used for preset yes/no once a vote is in progress. We use chat only because shortcuts didn’t show up in our packet captures. See **docs/WHY_CHAT_ONLY_NOT_SHORTCUTS.md**.
