---
date: 2026-04-21
task: session extract — deadworks bc59e6cf
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/bc59e6cf-d66e-49c5-8d78-a4a1d814f1c3.jsonl]
---

## Deadworks runtime

- Plugin discovery is directory-scan based: `PluginLoader.LoadAll()` scans `[DeadworksManaged.dll dir]/plugins/*.dll` (PluginLoader.cs:114-123). No manifest, no hardcoded names at runtime.
- Each plugin DLL loads in its own collectible `PluginLoadContext` (AssemblyLoadContext) loaded from a `MemoryStream` (PluginLoader.cs:209) so the file isn't locked; `.pdb` siblings loaded too (212-216). Enables hot-reload via `FileSystemWatcher` with 500ms debounce.
- Shared assemblies (NOT per-plugin): `DeadworksManaged.Api` (IDeadworksPlugin), `DeadworksManaged` (host), `Google.Protobuf`. Each plugin `.csproj` pins `DeadworksManaged.Api` as `<Private>false</Private>` + `<ExcludeAssets>runtime</ExcludeAssets>` so only the host's copy loads.
- Plugin type discovery: reflection `assembly.GetTypes().Where(t => typeof(IDeadworksPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)`, instantiated via `Activator.CreateInstance` (PluginLoader.cs:223-224).
- Enable/disable state persisted in `configs/plugins.jsonc` via `PluginStateManager`; `dw_plugin enable/disable <name>` is the console command. Disabled plugins skip reload-on-DLL-change (~line 329).
- Hook registration uses attributes like `[GameEventHandlerAttribute("event_name")]`, scanned on plugin load (PluginLoader.Events.cs:19-60); central `PluginRegistrationTracker` indexes all registrations per-plugin for clean unload.
- No `.sln` in `managed/` — the C++ `deadworks.vcxproj` PostBuildEvent orchestrates `dotnet publish` of `DeadworksManaged` + each plugin into `$(TargetDir)managed/plugins/`, driven by `$(DeadlockManagedDir)` from `managed/Directory.Build.props`.
- Known plugins in tree (Apr 2026): AutoRestart, ChatRelay, Deathmatch, Dumper, ExampleTimer, ItemRotation, ItemTest, RollTheDice, Scourge, SetModel, Tag. vcxproj explicitly publishes only 5 (Deathmatch, Scourge, RollTheDice, ExampleTimer, main); others publish via their own `DeployToGame` targets.

## Plugin build & deployment

- New docker cross-compile toolchain: Ubuntu 24.04 + LLVM 20 (`clang-cl` as `clang-20 --driver-mode=cl`, `lld-link` as `lld-20 -flavor link`) + `xwin` 0.6.5 splat for MSVC CRT/SDK headers/libs. See `docker/Dockerfile` stage 1, `docker/build-native.sh`, `docker/toolchain-clangcl.cmake`.
- **C++23 gotcha**: `clang-cl /std:c++23` does NOT set `__cplusplus` correctly, which breaks MSVC STL. Must pass `-Xclang -std=c++23` to reach the compiler frontend (build-native.sh CXX23_FLAGS).
- **Source SDK `__restrict` hack**: mismatch between decl/def in `bitbuf.h` is a hard error under clang-cl — patched via `/D__restrict=` (empty macro) in `SDK_FLAGS`. An earlier `/tmp/norestrict.h` pragma-push workaround was dead code and removed.
- Protobuf 3.21.8 cross-compiled via CMake toolchain file with `CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded` + `CMP0091=NEW` + `protobuf_MSVC_STATIC_RUNTIME=ON`. `CMAKE_TRY_COMPILE_CONFIGURATION=Release` and explicit Debug/Release `*_INIT` flags are required because CMake's compiler test otherwise tries to link `msvcrtd.lib` before `/MT` is applied.
- nethost (Microsoft.NETCore.App.Host.win-x64 NuGet) supplies `nethost.h`, `coreclr_delegates.h`, `hostfxr.h`, `libnethost.lib` — pulled at build time for linking `deadworks.exe`.
- Final link: `lld-link /SUBSYSTEM:CONSOLE` with `sourcesdk/lib/win64/tier0.lib`, `libprotobuf.lib`, `libnethost.lib`, `advapi32.lib`, `ole32.lib`.
- Runtime stage is `cm2network/steamcmd:root` + `dpkg --add-architecture i386` + Wine/Proton deps (libfreetype6/libvulkan1/libx11-6/libxcomposite1/libnss3/libgnutls30 etc., both x64 and i386 where needed).
- `docker/entrypoint.sh` 7-phase startup: Proton download (GE-Proton10-33 default), SteamCMD `+@sSteamCmdForcePlatformType windows +login ... +app_update 1422450` (Deadlock app id hardcoded), Wine prefix init under Xvfb :99, .NET 10 win-x64 runtime unzipped into `drive_c/Program Files/dotnet`, deadworks deploy, Proton-run with `DOTNET_ROOT='C:\\Program Files\\dotnet'`.
- Server-arg defaults in entrypoint.sh ~L182: `-dedicated -console -dev -insecure -allow_no_lobby_connect +tv_citadel_auto_record 0 +spec_replay_enable 0 +tv_enable 0 +citadel_upload_replay_enabled 0` — mirrors `startup.cpp` native defaults.
- Plugin auto-discovery at build: Dockerfile managed-builder stage now uses `find managed/plugins -name '*.csproj' -print0 | xargs -0 -r -I {} dotnet publish ...` instead of a hardcoded list. `-r` handles empty `managed/plugins/`.
- **External plugins**: `docker build --build-context extra-plugins=../my-plugins .` (or compose `additional_contexts: extra-plugins: ...`). Implemented via `RUN --mount=from=extra-plugins,target=/extra  cp -r /extra/. managed/plugins/ 2>/dev/null || true`. Default `extra-plugins` stage is a busybox `WORKDIR /extra` so the mount is empty no-op when not overridden. (Dockerfile L84-98.)
- `.dockerignore` had a typo `,env` (should be `.env`) — fixed. Dockerfile COPY order also reordered so volatile `deadworks/` copies LAST for better cache invalidation.
- New `.github/workflows/docker.yml` pushes to `ghcr.io/${{ github.repository }}` on main + v* tags, with `docker/metadata-action` tagging `latest`/`semver`/`sha`, `type=gha` build cache. Permissions `contents: read` + `packages: write`, login via `GITHUB_TOKEN`.
- Action versions refreshed across both workflows (as of Apr 2026): checkout@v6, setup-dotnet@v5, setup-msbuild@v3, cache@v5, upload-artifact@v7, download-artifact@v8, docker/metadata-action@v6, setup-qemu@v4, setup-buildx@v4, login-action@v4, build-push-action@v7, action-gh-release@v2.
- deadworks git remotes in use: `origin` = `Deadworks-net/deadworks`, `fork` = `raimannma/deadworks`. Fork's `main` had a divergent `.gitignore` commit; resolved via local rebase before `git push fork main`.
