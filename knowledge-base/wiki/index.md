---
title: Wiki Index
type: index
created: 2026-04-21
updated: 2026-04-23
---

_Last ingest: 2026-04-23 — **`citadel_kick_disconnected_players`
catalogued**: engine-native concommand ("Clear out all players who
aren't connected, removing them from any teams") added to
[[deadlock-game]]. Candidate replacement for the manual
`pawn.Remove() + controller.Remove()` pair in
[[deathmatch]]/[[trooper-invasion]] `OnClientDisconnect`. [[lock-timer]]
unaffected (only clears plugin-internal state). Other plugins
(StatusPoker, FlexSlotUnlock, HealOnSpawn, HeroSelect, Hostname,
TeamChangeBlock) have no `OnClientDisconnect` and are not applicable.
Safest invocation = `sv_cheats 1 / citadel_kick_disconnected_players /
sv_cheats 0` via `Server.ExecuteCommand`. Untested._

_Prev ingest: 2026-04-23 — **TrooperInvasion boss waves removed**:
`CBaseEntity.CreateByDesignerName("npc_trooper_boss") + Spawn()` with
null `CEntityKeyValues` crashed the server natively on the first boss
spawn. Lane-AI NPCs need `m_iLane` + squad + navmesh region wired in via
KV at Spawn time (the real path is `CCitadelTrooperSpawnGameSystem`
driven by `info_trooper_spawn` / `info_super_trooper_spawn`). Managed
spawn is only safe for point entities with explicit KV
(`CPointWorldText`, `ParticleSystem`). Reach-for-next-time pattern
documented: native `citadel_spawn_trooper x,y,z boss` bracketed with
`sv_cheats 1 / 0`._

# Content Catalog

The master catalog of every page in this wiki. Agents read this first to decide
what to load — keep it concise and current.

## Overview & reference

- [[overview]] — project-wide synthesis
- [[glossary]] — terms, acronyms, naming rules
- [[log]] — append-only operation log

## Plugins

- [[deathmatch]] — team-vs-team deathmatch gamemode on `dl_midtown`
- [[lock-timer]] — zone-based lock-timer gamemode with YAML zone config
- [[status-poker]] — periodic status/keepalive poker plugin
- [[trooper-invasion]] — PvE co-op gamemode, all humans on team 2 vs engine NPCs
- [[examples-index]] — index of the 11 Deadworks example plugins (AutoRestart,
  ChatRelay, Dumper, ExampleTimer, ItemRotation, ItemTest, RollTheDice,
  Scourge, SetModel, Tag)

## Concepts

- [[source-2-engine]] — Source 2 as it applies to this project: entity
  system, schema RE, CreateInterface, ConVars, game events, netconport,
  pause, HUD clock anchoring, GOTV/broadcast
- [[deadlock-game]] — Deadlock specifics: app id 1422450, CCitadelGameRules,
  teams/lanes, heroes, flex-slot, walker, NPC classnames, pause ConVars
- [[deadworks-runtime]] — C++ native + C# managed plugin host: bootstrap,
  hooks, nethost/hostfxr, PluginLoader dispatch, hot reload, shared API
- [[plugin-api-surface]] — umbrella map of every file under
  `DeadworksManaged.Api/`; enum reference; canonical idioms
- [[plugin-build-pipeline]] — csproj triple-mode, `gamemodes.json`,
  `extra-plugins` BuildKit context, Directory.Build.targets, protobuf
  plugin gotcha, CI workflows

## Entities

- [[deadworks-sourcesdk]] — the `sourcesdk` git submodule: protoc, protos,
  SDK headers
- [[deadworks-mem-jsonc]] — memory signature JSONC file: schema, scan
  mechanics, validate-signatures.py, crash-on-miss
- [[deadworks-plugin-loader]] — `PluginLoader.cs` class: scan, ALC,
  LoadFromStream, hot reload, dispatch, SharedAssemblies
- [[protobuf-pipeline]] — 3-era evolution (vendored → auto-update →
  build-time sourcesdk protoc), C++ vs managed proto sets, Google.Protobuf
  in plugin csprojs
- [[command-attribute]] — v0.4.5+ unified `[Command]`: typed arg binding,
  tokenizer, slot kinds, `CommandException`, migration from `[ChatCommand]`
- [[timer-api]] — `ITimer` (Once/Every/Sequence/NextTick), `IStep`/`Pace`,
  `Duration` tick-vs-realtime, `CancelOnMapChange`
- [[events-surface]] — full 23-hook `IDeadworksPlugin` list, `HookResult`
  max-wins, `AbilityAttemptEvent` masks, `CheckTransmitEvent`
- [[schema-accessors]] — `SchemaAccessor<T>` with UTF-8 literals, Players,
  NativeEntityFactory, `EntityData<T>` auto-cleanup
- [[netmessages-api]] — `NetMessages.Send/Hook`, `[NetMessageHandler]`,
  runtime enum→proto name mapping table, `RecipientFilter`
- [[plugin-config]] — `[PluginConfig]`, `IConfig.Validate`, hot-reload,
  JSONC auto-creation, class-name keyed paths
- [[gameevent-source-generator]] — `.gameevents` → typed `*Event` classes;
  type mapping table; file ordering invariant

## Operations

- [[docker-build]] — 3-stage build (clang-cl + xwin, dotnet publish,
  Proton runtime), extra-plugins context, per-mode GHCR tagging
- [[proton-runtime]] — Proton/Wine runtime: GE-Proton pinning, steamclient
  triple-copy, WINEDLLOVERRIDES, steam_appid.txt dual-location, SteamCMD
  retry loop, `.NET 10` in the pfx

## Sources

- [[session-extracts-2026-04-21]] — bulk ingest of ~61 session transcript
  extracts across four sibling project dirs
- [[deadworks-0.4.5-release]] — v0.4.5 release notes: `[Command]` attribute,
  port revert to 27067, `Slot`/`HeroID`/`AddItem(enhanced)` additions
- [[deadworks-scan-2026-04-22]] — deep scan of `../deadworks/` API surface,
  example plugins, and native layout (10 raw notes)

## Comparisons

_No comparisons yet._

---

**Total wiki pages:** 28 (index, log, overview, glossary, 3 source,
5 plugin, 5 concept, 11 entity, 2 operation)
**Last ingest:** 2026-04-22 — TrooperInvasion plugin scaffold
