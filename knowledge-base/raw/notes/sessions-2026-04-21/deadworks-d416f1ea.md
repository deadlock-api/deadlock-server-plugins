---
date: 2026-04-21
task: session extract — deadworks d416f1ea
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/d416f1ea-7f34-4a9f-b11c-289ced1d44c3.jsonl]
---

## Deadworks runtime

- `PluginLoader` maintains a `SharedAssemblies` map built at startup so every `PluginLoadContext` resolves host-shared types (`DeadworksManaged.Api`, `DeadworksManaged`, `Google.Protobuf`) to the same `Assembly` instance — this preserves type identity across plugins (e.g. `IDeadworksPlugin`). See `managed/PluginLoader.cs:70-89`.
- Plugin DLLs are loaded via `LoadFromStream` (not `LoadFromAssemblyPath`) using `File.ReadAllBytes` so the file on disk isn't locked by the runtime; `.pdb` sibling is loaded similarly when present. `managed/PluginLoader.cs:210-223`.
- `PluginLoadContext` is collectible (`base(isCollectible: true)`); `UnloadPlugin` disposes each plugin's `TimerService` before `OnUnload` so timers stop firing during teardown. `managed/PluginLoader.cs:14, 271-288`.
- File watcher reload uses a 500ms debounce (`_debounceTimer.Change(500, ...)`) aggregated across multiple `Changed`/`Created` events via `_pendingReloads` hashset. `managed/PluginLoader.cs:293-348`.
- `PluginLoader.LoadAll` initializes a fixed subsystem sequence: `TimerRegistry` -> `DeadworksConfig` -> `ConfigManager` -> `ConCommandManager` -> `ServerBrowser` -> `PluginStateManager`, then wires up `GameEvents.OnAddListener`, `NetMessages.OnSend/OnHookAdd/OnHookRemove`, and `EntityIO.OnHookOutput/Input` before scanning the `plugins/` dir. `managed/PluginLoader.cs:91-110`.
- Dispatch pattern: each plugin lifecycle event uses `DispatchToPlugins` (void) or `DispatchToPluginsWithResult` (HookResult). `HookResult` aggregation takes the max (`if (hr > result) result = hr;`) so any plugin returning a stronger value wins. `managed/PluginLoader.cs:374-391`.
- `DispatchStartupServer` cancels all map-change timers via `TimerRegistry.CancelAllMapChangeTimers()` and calls `ServerBrowser.OnStartupServer()` before dispatching to plugins. `managed/PluginLoader.cs:398-403`.
- `DispatchEntityDeleted` also calls `EntityDataRegistry.OnEntityDeleted(args.Entity.EntityHandle)` — plugin-attached entity data is auto-cleaned on entity delete. `managed/PluginLoader.cs:448-453`.
- `DispatchSignonState(ref string addons)` passes addons by ref to plugins — signon hook lets plugins append content addons to the connection response. `managed/PluginLoader.cs:479-486`.
- Server-list / content-addon feature (commit `5703a09`, "signon hooks, beginning server list and content addon") introduced a `ServerBrowser` static class referenced from `PluginLoader.cs:97,401,490` but the class file itself was never committed — the CI build fails with CS0103 because only locally-uncommitted work contains `ServerBrowser.cs`. `ServerBrowserConfig` lives in `managed/DeadworksConfig.cs` (API URL, heartbeat interval, content addons, extra maps, unlisted flag) and is already wired, waiting for the missing impl.
- Native-side companion for that feature: `A2SPatch.cpp/hpp` (server-query reply patch), `Hooks/ReplyConnection.{cpp,hpp}` and `Hooks/SendNetMessage.{cpp,hpp}` (signon + outbound netmsg hooks); all added in the same commit.

## Plugin build & deployment

- `DeadworksManaged.Api.csproj` has a post-build `DeployToGame` target (`AfterTargets="Build"`) that copies `DeadworksManaged.Api.dll`+`.pdb` to `$(DeadlockManagedDir)`. On CI the env var is empty, producing warning `MSB3023: No destination specified for Copy`. Fix: guard with `Condition="'$(DeadlockManagedDir)' != ''"` on the `<Copy>` element. `managed/DeadworksManaged.Api/DeadworksManaged.Api.csproj:34-39`.
- The Api csproj uses `<InternalsVisibleTo Include="DeadworksManaged" />` and `DeadworksManaged.Tests` — host and tests see `internal` Api members. It also registers the `DeadworksManaged.Generators` project as `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` (source generator, not a runtime dep). `managed/DeadworksManaged.Api/DeadworksManaged.Api.csproj:11-24`.
- Game event schemas are pulled in as `<AdditionalFiles Include="..\..\game_exported\*.gameevents" />` — generator consumes these at compile time. `managed/DeadworksManaged.Api/DeadworksManaged.Api.csproj:26-28`.
- Proto compilation: `<Protobuf Include="..\protos\**\*.proto" ProtoRoot="..\protos" GrpcServices="None" />` via `Grpc.Tools` 2.69.0; runtime is `Google.Protobuf` 3.29.3. Both must be shared with plugins to avoid duplicate-assembly identity issues (see SharedAssemblies above). `managed/DeadworksManaged.Api/DeadworksManaged.Api.csproj:17-18, 30-32`.
- Target framework is `net10.0` with `AllowUnsafeBlocks`, nullable-enabled, and `GenerateDocumentationFile=true`. `managed/DeadworksManaged.Api/DeadworksManaged.Api.csproj:3-9`.
