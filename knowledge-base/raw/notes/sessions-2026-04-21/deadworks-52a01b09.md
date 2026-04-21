---
date: 2026-04-21
task: session extract — deadworks 52a01b09
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/52a01b09-21aa-4cee-b8bd-30c7713de592.jsonl]
---

Session goal: design Docker deployment for the deadworks C++/C# framework (separate project at `/home/manuel/deadlock/deadworks`, distinct from `deadlock-server-plugins`).

## Source 2 engine

- `engine2.dll` exports `Source2Main(hInst, hPrev, cmdLine, nShowCmd, baseDir, game)` — the canonical dedicated-server entry point; deadworks calls it directly after installing hooks (`startup.cpp:9`, `startup.cpp:27`, `startup.cpp:76`).
- Default Deadlock server CLI baked into deadworks (`startup.cpp:63`): `-dedicated -console -dev -insecure -allow_no_lobby_connect +tv_citadel_auto_record 0 +spec_replay_enable 0 +tv_enable 0 +citadel_upload_replay_enabled 0 +hostport 27015 +map dl_midtown`.
- Baseline game dir is passed as `"citadel"` (sixth arg to `Source2Main`); exe resolves paths relative to `argv[0]`, not cwd (`startup.cpp:15-17`).
- Required signatures validated at boot (`startup.cpp:40-52`): `UTIL_Remove`, `CMaterialSystem2AppSystemDict::OnAppSystemLoaded`, `CServerSideClientBase::FilterMessage`, `GetVDataInstanceByName`, `CModifierProperty::AddModifier`.
- Bootstrap hook is `OnAppSystemLoaded` — installed inline via safetyhook before handoff, fires once engine AppSystems are up so the .NET runtime can be spun up at the right moment (`startup.cpp:61`).
- deadworks also preloads `../../citadel/bin/win64/server.dll` relative to the exe before calling `Source2Main` (`startup.cpp:19`) — ensures signature scans hit server.dll even if engine hasn't loaded it yet.

## Deadlock game systems

- Steam AppId `1422450` (Deadlock). Standard install layout `game/bin/win64/` for exes and DLLs, `game/citadel/cfg/` for configs, `citadel/bin/win64/server.dll` for server module.
- Plugin framework listed: AutoRestartPlugin, ChatRelayPlugin, DeathmatchPlugin, DumperPlugin, ExampleTimerPlugin, ItemRotationPlugin, ItemTestPlugin, RollTheDicePlugin, ScourgePlugin, SetModelPlugin, TagPlugin.
- Hook surface in `Core/Hooks/`: `Source2Server`, `Source2GameClients`, `CBaseEntity` (TakeDamageOld), `CCitadelPlayerPawn` (ModifyCurrency), `CCitadelPlayerController`, `GameEvents`, `NetworkServerService`, `PostEventAbstract`, `ProcessUsercmds`, `AbilityThink`, `AddModifier`, `TraceShape`, `EntityIO`, `BuildGameSessionManifest` (precaching).

## Deadworks runtime

- `deadworks.exe` is a replacement entry point, not an injected DLL — it loads `engine2.dll`, patches hooks, then calls `Source2Main` itself. No separate injector process needed (contrast with the Rust server-plugins pattern using `plugin_loader.dll` + `injector.exe`).
- Signatures are loaded from `citadel/cfg/deadworks_mem.jsonc` (`startup.cpp:34`) — JSONC sig DB resolved relative to exe dir. Missing file is fatal.
- `MemoryDataLoader` is a singleton (`deadworks::MemoryDataLoader::Get()`); signatures stored keyed by fully-qualified `Class::Method` names and accessed via `GetOffset(name) -> optional<uintptr_t>`.
- Hook mechanism: `safetyhook::create_inline(addr, detour)` — x86-64 inline trampoline hooks. `Zydis` used for disassembly-safe instruction-boundary detection. Both in `vendor/`.
- .NET hosting via `Hosting/DotNetHost.cpp` → `get_hostfxr_path()` from `nethost.lib`; hostfxr searches `DOTNET_ROOT` env var, then `C:\Program Files\dotnet\`, then registry. Runtime is .NET 10 (`DeadworksManaged.csproj`, README requires 10.0.5+).
- Managed layer layout shipped: `game/bin/win64/managed/DeadworksManaged.dll` + `DeadworksManaged.Api.dll` + `*.runtimeconfig.json` + `plugins/*.dll`. Plugins discovered via reflection (attributes `[Plugin]`, `[Event]`, `[NetMessage]`).
- Managed bridge: `Core/ManagedCallbacks.hpp` (C# signatures called from native), `Core/NativeCallbacks.hpp` (native funcs exposed to managed). `g_Deadworks.m_managed.onTakeDamageOld(...)` returning true blocks damage; false falls through to original via `hook.call(...)`.

## Plugin build & deployment

- Native toolchain hard-requires MSVC: `PlatformToolset v145` (VS 2026), x64 Release. `tier0.lib` is in MSVC format — MinGW cross-compile not feasible.
- Static deps: protobuf 3.21.8 built with `-Dprotobuf_MSVC_STATIC_RUNTIME=ON`, `libnethost.lib` from `Microsoft.NETCore.App.Host.win-x64/10.0.5/runtimes/win-x64/native`.
- Build config externalized to `local.props` (template in `local.props.example`): `ProtobufIncludeDir`, `ProtobufLibDir`, `NetHostDir`, optional `DeadlockDir` for auto-deploy post-build.
- CI artifact layout (.github/workflows/build.yml): `game/bin/win64/deadworks.exe` + `game/bin/win64/managed/...` + `game/citadel/cfg/deadworks_mem.jsonc` zipped as a release.
- Docker strategy chosen: fetch pre-built release zip rather than compile in container (no Windows container on Linux host). `cm2network/steamcmd:root` + GE-Proton10-33, same base as existing `deadlock-custom-server`/`deadlock-server-plugins` Docker setups.
- Critical Proton-specific step: .NET 10 Windows x64 runtime must be extracted into `{pfx}/drive_c/Program Files/dotnet/` AND `DOTNET_ROOT=C:\Program Files\dotnet` env passed through Proton so `hostfxr` can resolve. Runtime zip from `dotnetcli.azureedge.net/dotnet/Runtime/{ver}/dotnet-runtime-{ver}-win-x64.zip`.
- Launch command runs `deadworks.exe` via `proton run` (not `deadlock.exe`) — same Steam compat env vars (`STEAM_COMPAT_DATA_PATH`, `SteamAppId=1422450`) but new `DOTNET_ROOT`.
- Plan file written to `/home/manuel/.claude/plans/fluffy-rolling-codd.md`; session ended at ExitPlanMode (no code actually written).
