---
date: 2026-04-21
task: session extract — deadworks 328372c6
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/328372c6-ce59-4cb0-bd90-2081315ed61e.jsonl]
---

## Deadlock game systems

- Deadlock Steam AppID is `1422450` (used for SteamCMD `app_update` and `steam_appid.txt`) — `docker/entrypoint.sh:7`.
- Server launch defaults encode a stripped-down flavor: `-dedicated -console -dev -insecure -allow_no_lobby_connect` plus replay/TV disables `+tv_citadel_auto_record 0 +spec_replay_enable 0 +tv_enable 0 +citadel_upload_replay_enabled 0` — `docker/entrypoint.sh:182-183`.
- Default map is `dl_midtown`; default server port is `27015` (note README still documents `localhost:27067` for local-dev connect — inconsistency between Windows-build flow and docker flow).
- Config file `deadworks_mem.jsonc` lives in `game/citadel/cfg/` and is copied separately from managed/native artifacts — `docker/entrypoint.sh:163`, `Dockerfile:157`.

## Deadworks runtime

- Runtime layout deployed into `<install>/game/bin/win64/`: `deadworks.exe`, `managed/` (DeadworksManaged + dependencies), and `managed/plugins/` for user plugins — `docker/entrypoint.sh:155-159`, `Dockerfile:156-158`.
- `DOTNET_ROOT` is forced to `C:\Program Files\dotnet` inside Proton so hostfxr is discoverable — `docker/entrypoint.sh:212`. Runtime is installed by unzipping the NuGet Windows runtime into the Wine prefix's `drive_c/Program Files/dotnet` (not the container's Linux dotnet) — `entrypoint.sh:118-133`.
- `hostfxr.dll` presence under `host/fxr/*/` is the phase-4 success signal — `entrypoint.sh:136`. This implies Deadworks native host uses nethost → hostfxr to boot the managed layer.
- `steamclient64.dll` / `steamclient.dll` must be triple-placed: pfx `Program Files (x86)/Steam`, `win64/` next to deadworks.exe, and `system32` — `entrypoint.sh:105-113`. Source is the `Steamworks SDK Redist` app (AppID 1007) fetched anonymously.
- `/etc/machine-id` is bind-mounted read-only into the container — `docker-compose.yaml:14`. Steam/Proton need a stable machine-id for account/auth state; without this the prefix can trigger fresh-device prompts.

## Plugin build & deployment

- Managed build has a dedicated plugin auto-discovery stage: `find managed/plugins -name '*.csproj' | xargs dotnet publish -o /artifacts/managed/plugins --no-self-contained` — `Dockerfile:104-106`. Each plugin is a separate csproj; no central registration, purely filesystem-discovered.
- External plugins are injected via a BuildKit `additional_contexts` hook named `extra-plugins`. Default is an empty `busybox`-based stage so the `--mount=from=extra-plugins` copy is a no-op without override — `Dockerfile:86-98`. Override with `docker build --build-context extra-plugins=../my-plugins .`.
- Plugin csprojs must reference `DeadworksManaged.Api` (stated in Dockerfile comment `Dockerfile:96`).
- Published flag `--no-self-contained` is deliberate — plugins share the single managed runtime already published alongside DeadworksManaged.
- Native cross-compile from Linux: clang-20 with `--driver-mode=cl` wrapper (argv[0] detection unreliable in Docker, so explicit wrapper script) — `Dockerfile:18-23`. lld-link / llvm-lib / llvm-rc wrappers round out the MSVC-compatible toolchain.
- Windows SDK + MSVC CRT sourced via `xwin 0.6.5 splat` into `/xwin` — `Dockerfile:26-33`. nethost pulled from NuGet `Microsoft.NETCore.App.Host.win-x64` package (headers + libnethost.lib) — `Dockerfile:36-45`.
- Protobuf 3.21.8 is built from source at image build time with clang-cl using a custom toolchain file `docker/toolchain-clangcl.cmake`, static MT runtime (`CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded`, `protobuf_MSVC_STATIC_RUNTIME=ON`) — `Dockerfile:57-66`. Tarball download preferred over git clone "more reliable than git clone in BuildKit" — `Dockerfile:48`.
- CI publishes to `ghcr.io/${github.repository}` with tags `latest` (main only), semver `{version}` and `{major}.{minor}` on `v*` tags, plus `sha` — `.github/workflows/docker.yml:22-29`. Uses GHA cache `type=gha,mode=max`.
- `.env` required vars: `STEAM_LOGIN`, `STEAM_PASSWORD` (enforced by `:?` bash parameter expansion — `entrypoint.sh:8-9`). Tunables: `SERVER_PORT`, `SERVER_MAP`, `SERVER_PASSWORD`, `RCON_PASSWORD`, `PROTON_VERSION` (default `GE-Proton10-33`), `DOTNET_VERSION` (default `10.0.0`), `DEADWORKS_ARGS` (appended to default args) — `entrypoint.sh:10-16`.
- Named volumes cache expensive first-run downloads: `proton` (GE-Proton tarball), `gamedata` (SteamCMD server files), `compatdata` (Wine prefix + .NET install), `dotnet-cache` (.NET runtime zip) — `docker-compose.yaml:21-25`. Markers `.proton_marker` and `.dotnet_${version}_marker` gate re-install.
- Proton source: `github.com/GloriousEggroll/proton-ge-custom` releases, untarred into `/opt/proton` with `--strip-components=1` — `entrypoint.sh:31-32`.
- Xvfb `:99` at 640x480x24 is started before wineboot and the server run (Proton/Wine need a display even for dedicated server) — `entrypoint.sh:82`, `210`.
