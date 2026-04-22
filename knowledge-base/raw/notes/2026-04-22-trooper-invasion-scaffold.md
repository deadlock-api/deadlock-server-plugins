---
date: 2026-04-22
task: Scaffold TrooperInvasion PvE co-op gamemode
files: [TrooperInvasion/TrooperInvasion.cs, TrooperInvasion/TrooperInvasion.csproj, gamemodes.json, docker-compose.yml, .github/workflows/docker-gamemodes.yml, .github/workflows/build-plugins.yml]
---

New PvE gamemode "TrooperInvasion" scaffolded alongside Deathmatch/LockTimer/StatusPoker. Key design choices the next agent should know:

- **All humans forced to team 2 (Amber)** via `controller.ChangeTeam(2)` in `OnClientFullConnect`. Team 3 is NPC-only — engine keeps spawning the Sapphire-side troopers/guardians/walkers, which become the PvE enemies. No map NPC removal (unlike Deathmatch which strips everything). Intentional: the existing map NPCs ARE the content.
- **Trooper/NPC spawning explicitly enabled**: `citadel_trooper_spawn_enabled=1`, `citadel_npc_spawn_enabled=1` — opposite of Deathmatch and Tag (both disable). Without these, PvE has nothing to fight.
- **Client `changeteam`/`jointeam`/`selecthero`/`citadel_hero_pick` all hard-blocked** in `OnClientConCommand` — no post-connect hero swap window (Deathmatch's `_heroSwapUntil` machinery intentionally omitted). Hero swap only via `!hero` chat command.
- **Commands use v0.4.5 `[Command]` attribute** (not legacy `[ChatCommand]`) — matches LockTimer's post-6ace83c state, not Deathmatch which still uses `[ChatCommand]`. `!help`, `!hero <name>`, `!stuck`/`!suicide`.
- **Deathmatch parity intentionally skipped**: no lane rotation, no walker spawn capture, no cooldown scaling, no per-round kill tracking, no rank-based balancing (all team-vs-team concepts, meaningless for single-team PvE). HUD clock still pinned to elapsed-since-start because `EGameState.GameInProgress` needs the anchor fields written in lockstep or the client extrapolates past them.
- **Port 27018** (next slot after normal:27015, lock-timer:27016, deathmatch:27017).
- **Both CI workflows needed updating** — `docker-gamemodes.yml` AND `build-plugins.yml` both carry hardcoded `paths-filter` stanzas per plugin (force_all lives only in docker-gamemodes.yml's `force_all` which triggers on `gamemodes.json` changes; build-plugins.yml's force_all triggers on Directory.Build.* only). Forgetting either one silently yields an empty matrix.
- **No `Google.Protobuf` package reference** in the csproj — the scaffold doesn't use `NetMessages.Send<T>` (no HUD announcements yet). If the plugin grows to send game announcements, follow Deathmatch's pattern and add the package reference.
- **Empty `TrooperInvasionConfig` class** kept as `[PluginConfig]` target — same host-contract quirk noted in Deathmatch (the empty config is required, not dead code — deathmatch-5233473a).
