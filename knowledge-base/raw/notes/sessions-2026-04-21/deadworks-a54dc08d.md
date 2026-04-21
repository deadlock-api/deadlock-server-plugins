---
date: 2026-04-21
task: session extract — deadworks a54dc08d
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/a54dc08d-bbea-41f5-af74-27368ecedbb8.jsonl]
---

## Deadlock game systems

- Deadlock Steam AppID is `1422450`, hard-coded at `docker/entrypoint.sh:7` and also used as the compatdata path (`steamapps/compatdata/1422450/pfx`).
- Default map is `dl_midtown`; entrypoint passes it via `+map ${SERVER_MAP}` (`docker/entrypoint.sh:11,184`).

## Deadworks runtime

- `deadworks.exe` is tightly coupled to game offsets — the session warns pinning older Steam manifests may be incompatible with the current `config/deadworks_mem.jsonc` (offset/signature file shipped separately to `/opt/deadworks/game/citadel/cfg/`).
- Managed layer layout on runtime: `${WIN64_DIR}/managed/` for `DeadworksManaged` assemblies and `${WIN64_DIR}/managed/plugins/` for plugin DLLs (`docker/entrypoint.sh:158-159`).
- `DOTNET_ROOT` is exported as the Windows-style path `C:\Program Files\dotnet` inside Proton before `deadworks.exe` launches (`entrypoint.sh:212`) — the native host uses nethost/hostfxr to boot managed code.
- Default server args include `-dev -insecure -allow_no_lobby_connect` plus disabling replay/TV: `+tv_citadel_auto_record 0 +spec_replay_enable 0 +tv_enable 0 +citadel_upload_replay_enabled 0` (`entrypoint.sh:182-183`). Comment at :181 says these match `startup.cpp` defaults but are overridable.
- `DEADWORKS_ARGS` env var appends user-supplied extra args after the defaults (`entrypoint.sh:194-196`).

## Plugin build & deployment

- Three-stage Dockerfile (`docker/Dockerfile`):
  - Stage 1 `native-builder` (ubuntu:24.04): LLVM 20 apt repo → `clang-cl`/`lld-link` wrapper scripts (`:20-23`; comment notes argv[0] detection is unreliable in Docker so `--driver-mode=cl` is forced); xwin 0.6.5 splats MSVC SDK into `/xwin`; nethost headers+libnethost.lib pulled from NuGet `Microsoft.NETCore.App.Host.win-x64` 10.0.0 (`:37-45`); protobuf 3.21.8 built from tarball with `MultiThreaded` CRT + `protobuf_MSVC_STATIC_RUNTIME=ON` (`:49-66`).
  - Stage 2 `managed-builder` (dotnet/sdk:10.0): `dotnet publish managed/DeadworksManaged.csproj` then auto-discovers every `managed/plugins/**/*.csproj` via `find … -print0 | xargs dotnet publish` (`:104-106`).
  - Stage 3 `runtime` (`cm2network/steamcmd:root`) installs dpkg i386 arch + Wine deps, then copies `deadworks.exe`, `deadworks_mem.jsonc`, and managed artifacts in.
- Extra-plugins injection: named `extra-plugins` build context defaulting to empty busybox stage (`:86-87`); override with `docker build --build-context extra-plugins=../my-plugins .`; copied via `--mount=from=extra-plugins,target=/extra` into `managed/plugins/` before publish (`:97-98`).
- Runtime phases in `docker/entrypoint.sh`: (1) download `GE-Proton10-33` tarball and symlink into `compatibilitytools.d`; (2) SteamCMD `app_update 1422450` with 3-retry loop; (3) Xvfb `:99` + `wineboot --init` gated by `.proton_marker`; (4) unzip `dotnet-runtime-${DOTNET_VERSION}-win-x64.zip` into prefix `C:\Program Files\dotnet`, gated by `.dotnet_${DOTNET_VERSION}_marker` and cached under `/opt/dotnet-cache`; (5) copy deadworks artifacts over the vanilla install; (6) launch via `proton run ./deadworks.exe`.
- Steam client DLLs `steamclient64.dll`/`steamclient.dll` are copied from `Steamworks SDK Redist` into three locations: Wine prefix `Program Files (x86)/Steam`, `system32`, and the game's `win64` dir (`entrypoint.sh:105-113`).
- `steam_appid.txt` is written to both `win64/` and `game/citadel/` (`:166-167`).
- Game-version pinning not wired up: `app_update $APP_ID` has no `-beta` flag. Session's suggested approach is a `STEAM_BRANCH` env var producing `-beta <branch>`; alternative `download_depot <appid> <depotid> <manifestid>` pins exact build but loses `validate` and may break offset coupling with `deadworks_mem.jsonc`.
- Session running on branch `docker-build` (cwd `/home/manuel/deadlock/deadworks`) — indicates Docker pipeline is a WIP branch separate from mainline deadworks.
