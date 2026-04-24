---
title: Deadworks runtime
type: concept
sources:
  - raw/notes/2026-04-23-telemetry-env-vars.md
  - knowledge-base/raw/articles/deadworks-0.4.5-release.md
  - knowledge-base/raw/articles/deadworks-0.4.6-release.md
  - knowledge-base/raw/notes/2026-04-22-deadworks-command-attribute.md
  - knowledge-base/raw/notes/2026-04-22-deadworks-events-surface.md
  - knowledge-base/raw/notes/2026-04-22-deadworks-plugin-api-surface.md
  - knowledge-base/raw/notes/2026-04-22-deadworks-native-layout.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-0656dd61.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-1bb13986.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-1dba11a1.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-328372c6.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-32c45101.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-3beeff54.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-4881cd7a.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-4972c10e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-52a01b09.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-530007be.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-5d3198bf.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-88df5d67.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-a54dc08d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-aabd306f.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-bc59e6cf.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-d416f1ea.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-d48155c8.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-ddfface7.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-ec2918a5.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-3636296d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-493a9384.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-5233473a.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-c51730eb.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-6d3a9327.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-73f32122.md
related:
  - "[[source-2-engine]]"
  - "[[deadworks-plugin-loader]]"
  - "[[deadworks-sourcesdk]]"
  - "[[deadworks-mem-jsonc]]"
  - "[[protobuf-pipeline]]"
  - "[[plugin-build-pipeline]]"
  - "[[plugin-api-surface]]"
  - "[[command-attribute]]"
  - "[[timer-api]]"
  - "[[events-surface]]"
  - "[[schema-accessors]]"
  - "[[netmessages-api]]"
  - "[[plugin-config]]"
  - "[[gameevent-source-generator]]"
  - "[[examples-index]]"
  - "[[deadworks-0.4.6-release]]"
created: 2026-04-21
updated: 2026-04-24
confidence: high
---

# Deadworks runtime

Deadworks is a C++ native + C# managed plugin host that embeds itself into
the Deadlock dedicated server by acting as a **replacement entry point**.
The repo lives at `github.com/Deadworks-net/deadworks` (upstream) with a
`raimannma/deadworks` fork that carries Docker infrastructure.

## Architecture in one picture

```
deadworks.exe                  (replacement entry; loads engine2.dll)
   ↓ Source2Main("citadel")    (engine hands off to game)
engine2.dll → AppSystems
   ↓ OnAppSystemLoaded hook    (installed by deadworks before handoff)
safetyhook inline trampolines  (~30 game-engine hooks)
   ↓
nethost → hostfxr              (boots .NET 10 managed layer)
   ↓
DeadworksManaged.dll           (host)
   ↓
PluginLoader.LoadAll           (scans plugins/*.dll)
   ↓
plugin DLLs in isolated ALCs   (collectible AssemblyLoadContexts)
```

Sources: deadworks-52a01b09, deadworks-88df5d67, deadworks-a54dc08d,
deadworks-bc59e6cf.

## `deadworks.exe` as a replacement entry point

- `deadworks.exe` does NOT inject into `deadlock.exe` — the two binaries
  coexist in `game/bin/win64/` and only the one named on the proton command
  line runs. The entrypoint script copies `deadworks.exe` + `managed/` tree
  over the SteamCMD-installed `deadlock.exe` location on each container start
  (deadworks-ddfface7, `docker/entrypoint.sh:150-160`).
- Self-hosted model: `deadworks.exe` is run from `<Deadlock>/game/bin/win64/`
  as its CWD. Players connect via in-game console `connect localhost:27067`
  by default. As of **v0.4.5** the deadworks default port is back to
  `27067` (reverted to avoid conflicts with the game client); the Docker
  flow in this repo still sets its own `SERVER_PORT` (historically `27015`).
  Earlier log flagged this as a documentation inconsistency; v0.4.5
  resolves it on deadworks's side.
- Loads engine `engine2.dll` via `LoadLibrary`, calls exported
  `Source2Main(hInst, hPrev, cmdLine, nShowCmd, baseDir="", game="citadel")`.
- Preloads `../../citadel/bin/win64/server.dll` relative to the exe before
  handoff so signature scans hit `server.dll` even if engine hasn't loaded it
  yet (`startup.cpp:19`, deadworks-52a01b09).

## Bootstrap and signatures

At boot, deadworks loads [[deadworks-mem-jsonc]] from
`citadel/cfg/deadworks_mem.jsonc` and validates a list of required
signatures (`startup.cpp:40-52`, deadworks-52a01b09):

- `UTIL_Remove`
- `CMaterialSystem2AppSystemDict::OnAppSystemLoaded`
- `CServerSideClientBase::FilterMessage`
- `GetVDataInstanceByName`
- `CModifierProperty::AddModifier`

The only hook installed before handoff is `OnAppSystemLoaded`; it fires
once AppSystems are up, at which point deadworks installs ~30 safetyhook
inline trampolines and spins up the .NET managed layer.

**The C++ scanner crashes the process if a required pattern isn't found.**
There is no graceful fallback — stale signatures = hard crash at startup
(deadworks-d75e1c40). See [[deadworks-mem-jsonc]] for the validation tool
(`scripts/validate-signatures.py`).

## Hook surface (native C++)

Under `deadworks/src/Core/Hooks/`
(deadworks-52a01b09, deadworks-4972c10e, deadworks-bf8a9ef6):

- `Source2Server`, `Source2GameClients`
- `CBaseEntity` (TakeDamageOld)
- `CCitadelPlayerPawn` (ModifyCurrency)
- `CCitadelPlayerController`
- `GameEvents`, `NetworkServerService`, `PostEventAbstract`
- `ProcessUsercmds`, `AbilityThink`, `AddModifier`, `TraceShape`
- `EntityIO`, `BuildGameSessionManifest` (precaching)
- `ReplyConnection`, `CheckTransmit`, `A2SPatch`

The cross-compile source list in `docker/build-native.sh` is a
**hand-maintained bash array** — new hook `.cpp` files must be appended
explicitly or they silently vanish at link time with `undefined symbol`
errors (deadworks-4972c10e). Recent churn deleted `SendNetMessage.cpp` in
upstream `38a35cc` ("remove signonstate hooks") and added `CheckTransmit.cpp`
and `A2SPatch.cpp`; every such change requires a corresponding edit to the
fork's build script.

## Managed layer boot

.NET hosting (deadworks-52a01b09, deadworks-328372c6):

- `Hosting/DotNetHost.cpp` calls `get_hostfxr_path()` from `nethost.lib`.
- Hostfxr searches `DOTNET_ROOT` env var first, then `C:\Program Files\dotnet\`,
  then registry.
- Runtime must be .NET 10 (`DeadworksManaged.csproj`, README requires 10.0.5+).
- `DOTNET_ROOT` is set to Windows-style `C:\Program Files\dotnet` inside
  Proton before launch (`docker/entrypoint.sh:212`).
- Presence verified by `host/fxr/*/hostfxr.dll` marker.

Managed layer layout (`managed/` directory alongside `deadworks.exe`):

- `DeadworksManaged.dll` (host)
- `DeadworksManaged.Api.dll` (shared plugin API types)
- `*.runtimeconfig.json`
- `plugins/*.dll` (plugin DLLs discovered by [[deadworks-plugin-loader]])

## Managed/native bridge

- `Core/ManagedCallbacks.hpp` — C# methods called from native
- `Core/NativeCallbacks.hpp` — native functions exposed to managed
- Pattern: native C++ hook fires → calls into `EntryPoint`'s unmanaged
  callback → `PluginLoader` dispatches to registered plugins.
- Example: `g_Deadworks.m_managed.onTakeDamageOld(...)` returning true
  blocks damage; false falls through to original via `hook.call(...)`
  (deadworks-52a01b09).
- `NativeInterop.cs` exposes native callbacks to plugins, including
  `GetMaxHealth(void*) -> int`, `Heal(void*, float) -> int`,
  `CreateGameEvent`, `GetEntityFromHandle`, `GetPlayerController`,
  `NotifyStateChanged` (deathmatch-73f32122, deathmatch-493a9384,
  server-plugins-65d13a2e).

## C# plugin API (`DeadworksManaged.Api`)

Target framework: **`net10.0`** (deadworks-5d3198bf). Nullable enabled,
AllowUnsafeBlocks, docs generated.

> **For the full API surface catalogue**, see [[plugin-api-surface]].
> Dedicated deep-dives: [[command-attribute]], [[timer-api]],
> [[events-surface]], [[schema-accessors]], [[netmessages-api]],
> [[plugin-config]], [[gameevent-source-generator]]. For idiom
> examples, see [[examples-index]].

Key abstractions:

- **`IDeadworksPlugin`** — ~18 lifecycle/hook methods: `OnLoad`,
  `OnUnload`, `OnGameFrame`, `OnTakeDamage`, `OnChatMessage`, `OnAddModifier`,
  `OnClientFullConnect`, `OnClientDisconnect`, `OnClientConCommand`,
  `OnStartupServer`, `OnEntitySpawned`, `OnEntityDeleted`,
  `OnConfigReloaded`, etc. (deadworks-88df5d67).
- **`DeadworksPluginBase`** — abstract base class plugins derive from.
  Exposes `Timer` and `Logger` as **instance** properties, not static:
  - `protected ITimer Timer => TimerResolver.Get(this);` at
    `DeadworksManaged.Api/DeadworksPluginBase.cs:13`.
  - `protected ILogger Logger => LogResolver.Get(this);` (added in upstream
    `deb8ff2`, OpenTelemetry rework).
  - Consequence: plugin helpers needing `Timer`/`Logger` cannot be `static`
    — CS0120 "An object reference is required" otherwise
    (deadworks-3beeff54, deathmatch-c51730eb).
- **Attributes** (declarative registration; deadworks-bc59e6cf,
  deathmatch-493a9384):
  - `[Plugin]`, `[PluginConfig]` — class-level. `[PluginConfig]`-decorated
    classes must exist even if empty (required by the host contract;
    deathmatch-5233473a).
  - `[GameEventHandler("event_name")]` — event listener method
  - **`[Command("name")]`** (new in v0.4.5; **preferred**). Single
    attribute registers three surface forms at once: `dw_<name>` console
    concommand, `/<name>` chat slash command, `!<name>` chat bang command.
    Handler signature: `(CCitadelPlayerController caller, <typed args>)`
    returning `void` — the host parses `ctx.Args[i]` into the declared
    parameter type, so plugins no longer need to `int.TryParse` manually.
  - `[ChatCommand("!mycommand")]` — **deprecated in v0.4.5, will be
    removed.** Chat command handler with manual arg parsing via
    `ChatCommandContext`. Dispatcher strips both `/` and `!` prefixes
    before registry lookup (`PluginLoader.ChatCommands.cs:14-47`), so:
    - `[ChatCommand("foo")]` handles BOTH `/foo` and `!foo` (bare name)
    - `[ChatCommand("!foo")]` handles ONLY `!foo` (bang-only)

    The LockTimer bare-name registration (`[ChatCommand("zones")]` etc.)
    is thus **correct, not a latent bug** as previously flagged in the
    log — it just exposes both surfaces. See [[command-attribute]].
  - `[ConCommand]` — **deprecated in v0.4.5, will be removed.** Superseded
    by `[Command]`, which registers a `dw_`-prefixed concommand alongside
    the chat forms.
  - `[NetMessage]`, event IO attributes.
  - Scanned by `PluginLoader` reflectively on plugin load.
- **Core types / helpers** (see individual sources):
  - `SchemaAccessor<T>` with UTF-8 byte-literal class/field pairs:
    `new("CitadelAbilityVData"u8, "m_nAbilityTargetTypes"u8)`
    (deadworks-3beeff54, deathmatch-5233473a).
  - `EntityData<T>` — per-entity plugin state, auto-cleaned on entity
    delete via `EntityDataRegistry.OnEntityDeleted(handle)`
    (deadworks-d416f1ea).
  - `GlobalVars.CurTime`, `GlobalVars.TickCount`,
    `GlobalVars.IntervalPerTick` — canonical time primitives
    (deathmatch-493a9384).
  - `GameRules.TotalPausedTicks` — subtract when freezing the match clock.
  - `Entities.All` iteration for runtime entity discovery.
  - `RecipientFilter.All`, `Chat.PrintToChat(controller, …)`
    (maps controller → slot via `EntityIndex - 1`).
  - `Players.GetAll()`.
  - `ConVar.Find("name")?.SetString/SetInt/...`.
  - `Server.ExecuteCommand("cmd args")`.
  - `HookResult` enum: `Continue=0`, `Stop=1`, `Handled=2`. Event handlers
    and override hooks return this. Aggregation takes the max — any plugin
    returning a stronger value wins.
- **`NetMessages.Send<T>`** — `T : IMessage<T>` from `Google.Protobuf`.
  Used for HUD announcements etc. Requires plugin to reference
  `Google.Protobuf` at compile time (deathmatch-fa5d1d7e). See
  [[plugin-build-pipeline]] for the CS0311 build-failure mode.
- **Timers** — `Timer.Once(delay, Action)`, `Timer.Every(period, Action)`.
  `1.Ticks()` extension produces per-tick delays. `Timer.Every` is
  **sync-only** — no async HTTP. Plugins needing async use their own
  `System.Threading.Timer` + `CancellationTokenSource`
  (deathmatch-5233473a).
- **Config hot-reload** — plugins override `OnConfigReloaded()`;
  typical pattern `_timer?.Cancel(); Timer.Every(...)` (deadworks-3beeff54).

## PluginLoader dispatch

See dedicated entity page [[deadworks-plugin-loader]]. Summary:

- `PluginLoader.LoadAll()` initializes subsystems in fixed order:
  `TimerRegistry` → `DeadworksConfig` → `ConfigManager` →
  `ConCommandManager` → `ServerBrowser` → `PluginStateManager`
  (deadworks-5d3198bf, deadworks-d416f1ea).
- `DispatchStartupServer` cancels all map-change timers
  (`TimerRegistry.CancelAllMapChangeTimers()`) before dispatching plugins.
- `DispatchGameFrame` ticks `TimerEngine.OnTick()` each frame — TimerEngine
  is per-frame, not a separate thread.
- `DispatchEntityDeleted` cleans up per-entity plugin data via
  `EntityDataRegistry.OnEntityDeleted(handle)`.
- Hook aggregation (`DispatchToPluginsWithResult`): max HookResult wins.
- Errors per plugin are swallowed and logged: `_logger.LogError(ex,
  "Plugin {PluginName}.OnX error", plugin.Name)` — one plugin throwing does
  not abort dispatch to the rest (deadworks-4881cd7a, deadworks-530007be).
- Iteration uses `_pluginSnapshot` — immutable snapshot taken on load so
  plugins can register/unregister without mutating the collection being
  iterated.

## Hot reload

- `FileSystemWatcher` on the `plugins/` directory drives reload.
- 500ms debounce: `_debounceTimer.Change(500, …)` aggregates multiple
  `Changed`/`Created` events via `_pendingReloads` hashset
  (deadworks-d416f1ea, `PluginLoader.cs:293-348`).
- Each plugin DLL loads into its own collectible `PluginLoadContext`
  (`AssemblyLoadContext(isCollectible: true)`) from a `MemoryStream`
  (via `File.ReadAllBytes` + `LoadFromStream`) so the file isn't locked and
  can be overwritten for reload. `.pdb` siblings are loaded the same way
  (deadworks-bc59e6cf, deadworks-d416f1ea).
- `UnloadPlugin` disposes each plugin's `TimerService` before calling
  `OnUnload` so timers stop firing during teardown.
- Disabled plugins (via `dw_plugin disable <name>`, state in
  `configs/plugins.jsonc` via `PluginStateManager`) skip reload-on-DLL-change.

## Shared assemblies pattern

`PluginLoader` builds a `SharedAssemblies` map at startup so every
`PluginLoadContext` resolves host-shared types to the same `Assembly`
instance — preserves type identity (e.g. `IDeadworksPlugin`) across plugins
(deadworks-d416f1ea, deadworks-bc59e6cf, deadworks-5d3198bf):

- `DeadworksManaged.Api` (plugin API surface)
- `DeadworksManaged` (host)
- `Google.Protobuf` (via `typeof(IMessage).Assembly`)

Each plugin `.csproj` pins `DeadworksManaged.Api` with
`<Private>false</Private><ExcludeAssets>runtime</ExcludeAssets>` so only
the host's copy loads. Same pattern needed for `Google.Protobuf` when a
plugin uses protobuf types directly (see [[plugin-build-pipeline]]).

## Plugin discovery via reflection

`PluginLoader.cs:209-224` (deadworks-bc59e6cf):

```
assembly.GetTypes()
  .Where(t => typeof(IDeadworksPlugin).IsAssignableFrom(t)
           && !t.IsInterface && !t.IsAbstract)
```

Instantiated via `Activator.CreateInstance`. No manifest, no hardcoded
names at runtime.

Registrations (events, chat cmds, concommands, net messages) are indexed
by a central `PluginRegistrationTracker` — per-plugin so unload cleans
everything up.

## Partial-class layout

`PluginLoader.cs` is split by domain (deadworks-4881cd7a, deadworks-530007be):

- `PluginLoader.cs` (core)
- `PluginLoader.ChatCommands.cs`
- `PluginLoader.EntityIO.cs`
- `PluginLoader.Events.cs`
- `PluginLoader.NetMessages.cs`

Timer stack: `TimerEngine`, `TimerHandle`, `TimerRegistry`, `TimerService`.
Other managed classes: `ConCommandManager`, `ConfigManager`,
`DeadworksConfig`, `HandlerRegistry`, `NativeLogWriter`,
`PluginRegistrationTracker`, `PluginStateManager`, `ScheduledTask`,
`ServerBrowser`.

## Telemetry and logging (upstream `224d660`)

Canonical SHA on `main` is `224d660` ("add structured logging, metrics,
and traces via Microsoft.Extensions.Logging + OpenTelemetry",
2026-04-14). A pre-rebase copy of the same commit at `deb8ff2` exists
as a dangling object but is unreachable from any branch.

- Structured logging + OpenTelemetry via `Microsoft.Extensions.Logging`.
- Dual-sink: `NativeEngineLoggerProvider` (always added — writes to the
  game console via unmanaged callback) + OTLP log exporter (only when
  telemetry is enabled).
- Per-plugin `ILogger` via `LogResolver` / `PluginLoggerRegistry`;
  category name is `Plugin.{plugin.Name}`. Accessed as
  `this.Logger` on `DeadworksPluginBase`. Throws if accessed outside
  `OnLoad..OnUnload`.
- 19 metric instruments on `Meter("Deadworks.Server")` covering plugin
  lifecycle, player counts, frame duration, timer health, heartbeat,
  event dispatch, chat, and commands.
- `ActivitySource("Deadworks.Server")` traces on **infrequent**
  lifecycle events only — never per-frame.
- Migrated `Console.WriteLine` → `ILogger` across managed codebase.
- `NativeEngineLogger` formats as `[{category}] {prefix}: {message}`
  where prefix maps `Trace→trce`, `Debug→dbug`, `Information→info`,
  `Warning→warn`, `Error→fail`, `Critical→crit`.

### Config surface

All settings under `telemetry:` block in `deadworks.jsonc`. Env vars
**override** JSONC values. Defaults all off / safe:

| Env var | JSONC key | Default | Notes |
|---------|-----------|---------|-------|
| `DEADWORKS_TELEMETRY_ENABLED` | `enabled` | `false` | master gate |
| `DEADWORKS_OTLP_ENDPOINT` | `otlp_endpoint` | `http://localhost:4317` | |
| `DEADWORKS_OTLP_PROTOCOL` | `otlp_protocol` | `grpc` | or `http/protobuf` |
| `DEADWORKS_SERVICE_NAME` | `service_name` | `deadworks-server` | |
| `DEADWORKS_LOG_LEVEL` | `log_level` | `Information` | standard `LogLevel` names |

JSONC-only (no env override): `export_interval_ms=15000`,
`enable_traces=true`, `enable_metrics=true`, `trace_sampling_ratio=1.0`
(values `<1.0` wire `TraceIdRatioBasedSampler`).

The new `managed/Telemetry/` dir includes `DeadworksMetrics.cs`,
`DeadworksTelemetry.cs`, `DeadworksTracing.cs`,
`NativeEngineLoggerProvider.cs`, `NativeLogCallback.cs`,
`PluginLoggerRegistry.cs`. See
`managed/DeadworksManaged.Api/Logging/LogResolver.cs` for the
plugin-facing resolver indirection.

## Env-var passthrough for plugins

Any env var prefixed `DEADWORKS_ENV_*` on the launcher is forwarded to the
spawned game process (upstream `759d604` / `6d59ca5`, deadworks-3beeff54,
deadworks-530007be). Plugins roll their own getters — no centralized
env-var config helper in the host API.

`docker-compose` sets `SERVER_PORT` and `DEADWORKS_ENV_PORT` in lockstep
for each gamemode instance (server-plugins-0b7a496e). Distinct knobs: the
server binary reads `SERVER_PORT`, plugins read `DEADWORKS_ENV_PORT`.

## Upstream API churn (spring 2026)

Cluster of commits migrating entity wrappers from raw pointers to handles
(deadworks-4881cd7a, deadworks-530007be, deadworks-d48155c8):

- `e32ee21` store handles inside of `CBaseEntity` instead of raw pointers
- `18bd959` make `IsValid` check validity of handle AND existence of entity
- `0635838` expose `CBaseEntity.m_fFlags` and add `IsBot` helper
- `985245f` expose `CCitadelGameRules.m_bServerPaused`
- `f6ccd63` add player `SteamId64` to base controller
- `cd18e51` remove native hurt function, allow suicide by default
- `38a35cc` remove signonstate hooks (dropped `OnSignonState(ref string addons)`
  entirely — any plugin still implementing it fails to compile)

Launcher versioned independently from the plugin host; bumped to `0.2.10`
in `a71ac83`.

## v0.4.5 (2026-04-22)

Release notes: [[deadworks-0.4.5-release]]. Summary of the managed API
surface changes:

- `CCitadelPlayerPawn.AddItem` gains a `bool enhanced = false` overload
  parameter. `caller.AddItem(itemId, enhanced: true)` grants the enhanced
  variant.
- `CCitadelPlayerPawn.HeroID` exposed (previously internal). Plugins can
  now read a pawn's hero without going through schema accessors.
- **`CBasePlayerController.Slot`** added — canonical replacement for the
  `controller.EntityIndex - 1` idiom. `Chat.PrintToChat` and similar
  helpers use this mapping; plugins should prefer `controller.Slot` going
  forward.
- `CBasePlayerController.PrintToConsole` — was broken in earlier
  releases, now fixed. Plugins that suppressed usage because it silently
  did nothing can re-enable it.
- New API for **sending soundevents directly to players** (scoped to a
  single player rather than the default broadcast path).
- **`[Command]` attribute** supersedes `[ChatCommand]` and `[ConCommand]`.
  See the attribute list above for semantics.

Default listen port reverted to `27067` in the same release — see the
port note in the "replacement entry point" section above.

## v0.4.6 (2026-04-24)

Release notes: [[deadworks-0.4.6-release]]. Managed API surface changes:

- **Heroes no longer auto-precached.** `EntryPoint.OnPrecacheResources`
  used to iterate every `Heroes` enum value and call
  `Precache.AddHero(hero)` for each `AvailableInGame` entry before
  dispatching to plugins. That loop was removed (commit `0dcf287`).
  `Precache.AddHero` is still callable — plugins that dynamically
  swap heroes must now precache explicitly from their own
  `OnPrecacheResources`. **Impact:** smaller connecting-player
  working set; faster hot-join. No plugin in this repo currently
  overrides `OnPrecacheResources`.
- **Ability APIs added:**
  - `CCitadelAbilityComponent.FindAbilityByName(string)` — returns
    `CCitadelBaseAbility?` by internal ability name.
  - `CCitadelPlayerPawn.RemoveAbility(CCitadelBaseAbility)` — new
    overload taking the ability entity directly (returns `bool`).
- **`Entities.ByName` / `FirstByName` family** — four new static
  methods for targetname lookup, case-sensitive, cursor-backed native
  iteration of the engine's targetname index. Faster than scanning
  `Entities.All` with `.Where(targetname==)`. See
  [[schema-accessors]].
- **`CCitadelPlayerPawn.GetStamina() / SetStamina(float)`** — one-call
  stamina read/write. `SetStamina` writes `CurrentValue`,
  `LatchValue`, and `LatchTime = GlobalVars.CurTime` together.
- **`EntityData<T>` is `IEnumerable<KeyValuePair<CBaseEntity, T>>`**
  with a `Count` property. See [[schema-accessors]].
- **`CBaseEntity` equality is handle-based.** `==`/`!=`/`Equals`/
  `GetHashCode` now compare the packed `EntityHandle` (serial +
  index). Wrappers built twice for the same native entity compare
  equal. Unlocks `CBaseEntity` as a `Dictionary` / `HashSet` key. See
  [[schema-accessors]].
- **`AbilityResource.LatchTime` / `LatchValue` now network.** Prior
  versions used raw pointer writes that skipped
  `NotifyStateChanged`; v0.4.6 marks the accessors networked and
  routes writes through `SchemaAccessor.Set`. Makes the new
  `SetStamina` helper actually propagate to clients. See
  [[schema-accessors]].

No deprecations added in this release. The `[ChatCommand]` /
`[ConCommand]` deprecations from v0.4.5 remain pending removal; all
plugins in this repo already migrated.

## Host / fork split

Two remotes on `deadworks/` working trees:

- `origin = Deadworks-net/deadworks` (upstream, contributors have read-only
  push; 403 on push attempts)
- `fork = raimannma/deadworks` (personal, push target for PRs)

Docker infrastructure lives on the fork only (upstream has no `docker/`
dir). Fork's `main` tracks upstream via rebase rather than merge. Docker
workflows for the plugins repos must check out `raimannma/deadworks` until
upstream adds Docker infra (deathmatch-6d3a9327, deadworks-d48155c8,
deadworks-4972c10e).

The `docker-build` branch on the fork tracks Docker-specific commits
separately from the merged runtime work on `main`.

## External interop notes

- Pure memory writes to networked fields do NOT propagate (Source 2 is
  delta-encoded). Plugins must use the host's setters (which call
  `NotifyStateChanged`) or go through `ServerCommand`. See
  [[source-2-engine]].
- Under Wine/Proton, schema vtable calls crash — use binary-scan fallbacks
  (see [[source-2-engine]]).
- There is no centralized HTTP facility in the host API; plugins instantiate
  their own `static readonly HttpClient` (deathmatch-5233473a; see
  [[status-poker]]).
