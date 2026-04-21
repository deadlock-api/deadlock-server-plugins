---
date: 2026-04-21
task: session extract — deadworks 530007be
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/530007be-5d51-4d66-949d-034148698b18.jsonl]
---

## Deadworks runtime

- `managed/PluginLoader.cs` owns per-hook dispatch methods named `DispatchXxx` that iterate `_pluginSnapshot` and invoke `IDeadworksPlugin.OnXxx`, wrapping each plugin call in a try/catch that logs via `_logger.LogError(ex, "Plugin {PluginName}.OnXxx error", plugin.Name)`. Example `DispatchAddModifier` uses helper `DispatchToPluginsWithResult(p => p.OnAddModifier(args), nameof(IDeadworksPlugin.OnAddModifier))` while void hooks use `DispatchToPlugins(...)` (managed/PluginLoader.cs:~569).
- Upstream commit `38a35cc remove signonstate hooks` dropped the `OnSignonState(ref string addons)` plugin hook entirely; the corresponding `DispatchSignonState(ref string addons)` body was therefore removed from `PluginLoader.cs` during rebase. Any fork-local plugin still declaring `OnSignonState` must be ported to a different mechanism.
- The telemetry branch (commit `1b4d7e5 add structured logging, metrics, and traces via Microsoft.Extensions.Logging + OpenTelemetry`) is the source of the `_logger` field used in dispatch; this is what makes per-hook exception logging structured rather than Console.WriteLine.
- Plugin assembly isolation: commit `211583e fix: resolve native DLLs for plugins in isolated AssemblyLoadContext` — plugins run in an isolated ALC and need explicit native-DLL resolution. Related: `9719936 use Directory.Build.targets instead of props for plugin assembly resolution` (targets run after project references are known, props don't).
- `6d59ca5 forward DEADWORKS_ENV_* variables to game process` — any env var prefixed `DEADWORKS_ENV_` on the launcher is propagated into the spawned game process so plugins can read it.
- `b3d3af5 fix extra-plugin builds failing to resolve DeadworksManaged.Api` — external plugin builds need explicit reference to `DeadworksManaged.Api`; relevant when extending the csproj fallback logic mirrored in deadlock-server-plugins.

## Deadlock game systems

- Upstream commit `e32ee21 store handles inside of CBaseEntity instead of raw pointers` — entities now keep `CHandle` rather than raw pointers; `18bd959 make IsValid check validity of handle and existence of entity` updated `IsValid` accordingly. Downstream code assuming raw pointer stability must be revisited.
- `0635838 expose CBaseEntity.m_fFlags and add IsBot helper` — `m_fFlags` now exposed to managed side and an `IsBot` helper was added (bot detection was previously ad-hoc).
- `985245f expose CCitadelGameRules.m_bServerPaused` — managed can now read `m_bServerPaused` on the game rules entity.
- `cd18e51 remove native hurt function and make it allow suicide by default` — native hurt helper removed; suicide is now allowed by default without going through that shim. Relevant to !stuck/!suicide chat cmds added in deathmatch plugin (commit a81201b in deadlock-server-plugins).
- `f6ccd63 Add player SteamId64 to Base Controller` — `SteamId64` accessor is on the base player controller, not a subtype.

## Plugin build & deployment

- Docker image management: `177d4d4 keep base Docker image plugin-free by default`, `188b5d7 exclude managed/plugins from docker build context`, `7650f00 fix: clean managed dir before deploy to prevent stale plugins` — the base image ships without plugins; `managed/plugins` is gitignored from Docker context; deploy wipes `managed/` first to avoid stale DLLs lingering.
- `46be6d3 support sharing docker installation across multiple game servers` — single Deadworks install can back multiple compose services.
- `55e81e9 feat: stream console log to stdout for docker compose logs` + `2cedee8 fix: tail correct console.log path written by Source 2` — Source 2 writes `console.log` to a specific path that differs from the expected location; the tail path had to be corrected. Check this if `docker compose logs` appears empty.
- `d923193 fix docker build: add CheckTransmit.cpp to native build script` and `6498467 add missing source files to Docker build compile list` — the native build script's source list is maintained manually; new .cpp files must be added explicitly or they silently vanish in Docker builds.
- `189cf2a split plugins to new examples solution` — example plugins were moved out of the main solution into a separate one upstream.

## Git workflow (non-obvious)

- Working tree confirmed tracks two remotes: `origin` (upstream Deadworks-net) and `fork` (user's personal fork). Rebases are onto `origin/main`; pushes go to `fork/main` with `--force-with-lease`.
