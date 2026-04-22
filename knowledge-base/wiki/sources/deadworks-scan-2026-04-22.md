---
title: Deadworks Scan — 2026-04-22
type: source-summary
sources:
  - raw/notes/2026-04-22-deadworks-command-attribute.md
  - raw/notes/2026-04-22-deadworks-timer-api.md
  - raw/notes/2026-04-22-deadworks-events-surface.md
  - raw/notes/2026-04-22-deadworks-schema-accessors.md
  - raw/notes/2026-04-22-deadworks-netmessages-api.md
  - raw/notes/2026-04-22-deadworks-plugin-config.md
  - raw/notes/2026-04-22-deadworks-gameevent-source-generator.md
  - raw/notes/2026-04-22-deadworks-examples-catalog.md
  - raw/notes/2026-04-22-deadworks-plugin-api-surface.md
  - raw/notes/2026-04-22-deadworks-native-layout.md
related:
  - "[[plugin-api-surface]]"
  - "[[command-attribute]]"
  - "[[timer-api]]"
  - "[[events-surface]]"
  - "[[schema-accessors]]"
  - "[[netmessages-api]]"
  - "[[plugin-config]]"
  - "[[gameevent-source-generator]]"
  - "[[examples-index]]"
  - "[[deadworks-runtime]]"
  - "[[docker-build]]"
created: 2026-04-22
updated: 2026-04-22
confidence: high
---

# Deadworks Scan — 2026-04-22

Deep scan of the sibling `../deadworks/` repo to capture knowledge that
the wiki didn't yet cover. The previous ingest (session-extracts-2026-04-21
and deadworks-0.4.5-release) documented the Deadworks **runtime** — boot,
hot reload, memsigs, plugin loader. This scan covers the **plugin-facing
API surface** and the **example plugins**, both of which were
near-invisible on the wiki.

## Scope of the scan

Explored areas (repo paths relative to `../deadworks/`):

- `managed/DeadworksManaged.Api/` — the plugin-facing library: Commands,
  ConCommands, Config, Entities (+ SchemaAccessor family, Players,
  EntityData), Enums, Events, NetMessages, Sounds, Timer, Trace, plus
  top-level helpers (Chat, ConVar, Server, GameRules, ParticleSystem,
  Precache, KeyValues3, HeroData, etc.)
- `managed/` host-side — `PluginLoader.ChatCommands.cs`,
  `PluginLoader.Events.cs`, `PluginLoader.NetMessages.cs`,
  `ConfigManager.cs`, `Commands/CommandBinder.cs`,
  `Commands/CommandTokenizer.cs`
- `managed/DeadworksManaged.Generators/` — `GameEventSourceGenerator`
- `examples/plugins/` — all 11 example plugins (AutoRestart, ChatRelay,
  Dumper, ExampleTimer, ItemRotation, ItemTest, RollTheDice, Scourge,
  SetModel, Tag, DeathmatchPlugin)
- `deadworks/src/` — native C++ source layout (Core/Hooks cluster,
  Memory, SDK, Hosting)
- `docker/build-native.sh`, `docker/Dockerfile` — build-pipeline
  gotchas

## Raw notes produced (10 files)

1. [2026-04-22-deadworks-command-attribute.md](../../raw/notes/2026-04-22-deadworks-command-attribute.md)
   — `[Command]` attribute machinery, tokenizer, typed arg binding,
   CommandException
2. [2026-04-22-deadworks-timer-api.md](../../raw/notes/2026-04-22-deadworks-timer-api.md)
   — `ITimer` entry points, `IStep`/`Pace` for Sequence,
   `IHandle.CancelOnMapChange`, Duration tick vs realtime
3. [2026-04-22-deadworks-events-surface.md](../../raw/notes/2026-04-22-deadworks-events-surface.md)
   — full 23-hook `IDeadworksPlugin` surface, `HookResult` max-wins,
   `AbilityAttemptEvent`, `CheckTransmitEvent`, typed game events
4. [2026-04-22-deadworks-schema-accessors.md](../../raw/notes/2026-04-22-deadworks-schema-accessors.md)
   — `SchemaAccessor<T>` with UTF-8 literals, `SchemaArrayAccessor`,
   `SchemaStringAccessor`, `Players`, `NativeEntityFactory`,
   `EntityData<T>`
5. [2026-04-22-deadworks-netmessages-api.md](../../raw/notes/2026-04-22-deadworks-netmessages-api.md)
   — `NetMessages.Send/Hook`, `NetMessageRegistry` **runtime**
   reflection, enum→protoname mapping table, `[NetMessageHandler]`
6. [2026-04-22-deadworks-plugin-config.md](../../raw/notes/2026-04-22-deadworks-plugin-config.md)
   — `[PluginConfig]`, `IConfig.Validate`, hot-reload contract, JSONC
   file auto-creation, path keyed by plugin C# class name
7. [2026-04-22-deadworks-gameevent-source-generator.md](../../raw/notes/2026-04-22-deadworks-gameevent-source-generator.md)
   — `.gameevents` → typed `*Event` classes + factory; last-seen-wins
   file ordering; type mapping table
8. [2026-04-22-deadworks-examples-catalog.md](../../raw/notes/2026-04-22-deadworks-examples-catalog.md)
   — one-paragraph writeup per example plugin
9. [2026-04-22-deadworks-plugin-api-surface.md](../../raw/notes/2026-04-22-deadworks-plugin-api-surface.md)
   — umbrella table of every file under `DeadworksManaged.Api/`
10. [2026-04-22-deadworks-native-layout.md](../../raw/notes/2026-04-22-deadworks-native-layout.md)
    — `deadworks/src/` tree, Docker build-native.sh hand-list gotcha,
    clang-cl C++23 flag

## Corrections to previous wiki content

While scanning, several prior claims were verified incorrect:

1. **`NetMessageRegistry` uses runtime reflection, not source
   generation.** Builds `typeToId` on first `EnsureInitialized()` by
   scanning for `IMessage` types and mapping enum values via naming
   rules. Prior scan summary said "computed at compile time via source
   gen" — wrong.
2. **Config key is the plugin's C# class name**, not the folder name
   (as the `gamemodes.json` keys are). `plugin.GetType().Name`.
3. **`IConfig` is optional** — without implementing it, a config type
   still works; it just skips the validation step.
4. **`[ChatCommand("zones")]` handles BOTH `/zones` and `!zones`** —
   not a latent bug as previously flagged. The host's
   `DispatchChatMessage` strips both prefixes before registry lookup
   (`PluginLoader.ChatCommands.cs:14-47`). The prior log entry
   flagging this as a convention divergence is incorrect and
   should be reconciled.
5. **`EntityData<T>` is keyed by `uint EntityHandle`**, not entity
   reference. Handles carry a generation counter and survive
   pointer reuse. A global weak-reference registry auto-prunes on
   entity delete without plugin action.

## Cross-cutting findings (what new plugin authors should know)

- **`[Command]` is the v0.4.5+ default.** `[ChatCommand]` and
  `[ConCommand]` are `[Obsolete]` with `CS0618` suppressions at the
  host scan sites — removal is coming. All three plugins in this repo
  still use `[ChatCommand]` and need migration.
- **Timers are cancelled for you on plugin unload.** No need to
  `Cancel()` every handle in `OnUnload` — only the ones you want to
  stop early (e.g., in `OnConfigReloaded`).
- **`OnClientFullConnect` is the right hook for player-ready work.**
  This is when `Players.SetConnected(slot, true)` fires. Before that,
  `Players.GetAll()` won't include the player.
- **Precache only in `OnPrecacheResources`.** Too late in
  `OnStartupServer`, too early in `OnLoad`.
- **`EntityData<T>` auto-cleans on entity delete** — no manual
  `OnEntityDeleted` handling needed per-plugin.
- **`HookResult` aggregates max-wins across plugins.** Any plugin can
  raise to `Stop` or `Handled`; all still see the event.
- **Docker build: `build-native.sh` has a hand-maintained source
  list.** Upstream adding new hook files breaks the fork link step.

## What this scan deliberately did NOT cover

- `launcher/` — separate Tauri desktop app, out of scope
- Re-documenting anything already on `deadworks-runtime`,
  `source-2-engine`, `deadlock-game`, or `deadworks-mem-jsonc` pages
- `DeadworksManaged.Tests/` — test patterns (low priority; skip unless
  debugging test infra)
- The fork-divergent `examples/plugins/DeathmatchPlugin/` — noted as
  different from this repo's `Deathmatch/` but not diffed
