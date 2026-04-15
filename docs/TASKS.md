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

## 4. Resolved: /presets visibility

- **/presets** is available to all (RequireConnectedPlayer only). Players see votable presets with " /preset {i} - {Name}". Admins see that plus "Admin: set preset: /presetset &lt;number&gt;" and full list " /presetset {i} - {Name}". don’t see which **number** to use for **/preset &lt;number&gt;** unless we show votable numbers somewhere else (e.g. in **/preset** no-args, or in the on-demand vote start message). Options: (A) Make **/presets** available to all so everyone can see the list and the numbers, or (B) Keep **/presets** admin-only and add votable preset numbers (and names) to **/preset** no-args or to the “Vote to change to &lt;Name&gt;” message.
---

## 5. Implementation checklist

- [x] **Commands:** In `VotingPresetCommandModule.cs`, apply the mapping in section 1. Single name per command; remove all aliases. Rename handler references if needed (e.g. StartVote → still called from **votestart**).
- [x] **/preset (no args):** Change reply to current preset only + “Use /presets to list all presets.” No list of votable presets.
- [x] **/presets:** First line "To start a vote: /preset &lt;number&gt;"; each line " /preset {i} - {Name}". Admin-only unchanged (see open point). Ensure reply “Use /preset &lt;number&gt; to start a vote to switch to that preset.” Decide admin-only vs all (see open point).
- [x] **VoteOnDemand:** After recording a vote, **broadcast** one line with time left and Yes/No counts. Ensure this broadcast is used as “last update” for the 30s reminder.
- [x] **RunOnDemandVoteAsync:** Use “30s since last broadcast” for reminder: maintain last-broadcast time; set it at vote start and on every broadcast (vote update or reminder); send reminder only when ≥30s since last broadcast.
- [x] **Early end:** In on-demand loop (and optionally in VoteOnDemand after broadcast), check `yes_count > total_online/2` (yes wins) and `no_count >= total_online/2` (no wins). If either, exit loop and apply result (preset change or cooldown).
- [x] **Timer vote broadcast:** When broadcasting timer vote options, use **/vote &lt;number&gt;** in the text (e.g. “/vote 0 - Stay on current track”, “/vote 1 - &lt;Name&gt;”) instead of /vt.
- [x] **Docs:** README and this file are the only two; no new docs. (COMMANDS, STRUCTURE, AC_VOTE_SHORTCUTS merged or removed.)

### Backlog (not in §5 above)

- [ ] **§8 — First:** C# **done** for v1 on-demand (packets + `VotingPresetPlugin`: `RegisterOnlineEvent` → `VoteOnDemand`, `BroadcastOnDemandVoteOpenUi` on vote start / each vote update / 30s reminder). **Next:** **Lua** client (matching layout + `SharedNamespace.ServerScript`) + hash verify + optional structure one-liner. Timer CSP = **Soon**. Then §8.1–8.4 as needed.

---

## 6. Done / unchanged

- RequesterCooldownMinutes; one vote at a time (Idle | TimerVote | OnDemandVote); cooldown only after failed or canceled on-demand vote.
- Timer vote flow; on-demand start with /preset &lt;number&gt;; /yes, /no; auto-switch when only one other preset (no vote).
- Config in one file; single command module; single vote state in plugin.

---

## 7. Future / optional

- **Vote shortcuts / UI:** Chat-only history in **docs/WHY_CHAT_ONLY_NOT_SHORTCUTS.md**. **Concrete implementation path:** §8 **First** (CSP packets + Lua); keybinds/UI built on top of that channel.
- **PT-BR wording for players:** Add a pass to convert command-facing and status text to clearer Portuguese where desired (without breaking existing command names unless explicitly migrated).
- **/yes / /no ease-of-use:** Keep compatibility, but evaluate simpler player flow (clearer prompts, shorter reminders, optional aliases like `/sim` and `/nao` if approved).
- **Fake vote UI experiment:** Investigate if we can safely present native yes/no vote UI by emitting a synthetic vote type (session skip / kick-like packet flow) only as a frontend interaction layer for preset voting. Record protocol constraints and anti-abuse safeguards before any implementation.

---

## 8. Planned enhancements (backlog)

Backlog for behaviour **not** covered by §5. Same shape as §2–§3: **What**, **Why**, **How (technical)** — so we can implement later without re-deriving design.

**Implement §8 “First” before 8.1–8.4** unless priorities change (packet channel is prerequisite for Lua UI + keybinds).

**Overlap check (nothing below duplicates §2–§3 or §5):**
§2–§3 and the checklist already cover 30s-since-broadcast reminders, broadcast on `/yes`/`/no`, early end, and `/vote` timer text. **§8 First** adds CSP **bidirectional** messages (not chat). **8.1–8.4** add: minimum turnout, **session-safe voting and deferred timer (8.2 — spec + checklist)**, change-vote, shared chat formatter (§7 “shortcuts” is intentionally vague; §8 First is the actionable slice).

### §8 First — Agreed layout (packets & scope)

**Decisions (do not lose track):**

- **`Packets/` folder:** `VotingPresetVoteOpenPacket.cs` (server→client), `VotingPresetVoteCastPacket.cs` (client→server). Keys: **`VotingPreset_VoteOpen`**, **`VotingPreset_VoteCast`** (must match Lua `ac.StructItem.key` exactly).
- **V1 wire scope:** **On-demand yes/no only** — first fields + handler branch call **`VoteOnDemand`** (same as `/yes` `/no`). **`VoteKind` = 0** = on-demand for v1.
- **Timer / multi-option:** **Separate phase.** Skeleton = **comments only** inside those two files under a **“Soon”** banner; **no timer fields or handler branches** until we add checklist rows here and implement deliberately. Full behaviour described in comment blocks + this section; avoid mixing into v1 code paths.
- **Authority for hashing:** **`docs/technical-docs/client-server-communication.md`** — we **do not** duplicate it; plugin **README** links it.
- **No `dotnet build` / compile checks** unless the person doing the work explicitly wants them.

**README:** CSP vote UI subsection lists keys, `EnableClientMessages`, link to technical doc + this §8.

### First: two-way VotingPresetPlugin ↔ client (Lua UI foundation)

**Goal:** (1) **Server → clients:** notify “preset/on-demand vote open — please vote” (payload: enough for UI; e.g. preset name, seconds left, vote kind). (2) **Client → server:** “I vote yes / no” (or timer option later) **without** the client sending session/car id for attribution — **handler uses `ACTcpClient` from the connection** (`ChatCommandContext.Client` pattern); packet body is only vote payload; spoofing another player’s id is therefore not applicable if we never accept id from Lua.

**Docs / patterns (read before coding):**
- **`docs/technical-docs/client-server-communication.md`:** OnlineEvent **hash** must match; **Lua** passes **`ac.SharedNamespace.ServerScript`** as the **third** argument to **`ac.OnlineEvent(...)`** — *Solution 1 (PenaltyReporterNoClip):* explicit ServerScript namespace so **client-installed** Lua hashes like **server-provided** script behavior; **field order** matches AssettoServer (primitives first, then strings); enable `DebugClientMessages` when hashing.
- **Client → server reference:** `mods/client-plugins/PenaltyReporterNoClip` (Lua → server handler).
- **Server → client reference:** `VotingPresetPlugin` already sends **`ReconnectClientPacket`** (`[OnlineEvent]` in `Preset/ReconnectClientPacket.cs`) via `BroadcastPacket`; mirror that pattern for a new outbound type (broadcast or targeted if API allows).
- **Server custom outbound elsewhere:** e.g. `CollisionPenaltiesPlugin/Packets/ThrottlePenaltyPacket.cs` (server → client); same OnlineEvent registration/send patterns as other plugins.

**Quick implementation talklist:**
- [x] **C# v1 packet types:** `Packets/VotingPresetVoteOpenPacket.cs` + `VotingPresetVoteCastPacket.cs` — **`[OnlineEventField]`** payload for on-demand yes/no; **timer/multi-option** = **Soon** comment blocks only (no extra fields).
- [x] **`VotingPresetPlugin.cs`:** `CSPClientMessageTypeManager` in ctor; if **`EnableClientMessages`**, **`RegisterOnlineEvent<VotingPresetVoteCastPacket>(OnVoteCastFromClient)`** (FastTravel pattern).
- [x] **`OnVoteCastFromClient`:** `VoteKind` on-demand → **`ChatCommandContext` + `VoteOnDemand`**; timer → **Soon**.
- [x] On-demand **`BroadcastOnDemandVoteOpenUi`**: on **`StartOnDemandVote`**, after each **`VoteOnDemand`** chat update, and on **30s reminder** (alongside chat). **Timer vote** open packet = **Soon**.
- [ ] **Lua** (new or extended app): register receiver/send cast with **matching** layout + `ac.SharedNamespace.ServerScript`; keybinds optional.
- [ ] Verify hashes in logs; add final structure one-liner here or README when Lua lands.
- [ ] **Soon — Timer / multi-option CSP path:** extend open/cast payloads + `VotingAsync` broadcast + handler → `CountVote` (only after v1 on-demand path is stable; see packet file “Soon” blocks).

### 8.1 Minimum participation (e.g. ≥25% of players online)

**What:** Before applying a **winning** on-demand result (and optionally before applying a timer vote winner), require enough **distinct voters**. Example rule: `participated >= ceil(0.25 * total_online)`, with `total_online = _entryCarManager.ConnectedCars.Count` (same population as early-end in §3). If the vote would have passed on yes/no counts but participation is below threshold, treat as **failed**: preset unchanged; decide whether requester gets `RequesterCooldownMinutes` (policy — document in config comment).

**Why:** Stops a single auto-yes (starter) or a tiny clique from changing track on a full server when most people never voted.

**How:** Add config (e.g. `MinParticipationPercent` 0–100, default 0 = current behaviour). At end of `RunOnDemandVoteAsync`: if `yes > no` but `participated < required`, skip `SetPreset`, broadcast reason. Math: `required = max(1, (int)Math.Ceiling(total * percent / 100.0))` unless you explicitly want “0 voters allowed” when percent is 0. For **timer votes**, after `WaitVoting` in `VotingAsync`: if winner has votes but `_alreadyVoted.Count < required`, treat like “stay on track” or “vote failed” (product choice). Edge: AFK cars inflate `total_online` — acceptable if we use the same definition as §3.

### 8.2 Session-safe voting and preset changes (no mid-race; defer timer; apply in P/Q window)

**Product intent (FastFox):**

1. **Do not start votes during Race** — timer vote and on-demand (`/preset <n>`) may run only in **Practice** and **Qualifying** (see Booking below).
2. **Timer-driven behaviour** — when the interval elapses during Race, **do not** open a vote or call `SetPreset` immediately. **Defer** until a safe window (see 3).
3. **When a deferred change is applied** — apply only during **early Practice or early Qualifying**, not in the **tail of Qualifying** (the “session ending / about to go to Race” period). **Do not** switch preset at the moment Qualifying ends if that would interrupt the transition into Race.

**Why:** Reconnect / restart during Race or right before the race start breaks the event flow; P/Q are natural windows.

**Investigation summary (no code changes yet):**

- **VotingPresetPlugin** has **no** `SessionManager` (or session type) dependency today. All paths are time/state only:
  - **`ExecuteAsync`** waits `IntervalMilliseconds - VotingDurationMilliseconds`, then calls **`VotingAsync`** if `EnableVote` — **no session check**.
  - **`VotingAsync`** can **`SetPreset`** after a vote, on **“only one other preset”** auto-switch, or after `WaitVoting`; **`StartOnDemandVote`**, **`StartVote`**, **`RunOnDemandVoteAsync`** likewise **never** read session phase.
- **Authoritative session type** in this tree: `AssettoServer.Server.SessionManager.CurrentSession` → `SessionState.Configuration.Type` → `AssettoServer.Shared.Model.SessionType` (`Booking`, `Practice`, `Qualifying`, `Race`). Session lifecycle and transitions live in **`SessionManager.cs`** (`NextSession`, `IsSessionOver`, etc.). **`SessionChanged`** (`EventArgs.cs` → `SessionChangedEventArgs` with `PreviousSession`, `NextSession`) fires when a new session starts — good hook for “session just started”.
- **`PresetManager`** only reacts to **`SetPreset`** (restart/reconnect path); it does not know about sessions. Any gating belongs in **VotingPresetPlugin** (and optionally a tiny helper), not in `PresetManager`.

**Booking:** Decide explicitly: allow or deny votes during **Booking** (enum exists; many servers skip or shorten it). Default suggestion: **same as Race** (deny) unless config says otherwise.

**Admin commands:** **`/presetset`**, **`/presetrandom`**, **`/votestart`** are instant / admin-driven. Product default: **only gate player-facing starts** (`ExecuteAsync` interval, **`StartOnDemandVote`**); document whether admins may still force a switch during Race (recommend: leave admin path unrestricted unless you add `AdminRespectsSessionGate`).

---

#### 8.2a — Refuse to *start* votes in Race (and optionally Booking)

**Where to guard (same message to players):**

| Entry point | Method | Behaviour when disallowed |
|-------------|--------|---------------------------|
| Interval timer | `ExecuteAsync` → do not call `VotingAsync` yet; mark deferred (8.2b) | No chat spam each tick; optional one-line log |
| Admin timer start | `StartVote` | Reply: preset vote cannot start during race |
| On-demand | `StartOnDemandVote` | Reply to requester only |

**Config:** e.g. `DisallowVotesDuringRace: true` (default `true` when feature ships) + optional `DisallowVotesDuringBooking`.

---

#### 8.2b — Defer interval when it fires in Race (or when not in P/Q)

**Today:** The loop always waits the same delta, then runs `VotingAsync` if `EnableVote`.

**Target flow:**

1. When the wait completes, read **current** session type from **`SessionManager`**.
2. If type is **Race** (or Booking if configured): **do not** call `VotingAsync`. Set an internal flag, e.g. **`_timerVotePendingAfterRace`** (or a small struct: pending + reason), and **return to the wait** — or wait a short poll until session changes (avoid busy-loop; align with `SessionChanged` or a few seconds’ delay).
3. When session becomes **Practice** or **Qualifying**, if a vote is pending, run **`VotingAsync`** once (subject to 8.2c window), then clear the flag. If multiple interval ticks passed during one long Race, **collapse** to one pending vote (product: one vote per “safe window”, not a backlog storm).

**Edge:** **`VotingAsync` “only one other preset”** branch also calls **`SetPreset`** without a vote — that path must use the **same** deferral / apply rules as a full vote.

---

#### 8.2c — Apply only in “early” P/Q; not at end of Qualifying

**Problem:** Entering Qualifying is safe at **session start**; the **end** of Qualifying (time nearly up, `SessionOverFlag`-adjacent behaviour) is **not** — user asked not to change “at the END of qualifying”.

**Approach (pick one and document in config comments):**

- **A — Session-changed hook:** Subscribe to **`SessionManager.SessionChanged`**. When **`NextSession.Configuration.Type`** is **Practice** or **Qualifying**, treat the first part of the session as the apply window (e.g. first **N** seconds after change, using `NextSession.SessionTimeMilliseconds` / server time vs `StartTimeMilliseconds`). If a deferred vote completed earlier but apply was held, apply here. Optionally **refuse to start** a new timer vote if `TimeLeftMilliseconds` is below a threshold (so you never *start* voting in the last minute of Quali).
- **B — Time-left guard:** Allow `SetPreset` only when `SessionType` is P/Q **and** `TimeLeftMilliseconds > EndOfSessionGuardMs` (config), so the tail of Qualifying is excluded.
- **C — Combined:** Session-changed for “fresh session” + time-left guard for long sessions.

**Explicit rule:** When transitioning **Qualifying → Race**, do **not** apply a pending preset change as part of “quali just ended”; hold until **next** Practice or Qualifying **start** (e.g. next event cycle), unless product decides otherwise.

---

#### 8.2 — Implementation checklist

- [ ] Register **`SessionManager`** in **`VotingPresetModule`** / ctor of **`VotingPresetPlugin`** (same DI pattern as other plugins using session, e.g. **`CollisionPenaltiesFeature`**, **`RaceStartGraceFeature`**).
- [ ] Add config keys + validator comments: `DisallowVotesDuringRace`, optional `DisallowVotesDuringBooking`, `DeferTimerVoteUntilPracticeOrQualifying`, `QualifyingEndGuardSeconds` (or equivalent for approach A/B/C).
- [ ] **`StartOnDemandVote`**: session gate + user message.
- [ ] **`StartVote`**: session gate + admin message.
- [ ] **`ExecuteAsync`**: defer flag instead of blind `VotingAsync`; integrate with **`SessionChanged`** or periodic check.
- [ ] **`VotingAsync`**: all **`SetPreset`** exits (winner, stay, single-preset auto) respect apply-window rules; no apply in Race tail / disallowed transitions.
- [ ] Manual test matrix: interval fires in Practice / Qualifying / Race; long Race; vote win during Quali last 30s; only-one-preset auto during Race; on-demand during Race (denied).

### 8.3 On-demand: remember choice per client; allow changing vote

- [x] **Data structure:** `_onDemandVoteByClient` is **`Dictionary<ACTcpClient, bool>`** (yes=true, no=false). Key present iff that client has voted this round; abstain count still `ConnectedCars.Count - dictionary.Count`. Cleared when the on-demand vote ends or is canceled. `StartOnDemandVote` seeds the requester as **yes** via `[context.Client] = true`.

- [x] **Behaviour — change vote:** `VoteOnDemand` uses `TryGetValue`: first vote increments counts; flip yes→no or no→yes decrements old side and increments new; same choice again replies “Your vote is unchanged.” and skips broadcast. Early-end logic still reads `_onDemandYesCount` / `_onDemandNoCount` only.

**Why:** Storing yes/no per client is required before users can change their ballot without corrupting tallies.

**Timer vote path** (later): `_alreadyVoted` + `_availablePresets[i].Votes` — for change-vote there, use `Dictionary<ACTcpClient, int>` (option index) and on change decrement old option’s votes, increment new.

### 8.4 One formatter for on-demand status text (broadcast vs private)

**What:** Vote **start** uses `BroadcastChat` with `"Change to {Name}? /yes /no ..."`; `GetPresetListAndHelp` (during on-demand) uses `context.Reply` with a similar line but **remaining seconds** and live counts. They are **not** duplicates for different audiences: start is **public**, help is **private** to whoever typed `/preset` with no args — but the **wording drifts** and two places must be edited for i18n or clarity.

**Why:** Single `FormatOnDemandStatus(...)` (or local function) avoids inconsistent messages; optional parameter `forBroadcast: bool` if we ever need a shorter line for chat limits.

**How:** Method parameters e.g. `presetName`, `secondsLeft`, `yes`, `no`, `abstained`. Call from `StartOnDemandVote` (pass `VotingDurationSeconds` as initial `secondsLeft` or compute from `_onDemandEndTime`), `VoteOnDemand`, `RunOnDemandVoteAsync` reminder, and `GetPresetListAndHelp`. Do **not** remove public vs private routing — only unify the **string template**.
