---
title: Plugin build & deployment pipeline
type: concept
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-0b7a496e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-90226db4.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-328372c6.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-3beeff54.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-4972c10e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-52a01b09.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-543d455d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-5d3198bf.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-a54dc08d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-aabd306f.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-d48155c8.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-ddfface7.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-310fc296.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-3636296d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-6d3a9327.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-c51730eb.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-fa5d1d7e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-65d13a2e.md
related:
  - "[[docker-build]]"
  - "[[proton-runtime]]"
  - "[[deadworks-runtime]]"
  - "[[protobuf-pipeline]]"
  - "[[deadworks-plugin-loader]]"
created: 2026-04-21
updated: 2026-04-21
confidence: high
---

# Plugin build & deployment pipeline

How source code in this repo becomes a running per-gamemode container image.

## Three-stage Docker build

`docker/Dockerfile` in the deadworks fork has three stages
(deadworks-a54dc08d, deadworks-aabd306f, deadworks-52a01b09):

### Stage 1 — `native-builder` (`ubuntu:24.04`)

Cross-compiles `deadworks.exe` from Linux to Windows x64. Toolchain:

- **LLVM 20** (`clang-20`, `lld-20`) via LLVM apt repo.
- `clang-cl` and `lld-link` are shell wrappers that force
  `--driver-mode=cl` / `-flavor link` — argv[0]-based driver-mode detection
  is unreliable in Docker (deadworks-4972c10e).
- **xwin 0.6.5** splats MSVC SDK + CRT into `/xwin` (headers at
  `/xwin/crt/include`, `/xwin/sdk/include/{ucrt,um,shared}`).
- **nethost** — Microsoft.NETCore.App.Host.win-x64 NuGet (10.0.0) pulled
  at build time for `nethost.h`, `coreclr_delegates.h`, `hostfxr.h`,
  `libnethost.lib` (deadworks-328372c6).
- **Protobuf 3.21.8** built from source tarball with clang-cl using
  `docker/toolchain-clangcl.cmake`:
  - `CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded`, `CMP0091=NEW`,
    `protobuf_MSVC_STATIC_RUNTIME=ON`
  - `CMAKE_TRY_COMPILE_CONFIGURATION=Release` (otherwise CMake compiler test
    links `msvcrtd.lib` before `/MT` is applied).
  - Tarball preferred over `git clone` — "more reliable than git clone in
    BuildKit".

Gotchas (deadworks-aabd306f, deadworks-bc59e6cf):

- **C++23 flag**: clang-cl's `/std:c++23` does NOT set `__cplusplus`
  correctly, which breaks MSVC STL. Must pass `-Xclang -std=c++23` to reach
  the compiler frontend (build-native.sh `CXX23_FLAGS`).
- **Source SDK `__restrict` hack**: a decl/def mismatch in `bitbuf.h` is a
  hard error under clang-cl. Patched via `/D__restrict=` (empty macro) in
  `SDK_FLAGS`.
- **Native source list** (`docker/build-native.sh`) is a hand-maintained
  bash array — missing entries fail at link time only
  (deadworks-4972c10e, deadworks-3beeff54). Upstream churn in
  `Core/Hooks/*.cpp` (add/remove) requires matching edits to this file.
- `build-native.sh` iterates `for f in protobuf/*.pb.cc`; after the
  build-time protobuf migration, generates into `generated/protobuf/`
  instead (deadworks-ec2918a5).

Link: `lld-link /SUBSYSTEM:CONSOLE` with `sourcesdk/lib/win64/tier0.lib`,
`libprotobuf.lib`, `libnethost.lib`, `advapi32.lib`, `ole32.lib`.

### Stage 2 — `managed-builder` (`mcr.microsoft.com/dotnet/sdk:10.0`)

- Publishes `managed/DeadworksManaged.csproj` to `/artifacts/managed`:
  `dotnet publish managed/DeadworksManaged.csproj -c Release -o /artifacts/managed --no-self-contained`.
- Auto-discovers plugin csprojs (deadworks-328372c6,
  deadworks-aabd306f):
  ```
  find examples/plugins -name '*.csproj' -not -path '*/.*' -not -name '*.Tests.csproj' -print0 \
    | xargs -0 -r dotnet publish -c Release -o /artifacts/managed/plugins --no-self-contained
  ```
  `-r` handles empty dirs gracefully; `*.Tests.csproj` skipped automatically.
- `--no-self-contained` is deliberate: plugins share the single managed
  runtime already published alongside `DeadworksManaged`.
- `EnableDynamicLoading=true` on both host and plugin csprojs — required
  for `AssemblyLoadContext` isolation (deathmatch-3636296d).

### Stage 3 — `runtime` (`cm2network/steamcmd:root`)

- `dpkg --add-architecture i386` + large list of 32-bit Wine/Proton deps
  (freetype, vulkan, x11, gnutls, SDL2, NSS, fontconfig, gtk3, etc.).
- Copies `deadworks.exe`, `deadworks_mem.jsonc`, and
  `/artifacts/managed/*` into an `/opt/deadworks/` tree inside the image.
- Entrypoint copies from `/opt/deadworks` into the gamedata volume on each
  start (so the volume can be shared across containers).

Note: upstream `Deadworks-net/deadworks` has NO `docker/` directory —
only the fork carries Docker infra (deathmatch-6d3a9327,
deadworks-d48155c8).

## `extra-plugins` BuildKit context (the key hook)

Out-of-tree plugin sources are injected via a named BuildKit build context
(deadworks-328372c6, deadworks-3beeff54, deadworks-aabd306f,
deathmatch-310fc296):

- `FROM busybox AS extra-plugins` — default stage is empty, so the
  `--mount=from=extra-plugins` copy is a no-op unless overridden.
- Override: `docker build --build-context extra-plugins=../my-plugins .`
  OR in `docker-compose.yaml`:
  ```
  additional_contexts:
    extra-plugins: ../my-deadworks-plugins
  ```
- Dockerfile consumes it via
  `RUN --mount=from=extra-plugins,target=/extra  cp -r /extra/. managed/plugins/ 2>/dev/null || true`
  (or similar pattern into `examples/plugins/` after the upstream
  split in `189cf2a`).
- Plugin discovery then walks `extra-plugins/*/*.csproj` and publishes each.
- Plugin paths work across the `managed/plugins/` → `examples/plugins/`
  move because `examples/Directory.Build.props` is a one-line import of
  `..\managed\Directory.Build.props`.

## `Directory.Build.targets` for assembly resolution

Plugin csprojs reference `DeadworksManaged.Api` as a bare `<Reference>`
(not `<ProjectReference>`) inside Docker builds. The Dockerfile auto-generates
`extra-plugins/Directory.Build.targets` that injects:

```xml
<AssemblySearchPaths>$(AssemblySearchPaths);/artifacts/managed</AssemblySearchPaths>
```

so the bare Reference resolves against the already-published managed DLLs
(deadworks-3beeff54, deathmatch-6d3a9327).

Why `.targets` not `.props` (commit `ccdd9c4`): **props evaluate too
early**, before `AssemblySearchPaths` is populated by MSBuild's default
logic. Targets run after project references are known (deadworks-3beeff54,
deadworks-530007be).

## Dual-mode csproj pattern

Each plugin csproj must support three mutually-exclusive reference modes
that coexist in the same file (server-plugins-90226db4,
deathmatch-3636296d, deathmatch-6d3a9327):

1. **`DeadlockDir` set** → `HintPath` against a downloaded Deadlock release
   ZIP. Used by the `build-plugins.yml` standalone CI validation job.
2. **Sibling `../../deadworks/managed/DeadworksManaged.Api` exists** →
   `ProjectReference`. Used by local dev with sibling deadworks checkout.
3. **Neither** → bare `<Reference Include="DeadworksManaged.Api" />`
   fallback, resolved by deadworks' injected `Directory.Build.targets`
   inside the Docker build.

Commit arc for the current setup in this repo:

- `da1edf3` initial Docker/local-dev ProjectReference split (Docker path
  was wrong initially)
- `59b6e96` restored `DeadlockDir` HintPath branch for `build-plugins` CI
- `2648aa6` added bare Reference fallback for deadworks Docker build

Errors when misconfigured: `CS0246: The type or namespace name
'DeadworksManaged' could not be found` / `GameEventHandler could not be
found` despite the csproj compiling locally.

## `Google.Protobuf` in plugin csprojs

**Gotcha that bites plugins using `NetMessages.Send<T>`**
(deathmatch-3636296d, deathmatch-fa5d1d7e, server-plugins-90226db4):

- `NetMessages.Send<T>` constrains `T : IMessage<T>` from
  `Google.Protobuf`.
- Local builds transitively resolve `Google.Protobuf` through the sibling
  `DeadworksManaged.Api` ProjectReference.
- Docker CI links against the already-published `DeadworksManaged.Api.dll`
  and does NOT see the transitive package → CS0311 +
  cascade of misleading CS0246 for `PluginConfigAttribute`,
  `GameEventHandler`, `ChatCommand`, `Heroes`, `CCitadelPlayerPawn`.
- **Fix**: add direct `PackageReference` to the plugin csproj with the
  same "host provides runtime" attributes:
  ```xml
  <PackageReference Include="Google.Protobuf" Version="3.29.3"
                    Private="false" ExcludeAssets="runtime" />
  ```
- Alternative fix considered but not taken: publish `Google.Protobuf.dll`
  alongside `DeadworksManaged.Api.dll` into `/artifacts/managed`.

## `HintPath` builds with a stub game dir

For standalone CI builds or local dev without Deadlock installed, create
a synthetic stub (server-plugins-65d13a2e):

```
mkdir -p /tmp/dm-stub/managed
cp <path>/DeadworksManaged.Api.dll /tmp/dm-stub/managed/
cp <path>/Google.Protobuf.dll      /tmp/dm-stub/managed/
dotnet build -p:DeadlockBin=/tmp/dm-stub
```

`Google.Protobuf.dll` is NOT shipped alongside
`DeadworksManaged.Api.dll` in the latter's build output — must be copied
from a separate NuGet cache (`~/.nuget/packages/google.protobuf/3.29.3/lib/net5.0/Google.Protobuf.dll`).

## `DeployToGame` post-build target

`DeadworksManaged.Api.csproj` has a post-build `DeployToGame` target
(`AfterTargets="Build"`) that copies `DeadworksManaged.Api.dll` + `.pdb`
to `$(DeadlockManagedDir)`. On CI the env var is empty, producing warning
`MSB3023: No destination specified for Copy` (deadworks-d416f1ea,
server-plugins-90226db4).

**Fix**: guard with `Condition="'$(DeadlockManagedDir)' != ''"` on the
`<Copy>` element. Applies to plugin csprojs too — local dev auto-deploys
to `$(DeadlockBin)\managed\plugins` (server-plugins-65d13a2e).

## `DeadlockDir` / `DeadlockManagedDir` / `DeadlockBin`

Path hierarchy for local dev:

- `DEADLOCK_GAME_DIR` env var (or `DeadlockDir` MSBuild property)
- `$(DeadlockBin)` = `$(DeadlockDir)\game\bin\win64`
- `$(DeadlockManagedDir)` = `$(DeadlockDir)\managed` (set by
  `deadworks/managed/Directory.Build.props` when `DeadlockDir` non-empty)
- `local.props` holds `ProtobufIncludeDir`, `ProtobufLibDir`, `NetHostDir`,
  optional `DeadlockDir`. Template in `local.props.example`
  (deadworks-52a01b09).

`Properties/launchSettings.json` in the deathmatch plugin launches the
Windows deadworks.exe directly from
`C:\Program Files (x86)\Steam\steamapps\common\Deadlock\game\bin\win64\deadworks.exe`
— implies local plugin debugging attaches to a real Steam-installed
Deadlock client, not a containerised server (deathmatch-6d3a9327).

## `gamemodes.json` — per-mode plugin selection

Lives at the repo root of `deadlock-server-plugins`
(server-plugins-0b7a496e):

```json
{
  "normal": ["StatusPoker"],
  "lock-timer": ["StatusPoker", "LockTimer"]
}
```

- Keys are gamemode names.
- Values are plugin folder names (NOT csproj `AssemblyName`).
- CI staging copies the selected directories into `extra-plugins/`
  (server-plugins-90226db4). Renaming `DeathmatchPlugin/` → `Deathmatch/`
  required updating `gamemodes.json` — otherwise the gamemode image shipped
  with no plugin.
- `StatusPoker` is in both profiles; `LockTimer` is exclusive to
  `lock-timer`; `DeathmatchPlugin` is in the tree but not wired into
  `gamemodes.json` as of commit `941501d` (server-plugins-0b7a496e).

## CI workflows

**Per-mode image publishing** (`.github/workflows/docker.yml`):

- One image per gamemode: `ghcr.io/deadlock-api/deadlock-server-plugins/<mode>:latest`.
- Tags by dispatch/branch/PR. Commit `c77843c` added "readable refs".

**Deathmatch satellite repo** (`deadlock-deathmatch/.github/workflows/docker.yml`;
deathmatch-310fc296, deathmatch-c51730eb):

- Checks out `raimannma/deadworks` into path `deadworks/` (with
  `submodules: recursive`).
- Checks out own repo into path `deathmatch/`.
- `docker/build-push-action@v7` with:
  - `context: deadworks`
  - `file: deadworks/docker/Dockerfile`
  - `build-contexts: extra-plugins=deathmatch/plugins`
- Publishes to `ghcr.io/raimannma/deadlock-deathmatch/game-server:latest`.
- Commit `0dd89cd` added `workflow_dispatch:` and relaxed gates to
  `github.event_name != 'pull_request'` so manual dispatch publishes.

**GHA tagging rules** (`docker/metadata-action@v6`) used in deadworks
(deadworks-543d455d):

- `type=raw,value=latest,enable={{is_default_branch}}`
- `type=semver,pattern={{version}}` + `{{major}}.{{minor}}`
- `type=sha`

Push-to-main produces `latest` + `sha-<shortsha>`. Tag `v1.2.3` produces
`1.2.3`, `1.2`, `sha-...`, and `latest` if tag cut from main.

**Cache**: `type=gha,mode=max` used across all Docker workflows.

**Build-only on PR pattern** (deadworks-543d455d): `push:` field set to
`${{ github.event_name == 'push' }}`; GHCR login gated by
`if: github.event_name == 'push'`.

**Action major versions** (as of Apr 2026; deadworks-aabd306f,
deathmatch-6d3a9327): `actions/checkout@v6`, `actions/setup-dotnet@v5`,
`setup-msbuild@v3`, `cache@v5`, `upload-artifact@v7`, `download-artifact@v8`,
`docker/metadata-action@v6`, `setup-qemu@v4`, `setup-buildx@v4`,
`login-action@v4`, `build-push-action@v7`, `action-gh-release@v2`,
`peter-evans/create-pull-request@v8`, `setup-python@v6`.

**Auto-update workflows** in deadworks (deadworks-1bb13986, deadworks-d75e1c40):

- `.github/workflows/update-protos.yml` — daily 06:00 UTC cron,
  `workflow_dispatch`, opens PR on `chore/update-protos`.
- `.github/workflows/update-game-exported.yml` — same pattern for
  `.gameevents` files.
- `.github/workflows/update-sourcesdk.yml` — bumps the sourcesdk submodule.

All three share identical scheduling (06:00 UTC daily) and
`peter-evans/create-pull-request@v8` mechanics — consistency is
intentional.

## Persistent named volumes

In `docker-compose.yaml` (deadworks-ddfface7, server-plugins-0b7a496e):

- `proton:/opt/proton` — GE-Proton tarball cached across runs.
- `gamedata:/home/steam/server` — Deadlock server files from SteamCMD.
  (Per-instance in the plugins repo: `gamedata-<mode>`.)
- `compatdata:/home/steam/.steam/steam/steamapps/compatdata` — Wine prefix
  + .NET install. (Per-instance: `compatdata-<mode>`.)
- `dotnet-cache:/opt/dotnet-cache` — downloaded .NET runtime zip.

All four survive restarts so only the first boot pays the download cost.

Bind mount: `/etc/machine-id:/etc/machine-id:ro` — Steam/Proton needs a
stable machine-id for its compat sandbox (deadworks-328372c6).

## Per-instance volume strategy

The plugins repo uses a YAML anchor pattern (commit `1915da7`,
server-plugins-0b7a496e):

- Shared across services: `proton`, `gamefiles`, `dotnet-cache` volumes.
- Per-instance: `gamedata-<mode>`, `compatdata-<mode>` volumes (so each
  gamemode has its own Wine prefix and managed layer).

Each service ships on its own UDP+TCP port (`normal=27015`,
`lock-timer=27016`) and expects TWO env vars that must match:
`SERVER_PORT` and `DEADWORKS_ENV_PORT`.

## Memory sizing

Each Source 2 server reserves ~2 GB of virtual address space up front
(RSS ~3.5 GiB). Three gamemode containers on one host → ~10.5 GiB working
set (server-plugins-90226db4).

On a 7.8 GiB host with zero swap and `vm.overcommit_memory=0` (heuristic),
a running server's ~3.47 GiB makes a second container's ~2 GB VA
reservation fail at startup with
`err:virtual:allocate_virtual_memory out of memory`. Mitigations in order:
one gamemode per host, add swap + `vm.overcommit_memory=1` (fits 2),
more RAM (fits all 3).

## Build incantations

Single plugin build (from satellite repo root; deathmatch-493a9384):

```
dotnet build plugins/DeathmatchPlugin/DeathmatchPlugin.csproj -c Debug
```

Transitively builds sibling deadworks managed projects:
`DeadworksManaged.Generators` (netstandard2.0) → `DeadworksManaged.Api`
(net10.0) → plugin DLL into `plugins/<Name>/bin/Debug/net10.0/`.

Persistent warning `MSB3023` at `DeadworksManaged.Api.csproj(38,5)` — a
`<Copy>` task missing `DestinationFiles`/`DestinationFolder`. Non-fatal.
Not fixable from the plugins repo — lives upstream in deadworks.
