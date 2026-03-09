# Code examples (where to look in Redline-Racing)

Reference for implementing on-demand vote backend and Lua.

## Server injects Lua script

- **Path:** `serv-game/plugins/NotificationOverlayPlugin/`
- **Look at:** C# registering Lua with `CSPServerScriptProvider`, `AddScript`, manifest resource. In this repo, `lua/reconnectclient.lua` is already injected the same way.
- **Use for:** Injecting the vote UI script so every client runs it.

## Server sends packet → client Lua (OnlineEvent)

- **Paths:** `NotificationOverlayPlugin`, `ScoreTrackerPlugin` (e.g. `LapTimeNotificationPacket`, `WeatherChangeNotificationPacket`).
- **Look at:** C# `[OnlineEvent(Key = "…")]`, fields, `BroadcastPacket` / `SendPacket`; Lua subscription to same key, reading fields.
- **Use for:** Server → client “vote started / ended”, preset name, time left.

## Client sends vote to server

- **Path:** `ScoreTrackerPlugin`; AssettoServer docs for client messages.
- **Look at:** How client actions reach the server (client message, chat, or packet server listens for).
- **Use for:** Sending VoteYes / VoteNo from Lua to C#.

## Preset change (this plugin)

- **Path:** `serv-game/plugins/VotingPresetPlugin/` – `PresetManager`, `SetPreset`, vote flow.
- **Use for:** When on-demand vote passes, call existing PresetManager to switch preset.

## Collision penalty (notification + action)

- **Paths:** `CollisionPenaltiesPlugin`, `ScoreTrackerPlugin` (e.g. `CollisionPenaltyPacket` and Lua).
- **Look at:** Server packet → one client; client Lua applies action. Same “server tells client what to show/do” pattern.
- **Use for:** Vote UI driven by server packet, in sync with server vote state.
