# VotingPresetPlugin – Goals (including on-demand vote)

## What we have

This plugin is a fork of AssettoServer’s VotingPresetPlugin, maintained in `serv-game/plugins/VotingPresetPlugin`. It provides timer-based preset/track voting (e.g. 60‑minute cycle) and all existing chat commands.

## What we want to add (on-demand vote)

**Anyone can start a preset-change vote at any time** (not only on the fixed interval). When a player asks for a change, **everyone** gets an **in-game UI** (like collision penalty) with **yes/no** and **keybinds**.

---

## Vote model (to decide)

1. **Suggest preset + yes/no**
   - Requester suggests **one** preset to change to.
   - Everyone sees: “Change to [PresetName]? Yes / No” and can vote with keybinds.
   - Easier while driving (one key = yes, one key = no).

2. **Start vote + everyone picks preset**
   - Requester starts “preset vote”; everyone picks from a list (like `/vt 0`, `/vt 1` in UI).
   - Richer but harder while driving.

**Leaning:** Option 1 (suggest one preset → yes/no). Confirm keybinds for yes/no in AC/CSP.

---

## Two parts for on-demand UI

Same pattern as NotificationOverlayPlugin / collision penalty:

1. **Backend (C#)** – Already in this repo. Add: command(s) to request/suggest preset vote, vote state, timeout, packets to show/hide vote UI, receive yes/no from clients, call PresetManager when vote passes.
2. **Client Lua (injected by server)** – New script: subscribe to vote OnlineEvent, draw “Change to [X]? Yes / No”, keybinds, send vote back to server.

---

## References

- **This plugin:** timer-based preset votes; PresetManager, preset list, chat commands.
- **NotificationOverlayPlugin:** server sends packets → client Lua shows overlay.
- **Collision penalty:** server packet → client Lua; “notification + action” flow.
- **comfy weather vote (Overtake):** button-based voting UI; inspiration for yes/no style.
- **CyclePresetPlugin (nvrlift):** track voting; see `docs/inspiration/README.md`.
