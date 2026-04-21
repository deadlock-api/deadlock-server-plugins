---
date: 2026-04-21
task: session extract — deadworks 4881cd7a
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/4881cd7a-0912-4356-b583-d7f45fb548e6.jsonl]
---

## Deadworks runtime

- Upstream commit `38a35cc` ("remove signonstate hooks", perrccyy 2026-04-17) deleted the SignonState plumbing across both native and managed sides: `deadworks/src/Core/Deadworks.{cpp,hpp}`, `deadworks/src/Core/Hooks/SendNetMessage.{cpp,hpp}`, `deadworks/src/Core/ManagedCallbacks.{cpp,hpp}`, `config/deadworks_mem.jsonc`, plus `managed/DeadworksManaged.Api/{IDeadworksPlugin,DeadworksPluginBase}.cs`, `managed/EntryPoint.cs`, and `managed/PluginLoader.cs` (13 files, 134 deletions). `IDeadworksPlugin.OnSignonState(ref string addons)` is gone — any plugin still implementing it will fail to compile.
- Managed dispatcher pattern in `managed/PluginLoader.cs`: per-event static wrappers call either `DispatchToPlugins(p => p.OnX(args), nameof(IDeadworksPlugin.OnX))` (fire-and-forget) or `DispatchToPluginsWithResult(...)` which returns `HookResult` (e.g. `DispatchAddModifier` at line ~569 is a pre-hook with veto). Errors per plugin are swallowed and routed through `_logger.LogError(ex, "Plugin {PluginName}.OnX error", plugin.Name)` so one plugin throwing does not abort dispatch to the rest.
- Iteration uses `_pluginSnapshot` — an immutable snapshot taken on load so plugins can register/unregister without mutating the collection being iterated.
- Signon addon propagation used `ref string addons` — removed commit indicates the native side no longer intercepts `SendNetMessage` for content-addon injection at signon; replacement mechanism lives elsewhere (not covered in this session).
- Recent upstream work clustered around entity handle safety: `e32ee21` "store handles inside of CBaseEntity instead of raw pointers", `18bd959` "make IsValid check validity of handle and existence of entity", `0635838` "expose CBaseEntity.m_fFlags and add IsBot helper", plus `985245f` exposing `CCitadelGameRules.m_bServerPaused`. These reflect an ongoing shift away from raw pointer caching in managed API.

## Plugin build & deployment

- The repo uses two remotes named unusually: `origin` → `https://github.com/Deadworks-net/deadworks.git` (upstream), `fork` → `git@github.com:raimannma/deadworks.git` (personal). Working branch `feat/telemetry-otel` tracks `fork/...`, not `origin`.
- The telemetry branch adds a new `managed/Telemetry/` directory with `DeadworksMetrics.cs`, `DeadworksTelemetry.cs`, `DeadworksTracing.cs`, `NativeEngineLoggerProvider.cs`, `NativeLogCallback.cs`, `PluginLoggerRegistry.cs`, plus `managed/DeadworksManaged.Api/Logging/LogResolver.cs`. Touches `ConCommandManager.cs`, `ConfigManager.cs`, `DeadworksConfig.cs`, `EntryPoint.cs`, `PluginLoader.{ChatCommands,EntityIO,Events,NetMessages}.cs`, `PluginStateManager.cs`, `ServerBrowser.cs`, `TimerEngine.cs`, and both `.csproj` files — i.e. OTel instrumentation spans the whole managed runtime, not a single subsystem.
- `PluginLoader.cs` is partial-classed across `.ChatCommands`, `.EntityIO`, `.Events`, `.NetMessages` files — dispatch/event surface is split by domain rather than one monolithic file.
- Upstream has also split plugins into a separate examples solution (`189cf2a` "split plugins to new examples solution") — relevant for anyone grepping for plugin examples in the main solution.
- Rebase workflow gotcha: `git rebase origin/main` on a telemetry branch hit a conflict in `managed/PluginLoader.cs` where the local commit added `DispatchSignonState` next to `DispatchAddModifier`/`DispatchCheckTransmit` but upstream (`38a35cc`) had already deleted the whole signon path. Correct resolution was to drop `DispatchSignonState` entirely (not keep it) since `IDeadworksPlugin.OnSignonState` no longer exists. After resolution, force-push to the fork used `--force-with-lease fork feat/telemetry-otel` (note: "origin" is upstream here, so never force-push to origin).
