---
date: 2026-04-21
task: session extract — deadworks 5d3198bf
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/5d3198bf-6135-4e9c-9026-54c4e5128bf7.jsonl]
---

## Deadworks runtime

- `PluginLoader.LoadAll()` at `managed/PluginLoader.cs:91` initializes subsystems in a fixed order: `TimerRegistry`, `DeadworksConfig`, `ConfigManager`, `ConCommandManager`, `ServerBrowser`, `PluginStateManager`, then sets `PluginRegistry.Resolve`, wires `GameEvents.OnAddListener`/`OnRemoveListener`, calls `NetMessageRegistry.EnsureInitialized()`, assigns `NetMessages.OnSend`/`OnHookAdd`/`OnHookRemove`, and wires `EntityIO.OnHookOutput`.
- Assembly load map (`managed/PluginLoader.cs:80-86`) explicitly shares `DeadworksManaged` host assembly and `Google.Protobuf` (via `typeof(IMessage).Assembly`) with plugins — plugins must use the same protobuf runtime as the host or types won't match.
- `DispatchStartupServer()` at `managed/PluginLoader.cs:398` cancels all map-change timers via `TimerRegistry.CancelAllMapChangeTimers()` before calling `ServerBrowser.OnStartupServer()` and dispatching `OnStartupServer` to plugins — confirms the "CancelOnMapChange" timer semantic fires at server startup boundary.
- `DispatchGameFrame` at `managed/PluginLoader.cs:405` ticks `TimerEngine.OnTick()` each frame before dispatching to plugins, so `TimerEngine` is the per-frame tick source, not a separate thread.
- Managed source layout (flat under `managed/`): partial `PluginLoader` split into `.ChatCommands.cs`, `.EntityIO.cs`, `.Events.cs`, `.NetMessages.cs` + main `.cs`. Timer stack is 4 files: `TimerEngine`, `TimerHandle`, `TimerRegistry`, `TimerService`. Also `ConCommandManager`, `ConfigManager`, `DeadworksConfig`, `HandlerRegistry`, `NativeLogWriter`, `PluginRegistrationTracker`, `PluginStateManager`, `ScheduledTask`.
- `DeadworksConfig` exposes config sections as static accessors, e.g. `ServerBrowserConfig ServerBrowser => _root.ServerBrowser` at `managed/DeadworksConfig.cs:42` — the config class name collides with the (missing) `ServerBrowser` subsystem class, so bare `ServerBrowser.Initialize()` does not resolve to the config property (it's typed, not a static class).

## Plugin build & deployment

- Docker build pipeline publishes in two stages per `Dockerfile:100`: `dotnet publish managed/DeadworksManaged.csproj -c Release -o /artifacts/managed --no-self-contained`. The `DeadworksManaged.Api.csproj` produces a separate DLL (`managed/DeadworksManaged.Api/bin/Release/net10.0/DeadworksManaged.Api.dll`) — targets `net10.0`.
- Build target framework is `net10.0` (seen in generator output paths `obj/Release/net10.0/DeadworksManaged.Generators/...`). Source generator `DeadworksManaged.Generators.GameEventSourceGenerator` produces `GameEvents.g.cs` and `GameEventFactory.g.cs` with XML-doc warnings (CS1591) that do not fail the build.
- `DeadworksManaged.Api.csproj:38` has a `Copy` task warning MSB3023 (no `DestinationFiles`/`DestinationFolder`) — non-fatal but latent.
- Build failure root cause identified: upstream commit `5703a09` ("signon hooks, beginning server list and content addon" by perrccyy) added three calls to a `ServerBrowser` static class at `managed/PluginLoader.cs:97,401,490` but did NOT commit the `ServerBrowser.cs` source file. Branch `docker-build` inherited the broken state via merge of main. Only `ServerBrowserConfig` (typed property on `DeadworksConfig`) exists — the bare `ServerBrowser.Initialize()`/`OnStartupServer()`/`Shutdown()` calls fail with CS0103.
- `docker-build` branch diverges from main at `2fcb96a2...`; notable branch-specific commits: `e9ef96f` (initial Dockerfile/cross-compile scripts), `c942151` (refactor Dockerfile for plugin mgmt), `dbcf3b7` (GH Actions Docker build/push), `5cb25b3` (add managed build step), `1929f79` (only copy requisite managed files).
- Upstream (main branch) appears to sometimes ship broken HEAD — downstream forks must audit merges. This is the second plugin-repo issue in the session stream implying upstream Deadworks main is not always build-clean.
