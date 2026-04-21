---
title: Glossary
type: reference
created: 2026-04-21
updated: 2026-04-21
---

# Glossary

Terms, acronyms, and naming rules used across this wiki.

## Naming rules

- **Page filenames:** `kebab-case.md`.
- **Wikilinks:** `[[page-name]]` (no `.md`, no path).
- **Plugin names** in prose: the exact casing used in the repo (`DeathmatchPlugin`,
  `LockTimer`, `StatusPoker`) — these map directly to directory names.
- **Gamemodes** in prose: lowercase hyphenated (`normal`, `lock-timer`) — matches
  `gamemodes.json` keys and Docker image tags.

## Terms

### App ID 1422450
Deadlock's Steam App ID. Used for SteamCMD `app_update`, `steam_appid.txt`,
and Proton `compatdata/1422450/`. See [[deadlock-game]].

### AssemblyLoadContext (ALC)
.NET runtime construct for assembly isolation. Deadworks loads each plugin
DLL into its own collectible `PluginLoadContext` (an ALC subclass) so DLLs
can be unloaded and hot-reloaded. See [[deadworks-plugin-loader]].

### CCitadelGameRules
Server-side gamerules class. Discovered via entity with designer_name
`citadel_gamerules`; the `CCitadelGameRulesProxy`'s `m_pGameRules` at
offset `0x4A0` points at it. Holds match state, pause state, clock anchor,
flex-slot flag. See [[deadlock-game]].

### CEntityIdentity
Per-entity identity record inside `CGameEntitySystem`. 0x70 (112) bytes in
Deadlock. `+0x00 m_pInstance`, `+0x20 m_designerName*`. See [[source-2-engine]].

### citadel
Deadlock's internal mod directory name. Launch flag is `-game citadel`.
`server.dll` lives under `game/citadel/bin/win64/`.

### clang-cl
LLVM's MSVC-compatible compiler driver. Used via `--driver-mode=cl` wrapper
script in the Docker cross-compile. See [[docker-build]].

### CreateInterface
C-exported function on every Source 2 engine DLL. Takes an interface name
string, returns a pointer. The discovery primitive for all engine interfaces.
See [[source-2-engine]].

### deadworks
The C++ native + C# managed plugin host for Deadlock. Replaces
`deadlock.exe` as the server's entrypoint. See [[deadworks-runtime]].

### `deadworks_mem.jsonc`
Memory signature file at `game/citadel/cfg/deadworks_mem.jsonc`. Holds byte
patterns for function-finding + vtable offset tables. See
[[deadworks-mem-jsonc]].

### DEADWORKS_ENV_*
Env-var prefix on the launcher that forwards vars to the spawned game
process for plugin consumption. See [[deadworks-runtime]].

### dl_midtown
Default Deadlock map slug. Idle server has ~860 entities.

### engine2.dll
Source 2's core engine DLL (~6.6 MB). Exports `Source2Main`, hosts
`GameResourceServiceServerV001` and other key interfaces.

### extra-plugins
A BuildKit named build context used in the deadworks Dockerfile to inject
out-of-tree plugin sources. Overridden via `--build-context extra-plugins=<path>`
or `additional_contexts` in docker-compose. See [[plugin-build-pipeline]].

### flex slot
Late-game extra item slot in Deadlock. Unlock requires writing BOTH
`m_bFlexSlotsForcedUnlocked` on `CCitadelGameRules` AND `m_nFlexSlotsUnlocked`
bitmask on every `CCitadelTeam`. See [[deadlock-game]].

### `gamemodes.json`
Per-gamemode plugin selection file at repo root. Keys = gamemode names,
values = plugin folder names (NOT `AssemblyName`s). See
[[plugin-build-pipeline]].

### GameResourceServiceServerV001
Interface exported by `engine2.dll`. `CGameEntitySystem` pointer lives at
`+0x58`; must be re-read fresh each call (not stable across map loads).

### GE-Proton
GloriousEggroll's Proton fork. Default pinned to `GE-Proton10-33`. See
[[proton-runtime]].

### HookResult
Enum returned by plugin event handlers: `Continue=0`, `Stop=1`, `Handled=2`.
Dispatch aggregation takes the max — strongest value wins. See
[[deadworks-runtime]].

### IDeadworksPlugin
C# interface all plugins implement (usually via `DeadworksPluginBase`).
~18 lifecycle/hook methods. See [[deadworks-runtime]].

### kTeam_*
Deadlock team enum (`kTeam_Amber = 2`, `kTeam_Sapphire = 3`, `0` = unassigned).

### `-netconport`
Source 2 launch flag that opens a plain-text newline-delimited TCP console.
Unauthenticated. NOT Source 1 binary RCON. See [[source-2-engine]].

### nethost / hostfxr
.NET 10 Windows hosting libraries. `nethost.lib` provides
`get_hostfxr_path()`; hostfxr searches `DOTNET_ROOT` → `C:\Program Files\dotnet\`.
See [[deadworks-runtime]].

### `npc_boss_tier2`
Walker / Tier 2 tower classname. Per-team, carries `m_eLaneColor`. Used by
[[deathmatch]] as spawn anchors.

### PluginLoader
C# class in `managed/PluginLoader.cs` that scans, loads, dispatches, and
hot-reloads plugins. Partial-classed across ChatCommands / EntityIO /
Events / NetMessages. See [[deadworks-plugin-loader]].

### `<Private>false</Private>`
MSBuild attribute on plugin csproj references to `DeadworksManaged.Api`
(and `Google.Protobuf`). Pairs with `ExcludeAssets=runtime`. Ensures only
the host's copy of the shared assembly loads at runtime. See
[[plugin-build-pipeline]].

### Proton
Valve's Wine-based Windows-on-Linux runtime. GE-Proton fork used here to
host the Windows Deadlock dedicated server on Linux. See [[proton-runtime]].

### safetyhook
x86-64 inline trampoline hook library used by deadworks. Uses Zydis for
instruction-boundary detection. In `deadworks/vendor/`.

### SchemaAccessor
C# type `SchemaAccessor<T>` using UTF-8 byte literals for class/field
names: `new("CCitadelGameRules"u8, "m_flGameStartTime"u8)`. The canonical
plugin API for schema field access. See [[deadworks-runtime]].

### signatures
Byte-pattern memory signatures in [[deadworks-mem-jsonc]] used to locate
Source 2 functions at runtime. Hex bytes with `?`/`??` wildcards. Stale
sigs cause hard boot crash.

### sourcesdk
The `sourcesdk` git submodule. Hosts `protoc.exe`, Source 2 headers, and
the Deadlock `.proto` set. Single source of truth for build-time protobuf
generation. See [[deadworks-sourcesdk]].

### Source2Main
Exported function on `engine2.dll`. The canonical dedicated-server entry
point. Deadworks calls it after installing hooks. See [[source-2-engine]].

### steamclient64.dll
Windows Steam client DLL. Must be copied from the Steamworks SDK Redist
(app 1007) into **three locations** for Wine to find it. See
[[proton-runtime]].

### SteamID64
Player identifier, at `CBasePlayerController + 0x708` (u64,
`76561190000000000..76561210000000000` range).

### walker
Colloquial name for `npc_boss_tier2` (Tier 2 tower). Per-team per-lane.

### Wine prefix
Per-app Wine state directory. Here always at
`/home/steam/.steam/steam/steamapps/compatdata/1422450/pfx`. See
[[proton-runtime]].

### `WINEDLLOVERRIDES`
Env var controlling Wine's DLL search behavior. This project sets
`steamclient=n;steamclient64=n` (prefer native/copied DLLs over Wine
builtins). Must be exported inside the gosu subshell — outer-shell
exports are silently dropped.

### xwin
`0.6.5` tool that splats MSVC SDK + CRT into `/xwin` inside the Docker
native-builder stage. Lets clang-cl cross-compile Windows binaries from
Linux. See [[docker-build]].
