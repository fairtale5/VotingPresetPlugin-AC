# Architecture: backend + Lua (for on-demand vote UI)

The on-demand vote feature follows the same two-part pattern as NotificationOverlayPlugin and collision penalty.

## 1. Backend (C#)

- Lives in this repo (VotingPresetPlugin).
- **Existing:** PresetManager, preset list, timer-based voting, chat commands.
- **To add for on-demand:**
  - Command(s): e.g. `/preset` (help), `/preset X` (suggest preset X, start vote).
  - Vote state: who requested, who voted yes/no, timeout.
  - Packets to clients: show vote UI (“Change to [PresetName]? Yes / No”, time left).
  - Packets from clients: each player’s yes/no.
  - On vote pass: call PresetManager to change preset; on fail, close vote and notify.
- **Config:** vote duration, min yes count or ratio, cooldown, allowed presets.

## 2. Client Lua (injected by server)

- **Delivery:** Same as NotificationOverlayPlugin: server injects Lua via script provider.
- **Responsibilities:**
  - Subscribe to OnlineEvent for “preset change vote” from server.
  - When active: draw UI (“Change to [X]? Yes / No”, countdown).
  - Keybinds (and/or buttons): one key = yes, one key = no.
  - Send vote back to server via client-message/packet API.
- **Placement:** e.g. `lua/` in this repo, embedded and registered in C# like reconnectclient.lua.

## 3. Packet / event contract

- **Server → client:** “Vote started: preset = [Name], duration = N s”, “Vote ended”.
- **Client → server:** “VoteYes” / “VoteNo” (or one “Vote” with choice). Keys to be defined; match AssettoServer client message / OnlineEvent usage (see NotificationOverlayPlugin, ScoreTrackerPlugin).

## 4. Keybinds

- Goal: vote while driving with minimal effort.
- Document or use the same keys as similar UIs (e.g. vote kick/restart). Lua binds to those (or configurable keys) and sends vote to server.
