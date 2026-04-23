---
title: PluginLoader (C# managed)
type: entity
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-4881cd7a.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-530007be.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-5d3198bf.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-bc59e6cf.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-d416f1ea.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-ec2918a5.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-3636296d.md
  - raw/notes/2026-04-23-plugin-native-dll-resolution.md
related:
  - "[[deadworks-runtime]]"
  - "[[plugin-build-pipeline]]"
  - "[[deadworks-scan-2026-04-23]]"
created: 2026-04-21
updated: 2026-04-23
confidence: high
---

# PluginLoader (managed)

`managed/PluginLoader.cs` is the C# class that discovers, loads, hot-
reloads, and dispatches events to plugin DLLs inside the Deadworks host.

## Discovery

- Scan location: `[DeadworksManaged.dll dir]/plugins/*.dll`
  (`PluginLoader.cs:114-123`, deadworks-bc59e6cf).
- **No manifest, no hardcoded names** — purely filesystem-driven.
- Plugin type filter:
  ```csharp
  assembly.GetTypes().Where(t =>
    typeof(IDeadworksPlugin).IsAssignableFrom(t)
    && !t.IsInterface && !t.IsAbstract)
  ```
  Instantiated via `Activator.CreateInstance` (`PluginLoader.cs:223-224`).
- Hook registration scanned reflectively via attributes like
  `[GameEventHandler("event_name")]` at load time
  (`PluginLoader.Events.cs:19-60`). A central `PluginRegistrationTracker`
  indexes all per-plugin registrations for clean unload.

## Known plugins in tree (Apr 2026)

`deadworks/examples/plugins/` (deadworks-bc59e6cf, deadworks-4972c10e):

- AutoRestartPlugin, ChatRelayPlugin, DeathmatchPlugin, DumperPlugin,
  ExampleTimerPlugin, ItemRotationPlugin, ItemTestPlugin, RollTheDicePlugin,
  ScourgePlugin, SetModelPlugin, TagPlugin.
- Upstream commit `189cf2a` split plugins from `managed/plugins/` into
  `examples/plugins/`. `managed/plugins/` still exists as the build
  artifact dir.
- `examples/ExamplePlugins.slnx` lists 11 plugins including
  `ChatRelayPlugin`, which is **not** present in `managed/plugins/`
  artifact mirror (only 10 there) — ChatRelay is examples-only.

## Partial-class layout

`PluginLoader.cs` is split by domain (deadworks-4881cd7a, deadworks-530007be):

- `PluginLoader.cs` (core + fixed-order subsystem init + `DispatchStartupServer`,
  `DispatchGameFrame`, etc.)
- `PluginLoader.ChatCommands.cs`
- `PluginLoader.EntityIO.cs`
- `PluginLoader.Events.cs`
- `PluginLoader.NetMessages.cs`

## Load sequence (`LoadAll()`)

Fixed subsystem init order (`PluginLoader.cs:91-110`, deadworks-5d3198bf,
deadworks-d416f1ea):

1. `TimerRegistry`
2. `DeadworksConfig`
3. `ConfigManager`
4. `ConCommandManager`
5. `ServerBrowser`
6. `PluginStateManager`
7. Set `PluginRegistry.Resolve`
8. Wire `GameEvents.OnAddListener` / `OnRemoveListener`
9. `NetMessageRegistry.EnsureInitialized()`
10. Assign `NetMessages.OnSend` / `OnHookAdd` / `OnHookRemove`
11. Wire `EntityIO.OnHookOutput` / `.OnHookInput`
12. Scan the `plugins/` dir and load each DLL.

## Plugin loading mechanics

Each plugin DLL loads in its own collectible `PluginLoadContext`
(`AssemblyLoadContext(isCollectible: true)`) — per-plugin isolation with
hot-reload support (`PluginLoader.cs:14`, deadworks-bc59e6cf,
deadworks-d416f1ea):

- Loaded via `LoadFromStream` with bytes from `File.ReadAllBytes` —
  **file on disk is NOT locked** by the runtime.
- `.pdb` sibling loaded the same way when present
  (`PluginLoader.cs:210-223`).
- `UnloadPlugin` disposes each plugin's `TimerService` **before**
  calling `OnUnload` so timers stop firing during teardown
  (`PluginLoader.cs:271-288`).
- Enable/disable state persisted in `configs/plugins.jsonc` via
  `PluginStateManager`; console command is `dw_plugin enable/disable <name>`.
  Disabled plugins skip reload-on-DLL-change.

## Shared assemblies

`PluginLoader` builds a `SharedAssemblies` map at startup
(`PluginLoader.cs:70-89`, deadworks-d416f1ea, deadworks-5d3198bf):

- `DeadworksManaged.Api`
- `DeadworksManaged` (host)
- `Google.Protobuf` (via `typeof(IMessage).Assembly`)

Each `PluginLoadContext` resolves these host-shared types to the **same**
`Assembly` instance, preserving type identity for `IDeadworksPlugin`,
protobuf `IMessage<T>`, and everything the host marshals.

Plugin csprojs pair this with:

- `<Private>false</Private>`
- `<ExcludeAssets>runtime</ExcludeAssets>`

on the `DeadworksManaged.Api` reference — so only the host's copy loads
at runtime. See [[plugin-build-pipeline]] for the same pattern applied to
`Google.Protobuf`.

## Native-DLL resolution for plugins

Upstream commit `f9a876c` ("fix: resolve native DLLs for plugins in
isolated AssemblyLoadContext", 2026-04-14; canonical SHA on `main` —
the equivalent pre-rebase SHA `211583e` is unreachable from current
branches) adds a `LoadUnmanagedDll` override to `PluginLoadContext`
(`PluginLoader.cs:39-48`):

```csharp
protected override nint LoadUnmanagedDll(string unmanagedDllName)
{
    var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
    if (path != null)
        return NativeLibrary.Load(path);
    return nint.Zero;
}
```

This uses the plugin's own `AssemblyDependencyResolver` — i.e., the
plugin's `deps.json` — so plugins can bundle native dependencies in
`runtimes/<rid>/native/` and they'll resolve at `[DllImport]` time.
The motivating case in the commit message: `Microsoft.Data.Sqlite` /
`e_sqlite3.dll`.

Before this fix, the CLR's default unmanaged resolver only probed the
process directory (`…/game/bin/win64/`), so plugins with native deps
silently failed with `DllNotFoundException` on first use.

Managed and native resolution differ:

- **Managed** (`Load`): shared-host assemblies first (identity types
  like `IDeadworksPlugin`), then the per-plugin resolver.
- **Native** (`LoadUnmanagedDll`): per-plugin resolver only — there is
  no "shared native" concept. Each plugin that needs the same
  `e_sqlite3.dll` ships its own copy.

## Hot reload

- `FileSystemWatcher` on the `plugins/` dir drives reload
  (deadworks-88df5d67, deadworks-d416f1ea).
- 500ms debounce: `_debounceTimer.Change(500, …)` aggregates multiple
  `Changed`/`Created` events via `_pendingReloads` hashset
  (`PluginLoader.cs:293-348`).

## Dispatch

Per-event static wrappers call into one of two helpers
(deadworks-4881cd7a, deadworks-530007be, deadworks-d416f1ea):

- `DispatchToPlugins(p => p.OnX(args), nameof(IDeadworksPlugin.OnX))` —
  fire-and-forget (void-return hooks).
- `DispatchToPluginsWithResult(...)` returning `HookResult` — pre-hooks
  with veto. `HookResult` aggregation takes the max:
  `if (hr > result) result = hr;` — any plugin returning a stronger
  value wins (`PluginLoader.cs:374-391`).

**Error isolation**: each plugin call wrapped in try/catch that logs via
`_logger.LogError(ex, "Plugin {PluginName}.OnX error", plugin.Name)` so
one plugin throwing does not abort dispatch to the rest.

**Iteration safety**: uses `_pluginSnapshot` — immutable snapshot taken
on load. Plugins can register/unregister concurrently without mutating
the collection being iterated.

## Notable dispatch methods

From `managed/PluginLoader.cs:398-492` (deadworks-d416f1ea,
deadworks-5d3198bf):

- `DispatchStartupServer` — cancels all map-change timers via
  `TimerRegistry.CancelAllMapChangeTimers()`, calls
  `ServerBrowser.OnStartupServer()`, then dispatches `OnStartupServer` to
  plugins. Confirms "CancelOnMapChange" timer semantic fires at server
  startup boundary.
- `DispatchGameFrame` — ticks `TimerEngine.OnTick()` each frame **before**
  dispatching `OnGameFrame` to plugins. `TimerEngine` is the per-frame
  tick source, NOT a separate thread.
- `DispatchEntityDeleted` — calls
  `EntityDataRegistry.OnEntityDeleted(args.Entity.EntityHandle)` to
  auto-clean plugin-attached per-entity data.
- `DispatchAddModifier` — pre-hook with veto (`HookResult`).
- `DispatchSignonState(ref string addons)` — **removed** in upstream
  `38a35cc` (see [[deadworks-runtime]] for the removal).

## Known API removals / additions

Upstream `38a35cc` ("remove signonstate hooks", 2026-04-17;
deadworks-4881cd7a, deadworks-530007be, deathmatch-c51730eb):

- Deleted `IDeadworksPlugin.OnSignonState(ref string addons)`.
- Deleted `DispatchSignonState` from `PluginLoader.cs`.
- Any plugin still implementing `OnSignonState` fails to compile.

Upstream `5703a09` ("signon hooks, beginning server list and content
addon") added references to a `ServerBrowser` static class at
`PluginLoader.cs:97,401,490` but **never committed the `ServerBrowser.cs`
source file** (deadworks-5d3198bf, deadworks-d416f1ea). CI build fails
with CS0103 because only `ServerBrowserConfig` (a typed property on
`DeadworksConfig`) exists. This is a known upstream-broken-HEAD case.

## Sibling managed classes

Flat under `managed/` (deadworks-5d3198bf):

- Timer stack: `TimerEngine`, `TimerHandle`, `TimerRegistry`,
  `TimerService`.
- `ConCommandManager`, `ConfigManager`, `DeadworksConfig`,
  `HandlerRegistry`, `NativeLogWriter`, `PluginRegistrationTracker`,
  `PluginStateManager`, `ScheduledTask`, `EntryPoint`,
  `EntityDataRegistry`, `NetMessageRegistry`.

## Build artifact layout

Per `Dockerfile:100-106` (deadworks-a54dc08d, deadworks-5d3198bf):

- Host: `dotnet publish managed/DeadworksManaged.csproj -c Release -o /artifacts/managed --no-self-contained`
- Plugins: `find examples/plugins -name '*.csproj' -not -path '*/.*' -not -name '*.Tests.csproj' | xargs dotnet publish -c Release -o /artifacts/managed/plugins --no-self-contained`
- Both target `net10.0`.
- `EnableDynamicLoading=true` set on both host and plugin csprojs —
  required for AssemblyLoadContext isolation (deathmatch-3636296d).
