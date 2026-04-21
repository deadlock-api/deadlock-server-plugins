---
date: 2026-04-21
task: session extract — deathmatch 493a9384
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-deathmatch/493a9384-0da0-49d4-8eeb-f491dd776223.jsonl]
---

## Source 2 engine

- `game_exported/game.gameevents:167` defines a `match_clock` event with `match_time` (float) and `paused` (bool) — speculative mechanism for forcing HUD clock resets server-side when schema writes alone are insufficient.
- Game events are created server-side via `NativeInterop.CreateGameEvent` wrapped by `GameEvents.Create(name, force=true)` → `GameEventWriter`, which exposes `SetString/SetInt/SetFloat/SetBool` chainable builders before `Fire` (`DeadworksManaged.Api/Events/GameEventWriter.cs`, `GameEvents.cs:19`).
- Schema field writes use `SchemaAccessor<T>(className_utf8, fieldName_utf8)` with `.Set(ptr, value)` + an implicit `NotifyStateChanged` somewhere in the write path — for inline char buffers like player name, the plugin must call `NativeInterop.NotifyStateChanged` with the accessor's `Offset`/`ChainOffset` manually (`Entities/PlayerEntities.cs:24`).

## Deadlock game systems

- `CCitadelGameRules` clock-related schema fields, all on the `CCitadelGameRulesProxy.m_pGameRules` pointee (`DeadworksManaged.Api/GameRules.cs:16-28`): `m_fLevelStartTime`, `m_flGameStartTime`, `m_flGameStateStartTime`, `m_flGameStateEndTime`, `m_flRoundStartTime`, `m_flMatchClockAtLastUpdate`, `m_nMatchClockUpdateTick`, plus `m_eGameState/m_eMatchMode/m_eGameMode`.
- HUD match clock extrapolates between server ticks — to freeze/reset it you must pin the anchor pair together. The plugin's rotation handler writes all of `m_flGameStartTime`, `m_fLevelStartTime`, `m_flRoundStartTime` to `CurTime - elapsed - (TotalPausedTicks * IntervalPerTick)` AND `m_flMatchClockAtLastUpdate=elapsed` + `m_nMatchClockUpdateTick=TickCount` each tick. Missing `m_flRoundStartTime` was the specific bug ("only remaining CCitadelGameRules time field we weren't writing").
- Prior commits `d5ff443` ("anchor match clock tick so HUD stops extrapolating past our writes") and `dc9114e` ("reset HUD clock to 0 at rotation; mirror full respawn on teleport") show the clock-pinning approach evolving over multiple fixes.
- Deadlock lane IDs in schema: `_laneCycle = {1,3,6}` = Yellow/Green/Purple (Blue=4 skipped) — these are `LaneColor` enum values, see `Enums/LaneColor.cs`.
- Blocking hero swap: intercept `selecthero` and `citadel_hero_pick` client con-commands in `OnClientConCommand` returning `HookResult.Stop`; `changeteam`/`jointeam` are blocked the same way (`DeathmatchPlugin.cs:328-342`).
- `player_respawned` game event fires on both fresh spawns and respawns; distinguishing real death from initial spawn requires tracking `player_death` → `_lastDeathPos[pawn.EntityIndex]` and checking dict presence in the respawn handler.
- `player_death` event payload exposes victim coords via `VictimX/Y/Z` floats plus `UseridPawn` and `AttackerController` (`DeathmatchPlugin.cs:358-365`).

## Deadworks runtime

- `HookResult` enum has three values: `Continue=0, Stop=1, Handled=2` (`Enums/HookResult.cs:4`). Event handlers and override hooks all return this.
- `ClientConCommandEvent` provides `ControllerPtr` (nint), `Command` (string), `Args` (string[]), plus a lazy `Controller` property that wraps the ptr as `CCitadelPlayerController` when non-zero (`Events/ClientConCommandEvent.cs`).
- Game event handlers are registered declaratively with `[GameEventHandler("event_name")]` attribute on methods taking a strongly-typed event args class (e.g. `PlayerRespawnedEvent`, `PlayerHeroChangedEvent`, `PlayerDeathEvent`) — these are codegen'd, not in the Api source tree directly; lookups found them only via `deadlock-api/tools/target/doc/valveprotos/deadlock/enum.ECitadelGameEvents.html` reference docs and `deadworks/src/Core/Hooks/GameEvents.cpp`.
- `GlobalVars.CurTime`, `GlobalVars.TickCount`, `GlobalVars.IntervalPerTick` and `GameRules.TotalPausedTicks` are the canonical time primitives for scheduling and clock math.
- Controller vs pawn entity indices are distinct keys — plugin uses controller `EntityIndex` for the hero-swap window dict and pawn `EntityIndex` for `_lastDeathPos`/`_invulnerableUntil`. On `OnClientDisconnect` both must be cleaned, and `controller.GetHeroPawn()?.Remove()` + `controller.Remove()` teardown (`DeathmatchPlugin.cs` disconnect handler).
- `CBasePlayerController.SetPawn(pawn, retainOldPawnTeam, copyMovementState, allowTeamMismatch, preserveMovementState)` is the supported reassignment API; player name write is an inline 128-byte buffer requiring manual null-termination (`Entities/PlayerEntities.cs:34-36`).

## Plugin build & deployment

- Build incantation for a single plugin: `dotnet build plugins/DeathmatchPlugin/DeathmatchPlugin.csproj -c Debug` from the deathmatch repo root.
- Repo layout: deathmatch gamemode lives in a separate repo `/home/manuel/deadlock/deadlock-deathmatch/` (distinct from `deadlock-server-plugins/`), consumes `DeadworksManaged.Api` from `/home/manuel/deadlock/deadworks/managed/DeadworksManaged.Api/` — recent CI-related commits on deathmatch (`2c4e6ae`, `a8c2fc1`, `54759bf`) mirror the csproj split pattern used in the main plugins repo for Docker vs local-dev builds.
