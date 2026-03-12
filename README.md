# VotingPresetPlugin

AssettoServer plugin for preset (track/config) voting. Lets the server rotate presets on a timer or when players ask for a vote, so the community can choose the next track without an admin having to change it by hand. Two modes: **timer vote** (e.g. every 60 min the server offers N options, players vote by number) and **on-demand vote** (any player can start a yes/no vote to switch to a specific preset). All via in-game chat; no extra client app. Fork maintained for Redline Racing; lives under `serv-game/plugins/` and references the parent AssettoServer solution.

---

## How it works

- **Timer vote:** On a configurable interval the server starts a vote with N preset options. Players use **/vote &lt;number&gt;** to pick an option. Admin can start/end/cancel/extend with **votestart**, **voteend**, **votecancel**, **votetimeadd**. Winner is applied when the vote ends. If only one other preset exists, the server switches to it without a vote and logs it.
- **On-demand vote:** Any player can start a vote with **/preset &lt;number&gt;**. Server broadcasts: “Vote to change to &lt;PresetName&gt;. Type /yes or /no. Ends in Ns.” Others reply **/yes** or **/no**. One vote at a time. Reminders are sent **30 seconds since the last broadcast** (so after a vote update we don’t spam a reminder). Vote can **end early** when the result is decided (majority yes, or impossible for yes to win). After a **rejected** on-demand vote, the player who started it cannot start another for **RequesterCooldownMinutes** (default 15).

**Config:** One file per server (e.g. `plugin_voting_preset_cfg.yml` in preset folders and cfg): timer interval, vote duration, requester cooldown, transition delay, etc. Enable the plugin in server config with `VotingPresetPlugin` in `EnablePlugins`.

### Commands (target)

| Command | Who | What it does |
|--------|-----|--------------|
| **/preset** | Player | Show current preset name. Reply: “Use /presets to list all presets.” |
| **/preset &lt;number&gt;** | Player | Start on-demand vote for preset at that index. Reject if vote running, invalid index, or requester on cooldown. |
| **/presets** | Player / Admin | Votable presets (for /preset). Admins also see full list (for /presetset). |
| **/vote &lt;number&gt;** | Player | During **timer vote** only: cast vote for that option. If no timer vote: “There is no ongoing track vote.” |
| **/yes**, **/y** | Player | During on-demand vote: vote yes. |
| **/no**, **/n** | Player | During on-demand vote: vote no. |
| **/presetset &lt;number&gt;** | Admin | Switch to preset by index. No vote. |
| **/presetrandom** | Admin | Switch to a random other preset. No vote. |
| **/votestart** | Admin | Start the timer vote now. |
| **/voteend** | Admin | End timer vote and apply result. |
| **/votecancel** | Admin | Cancel current vote (timer or on-demand). |
| **/votetimeadd &lt;seconds&gt;** | Admin | Add seconds to the current vote window. |

*(Some of these names are still being simplified from older aliases; see `docs/TASKS.md` for the exact mapping and implementation status.)*

---

## Project structure

**Plugin root (C#)**

| File | Responsibility |
|------|-----------------|
| **VotingPresetPlugin.csproj** | Project file; references AssettoServer. Publishes to parent `AssettoServer/out-$(RuntimeIdentifier)/plugins/VotingPresetPlugin/`. |
| **VotingPresetModule.cs** | Plugin entry. Registers services, command module, config. Loads presets from `presets/*/plugin_voting_preset_cfg.yml`. |
| **VotingPresetPlugin.cs** | Main logic and vote state: Idle / TimerVote / OnDemandVote. Timer vote loop (`VotingAsync`), on-demand loop (`RunOnDemandVoteAsync`), cooldown, auto-switch when only one other preset. Calls PresetManager to apply preset. |
| **VotingPresetCommandModule.cs** | All chat commands in one place. Maps /preset, /vote, /yes, /no, /presets, /presetset, /presetrandom, votestart, voteend, votecancel, votetimeadd to methods on the plugin. |
| **VotingPresetConfiguration.cs** | Config model: interval, vote duration, requester cooldown, transition delay, etc. One place to tweak per server. |
| **VotingPresetConfigurationValidator.cs** | Validates config (e.g. VoteChoices ≥ 2, IntervalMinutes ≥ 5, RequesterCooldownMinutes ≥ 0). |

**Preset/** — preset types and application

| File | Responsibility |
|------|-----------------|
| **PresetType.cs** | Preset identifier (e.g. name, folder). |
| **PresetData.cs** | Data passed when applying a preset (current + upcoming + transition). |
| **PresetConfiguration.cs** | Preset-level config (name, AdminOnly, etc.). |
| **PresetConfigurationManager.cs** | Loads and merges preset configs from presets and cfg. Builds votable and admin preset lists. |
| **PresetManager.cs** | Applies preset (restart path, reconnect/kick per config). Called when a vote passes or admin sets preset. |
| **ReconnectClientPacket.cs** | Custom packet for telling clients to reconnect on preset change (CSP). |

**lua/** — client script (embedded)

| File | Responsibility |
|------|-----------------|
| **reconnectclient.lua** | Injected when client messages and reconnect are enabled. Shows reconnecting image from server and triggers reconnect on preset change. |

**wwwroot/** — static files served by the server

| Content | Responsibility |
|---------|-----------------|
| **reconnecting.png** | Image shown to clients during preset-change reconnect. Served at `/static/VotingPresetPlugin/reconnecting.png`. |

---

## Docs folder (`docs/`)

| File | Purpose |
|------|---------|
| **TASKS.md** | Plan and implementation checklist: command simplification, 30s reminder behaviour, early end rules, and open points. Single place for “what to do next.” |
| **WHY_CHAT_ONLY_NOT_SHORTCUTS.md** | Explains why we use chat commands (/yes, /no) instead of in-game keyboard shortcuts: we couldn’t capture shortcut traffic in tests; may be revisited later. |


---

## Future / open plans

- **Command simplification:** Finish renaming and de-aliasing (votetrack → /vote &lt;number&gt;, presetshow → /preset, presetlist → /presets, presetstartvote → votestart, etc.). See `docs/TASKS.md` §1.
- **On-demand behaviour:** 30s reminder “since last broadcast”; broadcast counts when someone votes; early end when majority yes or no. See `docs/TASKS.md` §2–3.
- **/presets visibility:** Resolved: /presets is available to all; players see votable list, admins see that plus full list for /presetset. See `docs/TASKS.md` §4.
- **Vote shortcuts:** Re-investigate whether in-game Y/N (or Ctrl+Y/N) can be used for our preset vote; currently we rely on chat only. See `docs/WHY_CHAT_ONLY_NOT_SHORTCUTS.md` and `docs/TASKS.md` for context.

Full checklist and implementation details are in **docs/TASKS.md**.
