---
title: Wiki Index
type: index
created: 2026-04-21
updated: 2026-04-22
---

_Last ingest: 2026-04-22 — TrooperInvasion round-cycling + lane-gating +
HUD announcements + deletion of match-clock anchor code + event-driven
empty-server cleanup. Plus reusable findings: `citadel_active_lane`
bitmask, `CCitadelUserMsg_HudGameAnnouncement` idiom, scheduler
timer-IHandle tracking pattern._

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
