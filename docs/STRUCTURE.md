# VotingPresetPlugin – file structure and responsibilities

Proposed layout with clear separation of concerns. No duplication: one place per responsibility.

---

## Plugin root (C#)

Assume plugin lives at `serv-game/AssettoServer/VotingPresetPlugin/` (or `serv-game/plugins/VotingPresetPlugin/`). Same pattern as VotingWeatherPlugin.

| File | Responsibility |
|------|----------------|
| **VotingPresetPlugin.csproj** | Project file, references AssettoServer. |
| **VotingPresetModule.cs** | Plugin entry. Registers services, command module, config. Loads presets from `presets/*/plugin_voting_preset_cfg.yml`. |
| **VotingPresetConfiguration.cs** | Config: timer interval (60 min), vote duration, **requester cooldown minutes (15)**, pass rule (e.g. 50% yes). One place to tweak per server. |
| **VotingPresetConfigurationValidator.cs** | Validates config (optional). |

---

## Commands (single module)

| File | Responsibility |
|------|----------------|
| **VotingPresetCommandModule.cs** | All chat commands in one module. `/vote` (no args = list+help; number/name = start on-demand or timer vote), `/yes` & `/no` (or `/y`, `/n`), `/presets` (admin), `/presetset <n>` (admin), `/cancelvote` (admin). Timer admin: presetstartvote, presetfinishvote, presetcancelvote, presetextendvote, presetrandom. No duplicate handlers. |

---

## Vote state and flow

| File | Responsibility |
|------|----------------|
| **PresetVoteState.cs** (or **PresetVoteService.cs**) | Single source of vote state: Idle | TimerVoteLive | OnDemandLive. For on-demand: who started (SessionId), which preset, end time, yes/no counts. For timer: existing timer vote behaviour. One vote at a time. |
| **PresetVoteCooldown.cs** (or inside PresetVoteState) | Track last failed on-demand vote per player. Block that player from starting a new on-demand vote for `RequesterCooldownMinutes` (config). Apply cooldown only when vote **fails** (rejected). |

---

## Preset resolution and application

| File | Responsibility |
|------|----------------|
| **PresetResolver.cs** (or methods on preset list service) | Resolve preset by number (index in votable list) or by name (case-insensitive, single match). Used by `/vote <n>`, `/vote <name>`, `/presetset`. |
| **PresetManager** (existing) | Apply preset (restart path, etc.). Called when vote passes. No change to existing behaviour. |

---

## Messaging

| Responsibility | Where |
|----------------|----------------|
| Broadcast when vote starts | PresetVoteState when transitioning to OnDemandLive: e.g. chat message “Vote to change to &lt;PresetName&gt;. Type /yes or /no. Ends in Ns.” |
| Reply to /vote (list + help) | VotingPresetCommandModule → PresetVoteState/PresetResolver for current preset + list. |
| Pass/fail messages | PresetVoteState when vote ends: “Vote passed. Changing to X in 5s.” / “Vote failed. Preset unchanged.” |

---

## Docs (this folder)

| File | Purpose |
|------|--------|
| **README.md** | Overview: 60m timer + on-demand anytime, broadcast, /yes /no, cooldown. Links to COMMANDS, STRUCTURE, TASKS. |
| **COMMANDS.md** | Command reference (current vs target). |
| **TASKS.md** | Task/objectives list: what to implement, in order. Single checklist. |
| **AC_VOTE_SHORTCUTS.md** | Why in-game vote shortcuts weren’t captured; we use chat only. |
| **STRUCTURE.md** | This file. |

---

## Summary

- **One command module** → no duplicate command registration.
- **One vote state** → Idle | TimerVoteLive | OnDemandLive; one place for “who, which preset, when it ends, yes/no counts”.
- **One cooldown** → configurable minutes per server; applied only after a **rejected** on-demand vote.
- **Config** → timer interval, vote duration, requester cooldown, pass rule in one configuration class.
