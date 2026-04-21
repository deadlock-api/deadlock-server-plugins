---
date: 2026-04-21
task: session extract — deadworks 3beeff54
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/3beeff54-fde3-4690-be39-3f45112f6096.jsonl]
---

## Deadworks runtime

- `DeadworksPluginBase` exposes `Timer` as an instance property (`protected ITimer Timer => TimerResolver.Get(this);`) at `managed/DeadworksManaged.Api/DeadworksPluginBase.cs:13`. Plugins that derive from it call `Timer.Once(...)` / `Timer.Every(...)` as instance access — calling `Timer` from a `static` method produces `CS0120: object reference required`, which is how the Deathmatch `TryHeal` static-helper bug surfaced in the Docker build.
- Plugin config hot-reload hooks into `OnConfigReloaded()`; Deathmatch uses it to restart its swap timer via `_swapTimer?.Cancel()` + `Timer.Every(...)` (`examples/plugins/DeathmatchPlugin/DeathmatchPlugin.cs:94-100`). Cancelled `IHandle` tokens are the idiomatic way to restart recurring timers on reload.
- `EntityData<T>` (per-entity state) and `SchemaAccessor<T>` with UTF-8 byte-literal class/field pairs (`new("CitadelAbilityVData"u8, "m_nAbilityTargetTypes"u8)`) are the canonical API for schema field access in plugins — seen at `deadlock-deathmatch/plugins/DeathmatchPlugin/DeathmatchPlugin.cs:29-35`.
- Plugin-scope env passthrough: the `DEADWORKS_ENV_*` prefix forwards host env vars into the game process for plugin consumption (commit 759d604 `forward DEADWORKS_ENV_* variables to game process for plugin use`); docker-compose sets both `SERVER_PORT` and `DEADWORKS_ENV_PORT`.

## Plugin build & deployment

- `docker/build-native.sh` contains an explicit per-file source list for the native `deadworks.exe` cross-compile (around line 140). Upstream commit `38a35cc` deleted `src/Core/Hooks/SendNetMessage.{cpp,hpp}` but did not edit `build-native.sh`, so the fork's line 149 referenced a missing source and builds failed. Fix: drop the `${SRC}/Core/Hooks/SendNetMessage.cpp \` line. Commit `4cb64c5` on `fork/main`; `fork/docker-build` already had it removed in `67a1cd1`.
- Dockerfile plugin layering (`docker/Dockerfile:85-107`): an empty `busybox` stage named `extra-plugins` is the default build context, overridden via `docker build --build-context extra-plugins=./my-plugins`. Stage 2 (`managed-builder`, `mcr.microsoft.com/dotnet/sdk:10.0`) copies the extra plugins into `examples/plugins/`, publishes `managed/DeadworksManaged.csproj` to `/artifacts/managed`, then finds+publishes every `*.csproj` under `examples/plugins` to `/artifacts/managed/plugins`. Runtime stage derives from `cm2network/steamcmd:root`.
- Extra-plugins assembly resolution: a generated `extra-plugins/Directory.Build.targets` sets `<AssemblySearchPaths>$(AssemblySearchPaths);/artifacts/managed</AssemblySearchPaths>` so plugin csprojs referencing `DeadworksManaged.Api` as a bare `<Reference>` (not `<ProjectReference>`) resolve the assembly from the published managed output. This is why commit `ccdd9c4` switched from `Directory.Build.props` to `.targets` — props evaluate too early, before `AssemblySearchPaths` is populated.
- The extra-plugins build step uses `find … -not -path '*/.*' -not -name '*.Tests.csproj'` to skip hidden dirs and test projects; `xargs -r` skips entirely if zero csprojs.
- CI pattern for a plugin-only repo (`deadlock-deathmatch/.github/workflows/docker.yml`): checkout `raimannma/deadworks` into `deadworks/`, checkout own repo into `deathmatch/`, then `build-push-action` with `context: deadworks`, `file: deadworks/docker/Dockerfile`, and `build-contexts: extra-plugins=deathmatch/plugins`. Image tag `ghcr.io/${{ github.repository }}/game-server:latest`.
- Source 2 `-con_logfile` path gotcha (commit `5d663ad`): writes relative to the mod directory (`game/citadel/`), not process cwd. `docker/entrypoint.sh` was tailing `win64/console.log` and watching an empty file while the real log accumulated at `game/citadel/console.log`.
- Multi-server sharing (commit `8836487` + `38492cf`): docker-compose uses named volumes (`proton`, `gamefiles`, `dotnet-cache`, `gamedata`, `compatdata`) and `managed/plugins` is excluded from build context so a single image supports multiple game-server instances.
- Fork topology: `origin = Deadworks-net/deadworks` (upstream), `fork = raimannma/deadworks`. The `fork/docker-build` branch tracks Docker changes separately (`67a1cd1 fix docker build after rebase: add missing sources and update plugins path`) while `fork/main` carries the merged runtime work (5d663ad, 2f4c489, c390f81, 8836487, 38492cf). Fixes often need replicating across both.
