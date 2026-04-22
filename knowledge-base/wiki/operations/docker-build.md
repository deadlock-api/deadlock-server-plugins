---
title: Docker build — cross-compile + publish
type: operation
sources:
  - knowledge-base/raw/notes/2026-04-22-deadworks-native-layout.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-328372c6.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-3beeff54.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-4972c10e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-543d455d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-5d3198bf.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-a54dc08d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-aabd306f.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-bc59e6cf.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-ec2918a5.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-ecd0b4a8.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-0b7a496e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-90226db4.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-310fc296.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-6d3a9327.md
related:
  - "[[plugin-build-pipeline]]"
  - "[[proton-runtime]]"
  - "[[deadworks-runtime]]"
created: 2026-04-21
updated: 2026-04-21
confidence: high
---

# Docker build

Runbook for the three-stage Docker pipeline that produces the per-gamemode
server images.

## Stage summary

```
native-builder    ubuntu:24.04
  ├── LLVM 20 (clang-cl, lld-link shell wrappers)
  ├── xwin 0.6.5 splat → /xwin (MSVC SDK/CRT)
  ├── nethost NuGet 10.0.0 → /nethost (headers + libnethost.lib)
  ├── protobuf 3.21.8 build from tarball → libprotobuf.lib
  └── build-native.sh: clang-cl deadworks/src/**/*.cpp → deadworks.exe
         ↓ artifact
managed-builder   mcr.microsoft.com/dotnet/sdk:10.0
  ├── dotnet publish managed/DeadworksManaged.csproj
  │     -c Release -o /artifacts/managed --no-self-contained
  └── find examples/plugins -name '*.csproj' ... | xargs dotnet publish
         -c Release -o /artifacts/managed/plugins --no-self-contained
         ↓ artifacts
runtime           cm2network/steamcmd:root
  ├── dpkg --add-architecture i386 + Wine/Proton deps
  └── COPY deadworks.exe + deadworks_mem.jsonc + /artifacts/managed/*
        → /opt/deadworks/
```

Sources: deadworks-a54dc08d, deadworks-328372c6, deadworks-52a01b09,
deadworks-aabd306f.

## Stage 1 — `native-builder`

`ubuntu:24.04` + LLVM 20 apt repo for `clang-20` / `lld-20`.

**Wrapper scripts** in `/usr/local/bin/`:

- `clang-cl` → `clang-20 --driver-mode=cl "$@"`
- `lld-link` → `lld-20 -flavor link "$@"`
- Also `llvm-lib`, `llvm-rc` wrappers.

Reason: argv[0]-based driver-mode detection is unreliable in Docker,
so explicit flags are forced (deadworks-328372c6, deadworks-4972c10e).

**xwin** (0.6.5) splats MSVC headers/libs into `/xwin`
(`/xwin/crt/include`, `/xwin/sdk/include/{ucrt,um,shared}`).

**nethost** pulled from NuGet `Microsoft.NETCore.App.Host.win-x64`
(10.0.0) to `/nethost` for `nethost.h`, `coreclr_delegates.h`,
`hostfxr.h`, `libnethost.lib` (deadworks-328372c6).

**Protobuf 3.21.8** built from source tarball via
`docker/toolchain-clangcl.cmake`:

- `CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded`
- `CMP0091=NEW`
- `protobuf_MSVC_STATIC_RUNTIME=ON`
- `CMAKE_TRY_COMPILE_CONFIGURATION=Release` (required — CMake compiler
  test otherwise links `msvcrtd.lib`).
- Tarball preferred over `git clone` — "more reliable than git clone
  in BuildKit" (deadworks-328372c6).

## `build-native.sh` quirks

(deadworks-bc59e6cf, deadworks-4972c10e, deadworks-3beeff54,
deadworks-aabd306f)

- Hand-maintained bash array of source files. Missing files fail only
  at **link time** (`undefined symbol`), not compile time.
- Churn in `src/Core/Hooks/` (`SendNetMessage.cpp` removed in upstream
  `38a35cc`; `CheckTransmit.cpp` and `A2SPatch.cpp` added) requires
  matching edits to `build-native.sh` in the Docker fork.
- **C++23**: `clang-cl /std:c++23` does NOT set `__cplusplus` correctly;
  must use `-Xclang -std=c++23` to reach frontend.
- **`__restrict` hack**: mismatch between decl/def in SDK `bitbuf.h` is
  a clang-cl hard error. Patched via `/D__restrict=` (empty macro) in
  `SDK_FLAGS`.
- Protobuf compiled with `/TC /MT /O2 /DNDEBUG -fuse-ld=lld` and
  `$TARGET` var from xwin. Zydis as C (`/TC`).
- Project includes: `sourcesdk/public/{tier0,tier1,appframework,mathlib,entity2,engine}`,
  `sourcesdk/game/shared`, `sourcesdk/common`, `/protobuf-src/src`,
  `/nethost`.

Final link:

```
lld-link /SUBSYSTEM:CONSOLE
  sourcesdk/lib/win64/tier0.lib
  libprotobuf.lib
  libnethost.lib
  advapi32.lib ole32.lib
```

## Stage 2 — `managed-builder`

- `mcr.microsoft.com/dotnet/sdk:10.0`.
- Publishes `DeadworksManaged.csproj` and every plugin csproj with
  `--no-self-contained` (plugins share the single managed runtime).
- `-r` on `xargs` handles empty plugins dirs gracefully.
- Skips `.Tests.csproj` automatically.

## `extra-plugins` BuildKit context

```dockerfile
FROM busybox AS extra-plugins
# Default: empty stage → no-op mount
```

Overridden by `docker build --build-context extra-plugins=<path> .`
OR `docker-compose.yaml additional_contexts: extra-plugins: <path>`
(deadworks-328372c6, deathmatch-310fc296).

Used in the `managed-builder` stage:

```dockerfile
RUN --mount=from=extra-plugins,target=/extra \
    cp -r /extra/. examples/plugins/ 2>/dev/null || true
```

(Path was `managed/plugins/` before upstream `189cf2a` split plugins
to `examples/plugins/`; deadworks-4972c10e.)

## Auto-generated `extra-plugins/Directory.Build.targets`

The managed-builder stage writes this `.targets` file before the plugin
publish step (deadworks-3beeff54, deathmatch-6d3a9327):

```xml
<Project>
  <PropertyGroup>
    <AssemblySearchPaths>$(AssemblySearchPaths);/artifacts/managed</AssemblySearchPaths>
  </PropertyGroup>
</Project>
```

Lets plugin csprojs reference `DeadworksManaged.Api` as a bare
`<Reference>` (not `<ProjectReference>`) and resolve to the pre-published
`/artifacts/managed/DeadworksManaged.Api.dll`.

**`.targets` not `.props`** (commit `ccdd9c4`): props evaluate too early,
before MSBuild populates `AssemblySearchPaths`. `.targets` runs after
project references are known.

MSBuild auto-discovers `deadworks/examples/Directory.Build.props` by
walking up from `/build/examples/plugins/<P>/` during the Docker build;
that file imports `deadworks/managed/Directory.Build.props` and sets
`$(DeadlockManagedDir)`. A `Directory.Build.props` at the
**server-plugins repo root** is NEVER on that walk — so it has zero
effect on CI/Docker (server-plugins-90226db4).

## Stage 3 — `runtime`

`cm2network/steamcmd:root` + i386 multiarch + Wine/Proton deps. See
[[proton-runtime]] for full runtime details.

Runtime layer copies:

- `deadworks.exe` → `/opt/deadworks/game/bin/win64/deadworks.exe`
- `deadworks_mem.jsonc` → `/opt/deadworks/game/citadel/cfg/deadworks_mem.jsonc`
- `/artifacts/managed/*` → `/opt/deadworks/game/bin/win64/managed/`
- `/artifacts/managed/plugins/*` → `/opt/deadworks/game/bin/win64/managed/plugins/`

The entrypoint then copies from `/opt/deadworks` into the gamedata
volume on each container start (deadworks-ddfface7) so the volume can
be shared across instances (commit `46be6d3`; upstream commits
`177d4d4` "keep base Docker image plugin-free by default" and
`188b5d7` "exclude managed/plugins from docker build context";
deadworks-530007be).

Deploy wipes `managed/` first to avoid stale DLLs (commit `7650f00`).

## `docker-compose.yaml` topology

### Deadworks repo

(deadworks-d48155c8, deadworks-ddfface7, deadworks-aabd306f)

```yaml
services:
  deadworks:
    build:
      context: .
      dockerfile: docker/Dockerfile
      # Optional:
      additional_contexts:
        extra-plugins: ../my-deadworks-plugins
    env_file: .env
    ports:
      - "${SERVER_PORT:-27015}:${SERVER_PORT:-27015}/udp"
      - "${SERVER_PORT:-27015}:${SERVER_PORT:-27015}/tcp"
    volumes:
      - proton:/opt/proton
      - gamedata:/home/steam/server
      - compatdata:/home/steam/.steam/steam/steamapps/compatdata
      - dotnet-cache:/opt/dotnet-cache
      - /etc/machine-id:/etc/machine-id:ro

  # HLTV profile (deadworks-d48155c8)
  hltv-relay:
    profiles: [tv]
    image: ghcr.io/deadlock-api/hltv-relay:latest
    ports: ["8080:3000"]
    environment:
      HLTV_RELAY_AUTH_MODE: key
      HLTV_RELAY_AUTH_KEY: ${TV_BROADCAST_AUTH}
      HLTV_RELAY_STORAGE: redis
    depends_on: [redis]
  redis:
    profiles: [tv]
    image: redis:8-alpine
```

HLTV services gated by the `tv` profile — start with
`docker compose --profile tv up`.

### Plugins repo (this repo)

YAML anchor + per-instance volumes (commit `1915da7`;
server-plugins-0b7a496e):

```yaml
x-base: &base
  volumes:
    - proton:/opt/proton
    - gamefiles:/home/steam/server
    - dotnet-cache:/opt/dotnet-cache
services:
  normal:
    <<: *base
    ports: ["27015:27015/udp", "27015:27015/tcp"]
    environment:
      SERVER_PORT: 27015
      DEADWORKS_ENV_PORT: 27015
    volumes:
      - gamedata-normal:/.../gamedata
      - compatdata-normal:/.../compatdata
  lock-timer:
    <<: *base
    ports: ["27016:27016/udp", "27016:27016/tcp"]
    environment:
      SERVER_PORT: 27016
      DEADWORKS_ENV_PORT: 27016
    volumes:
      - gamedata-lock-timer:/.../gamedata
      - compatdata-lock-timer:/.../compatdata
```

- Per-instance: `gamedata-<mode>`, `compatdata-<mode>`.
- Shared: `proton`, `gamefiles`, `dotnet-cache` (avoid duplicating
  Deadlock install + Proton runtime).

## Healthcheck

`start_period: 120s` with 60s interval — Source 2 + Proton cold boot is
slow (custom-server-543ec808). Dependent services must
`depends_on: { condition: service_healthy }` rather than start-order
alone.

## Plugin CI workflow (satellite repo pattern)

For `deadlock-deathmatch` (deathmatch-310fc296, deathmatch-c51730eb):

```yaml
- uses: actions/checkout@v6
  with: { repository: raimannma/deadworks, submodules: recursive, path: deadworks }
- uses: actions/checkout@v6
  with: { path: deathmatch }
- uses: docker/build-push-action@v7
  with:
    context: deadworks
    file: deadworks/docker/Dockerfile
    build-contexts: extra-plugins=deathmatch/plugins
    tags: ghcr.io/${{ github.repository }}/game-server:latest
    cache-from: type=gha
    cache-to: type=gha,mode=max
```

**Upstream fork caveat**: `Deadworks-net/deadworks` does NOT contain a
`docker/` directory — only the fork has. CI pointed at upstream fails
with `failed to build: resolve : lstat deadworks/docker: no such file
or directory`. Workflows must checkout `raimannma/deadworks` until
upstream absorbs Docker infra (deathmatch-6d3a9327, deadworks-d48155c8).

## CI tagging

`docker/metadata-action@v6` rules (deadworks-543d455d):

- `type=raw,value=latest,enable={{is_default_branch}}`
- `type=semver,pattern={{version}}`
- `type=semver,pattern={{major}}.{{minor}}`
- `type=sha`

Push to `main` → `latest` + `sha-<shortsha>`.
Tag `v1.2.3` cut from `main` → `1.2.3`, `1.2`, `sha-<...>`, and
`latest` (metadata-action's `is_default_branch` evaluates true for tags
cut from main).

## PR-mode build-only

Pattern from deadworks-543d455d:

```yaml
on:
  push: { branches: [main] }
  pull_request: { branches: [main] }
jobs:
  docker:
    steps:
      - uses: docker/login-action@v4
        if: github.event_name == 'push'
      - uses: docker/build-push-action@v7
        with:
          push: ${{ github.event_name == 'push' }}
```

PRs build-only (no login, no push); main/tag pushes deploy.

## `.dockerignore` gotcha

`.dockerignore` excluding `Cargo.toml` / `Cargo.lock` / `src/` broke
Rust Dockerfile stages in the `custom-server` repo. Minimal contents
for workspace-root context: `.git .idea target/ server/`. Also
`.env` typos like `,env` (comma instead of dot) are silently invalid
(custom-server-9a7f664c, deadworks-aabd306f).

Parallel risk here: never exclude `*.csproj`, `*.cs`, or anything under
`managed/` / `examples/` in `.dockerignore`.

## Trixie upgrade (WIP)

Session `custom-server-ecd0b4a8` was exploring Debian 13 (Trixie) upgrade
for the runtime base (currently `cm2network/steamcmd:root` with Debian
Bookworm). Task was interrupted; no edits landed.
