---
date: 2026-04-21
task: session extract — deathmatch 6d3a9327
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-deathmatch/6d3a9327-777f-4c6d-ba75-bbb98a277176.jsonl]
---

## Deadworks runtime

- `deadlock-deathmatch/DeathmatchPlugin.csproj` (pre-restructure) used `<ProjectReference Include="..\deadworks\managed\DeadworksManaged.Api\..."/>` assuming a sibling-repo layout of `deadworks/` next to `deadlock-deathmatch/`. `Directory.Build.props` conditionally imports `..\deadworks\managed\Directory.Build.props` only if it exists.
- `deadworks/managed/Directory.Build.props` sets `DeadlockManagedDir=$(DeadlockDir)\managed` when `DeadlockDir` is non-empty, used by `local.props` for local dev builds targeting an installed Deadlock copy.
- `Properties/launchSettings.json` in the deathmatch plugin launches the Windows deadworks.exe directly from `C:\Program Files (x86)\Steam\steamapps\common\Deadlock\game\bin\win64\deadworks.exe` — implies local plugin debugging attaches to a real Steam-installed Deadlock client, not a containerised server.
- StatusPoker plugin (copied from `deadlock-api/deadlock-server-plugins/StatusPoker`) calls `DeadworksPluginBase` with `Name` override and uses a `System.Threading.Timer` against `DeadworksManaged.Api` — so plugin subclasses live standalone with just the Api reference.

## Plugin build & deployment

- Two diverging Dockerfile strategies observed for deadworks plugin bundling. Old flow (referenced by CLAUDE.md): plugin subdirs merged into `managed/plugins/` during the managed-builder stage, so csprojs use `<ProjectReference>` with path `..\..\DeadworksManaged.Api\...`. New flow (current `raimannma/deadworks@main` `docker/Dockerfile`): managed layer is published first to `/artifacts/managed`, then the plugin build-context runs `dotnet publish` on each `extra-plugins/*/*.csproj` with an injected `Directory.Build.targets` that sets `<AssemblySearchPaths>$(AssemblySearchPaths);/artifacts/managed</AssemblySearchPaths>`. Plugins therefore must use `<Reference Include="DeadworksManaged.Api" />`, not `ProjectReference`, to resolve inside Docker.
- Gotcha: `ProjectReference` still works for local-dev sibling builds; the CI fix uses dual-mode csprojs — `<Reference>` for Docker, and keeps `<ProjectReference>` conditional for local. Errors when wrong: `CS0246: The type or namespace name 'DeadworksManaged' could not be found` / `GameEventHandler could not be found` despite the csproj compiling locally.
- The plugin build-context loop in the Dockerfile: `find extra-plugins -name '*.csproj' -not -path '*/.*' -not -name '*.Tests.csproj' | xargs dotnet publish -c Release -o /artifacts/managed/plugins --no-self-contained`. Skips `.Tests.csproj` automatically; every top-level `extra-plugins/*/*.csproj` is published.
- Upstream `Deadworks-net/deadworks` (the official) does NOT contain a `docker/` directory — only the fork `raimannma/deadworks` does, on `main`. CI pointed at the upstream failed with `failed to build: resolve : lstat deadworks/docker: no such file or directory`. Workflows for this gamemode must checkout `raimannma/deadworks` until upstream adds Docker infra.
- `docker/build-push-action@v7` invocation pattern used: `build-contexts: extra-plugins=<path>` passes a named additional build context that the Dockerfile consumes via `FROM busybox AS extra-plugins` + `COPY --from=extra-plugins`. Enables plugin sources to come from outside the host checkout without modifying the host Dockerfile.
- `deadlock-deathmatch` layout after restructure: `plugins/DeathmatchPlugin/`, `plugins/StatusPoker/` each with `.csproj + .cs`; the slnx references both plus `..\deadworks\managed\DeadworksManaged.Api\DeadworksManaged.Api.csproj`. CI build-context root is `plugins/`, so `extra-plugins/<PluginName>/<PluginName>.csproj` matches the Dockerfile find pattern.
- Latest GH Action majors confirmed via API (2026-04): `actions/checkout@v6` (v6.0.2), `docker/setup-buildx-action@v4` (v4.0.0), `docker/login-action@v4` (v4.1.0), `docker/build-push-action@v7` (v7.1.0), `docker/setup-qemu-action@v4` (v4.0.0). Floating major tags track minor/patch automatically.
- Image tagged `ghcr.io/raimannma/deadlock-deathmatch/game-server:latest`, pushed only on push events (not PR). Uses `cache-from/to: type=gha, mode=max` for buildx cache.
- Example plugins in deadworks repo listed: `AutoRestartPlugin, ChatRelayPlugin, DeathmatchPlugin, DumperPlugin, ExampleTimerPlugin, ItemRotationPlugin, ItemTestPlugin, RollTheDicePlugin, ScourgePlugin, SetModelPlugin, TagPlugin` under `deadworks/examples/plugins/`.
