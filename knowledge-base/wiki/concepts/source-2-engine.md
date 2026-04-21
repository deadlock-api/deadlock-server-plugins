---
title: Source 2 engine (as it applies to this project)
type: concept
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-1b75db40.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-34752d6a.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-4327d1b2.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-4f2af8b9.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-51b5680e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-65d13a2e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-7554a944.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-81382d9e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-93cfc73e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-9df5d718.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-d63499d3.md
  - knowledge-base/raw/notes/sessions-2026-04-21/custom-server-2c3ccbd4.md
  - knowledge-base/raw/notes/sessions-2026-04-21/custom-server-3f49d607.md
  - knowledge-base/raw/notes/sessions-2026-04-21/custom-server-9a7f664c.md
  - knowledge-base/raw/notes/sessions-2026-04-21/custom-server-a6b83c6e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-1dba11a1.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-52a01b09.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-88df5d67.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-493a9384.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-fa5d1d7e.md
related:
  - "[[deadlock-game]]"
  - "[[deadworks-runtime]]"
  - "[[deadworks-mem-jsonc]]"
  - "[[protobuf-pipeline]]"
created: 2026-04-21
updated: 2026-04-21
confidence: high
---

# Source 2 engine (as it applies here)

The Deadlock dedicated server is a Source 2 Windows PE64 running under
Proton/Wine on Linux. These notes only cover the parts that matter for
plugin work, signature scanning, and server hosting.

## Entry points and DLL layout

- Server process starts at `deadlock.exe`, which loads `tier0.dll`, then
  `engine2.dll`, then `server.dll`. Layout under the install:
  - `<install>/game/bin/win64/deadlock.exe` + `engine2.dll` + `tier0.dll`
  - `<install>/game/citadel/bin/win64/server.dll` (~54 MB)
  - `engine2.dll` ~6.6 MB
- Deadworks replaces the entrypoint: `deadworks.exe` calls `LoadLibrary` on
  `engine2.dll`, installs a hook on `OnAppSystemLoaded`, then invokes the
  exported `Source2Main(hInst, hPrev, cmdLine, nShowCmd, baseDir, game)` with
  `"citadel"` as the game name. See
  `deadworks/src/startup.cpp:9,63,76` via `deadworks-52a01b09`, `deadworks-88df5d67`.
- Engine expects CWD = game root (`game/bin/win64`). Launching via `proton run`
  requires an explicit `cd` in the entrypoint (server-plugins-9df5d718).

## CreateInterface and the key interfaces

Each engine DLL exports a C function `CreateInterface(name, returnCode)`.
Discovery pattern: `GetProcAddress(GetModuleHandleA("engine2.dll"), "CreateInterface")`.

Versions used by this project (server-plugins-d63499d3, server-plugins-51b5680e):

- `GameResourceServiceServerV001` — `engine2.dll`. Holds a pointer to the
  entity system at **offset 0x58**. Must be re-read fresh each call; caching
  across map loads gives stale/empty pools (server-plugins-7554a944).
- `SchemaSystem_001` — `schemasystem.dll`.
- `VEngineCvar007` — `tier0.dll`. `ICvar` interface.
- `Source2EngineToServer001` — exposes `ServerCommand`. `ServerCommand` is
  at vtable index ~36 but the index drifts between builds and must be
  sig-verified against strings like `"CSource2Server::GameFrame"`
  (custom-server-a6b83c6e).

## Entity system

`CGameEntitySystem` layout (resolved against current Deadlock build;
server-plugins-1b75db40, server-plugins-7554a944, server-plugins-4f2af8b9):

- Chunk table is **inline** at `entity_system + 0x10`, 8-byte pointer
  stride, **64 entries**. Chunks 0–31 non-networkable, 32–63 networkable.
  Older Rust-era notes said 32 entries — newer is authoritative.
- `ENTITIES_PER_CHUNK = 512` (confirmed by disassembly pattern
  `shr ecx, 9` / `and ecx, 0x1FF`). `MAX_ENTITIES = 32768`.
- `CEntityIdentity` stride = **0x70 (112 bytes)** in Deadlock. CS2 had 0x78
  (120); using the CS2 size under Deadlock compounds 8 bytes of drift per
  index. Layout: `+0x00 m_pInstance`, `+0x08 class-metadata code pointer`,
  `+0x20 m_designerName*` (char*, MAY be non-8-byte-aligned — pointer-alignment
  guards must be relaxed to read class names).
- Entity handle convention: low 15 bits (`& 0x7FFF`) = entity index;
  `0xFFFFFFFF` = invalid.

Resolved field offsets on `CBaseEntity` for current Deadlock (approx build
6411; server-plugins-1b75db40, server-plugins-34752d6a):

- `m_CBodyComponent = 0x30`
- `m_NetworkTransmitComponent = 0x38` (**inline**, NOT a pointer)
- `m_iHealth = 0x2D0`, `m_iMaxHealth = 0x2D4`, `m_lifeState = 0x2D8`
- `m_nSubclassID = 0x314` (CUtlStringToken u32)
- `m_iTeamNum = 0x33C`

Position chain: `entity -> m_CBodyComponent (0x30) -> m_pSceneNode (0x8) -> m_vecAbsOrigin (0xC8)`.

## Schema system

Two paths for resolving schema field offsets (server-plugins-51b5680e,
server-plugins-1b75db40):

1. **Vtable call**: `SchemaSystem_001.FindTypeScopeForModule("server.dll")`
   at vtable index **13**, then `CSchemaSystemTypeScope::FindDeclaredClass`
   at vtable index **2**. Returns `SchemaClassInfoData_t`:
   `+0x08 name, +0x10 module, +0x18 size(i32), +0x1C field_count(i16), +0x28 fields*`.
   `SchemaClassFieldData`: `+0x00 name, +0x08 type*, +0x10 offset(i32)`.
   Struct is **0x20 bytes**; treating it as 0x28 misaligns subsequent entries.

2. **Binary scan** (preferred under Wine/Proton; vtable calls crash the
   schemasystem there): find the null-terminated field-name string in
   `server.dll`, find pointer-to-string in a fields array, read offset at
   `+0x10`. Sanity range: `0 < offset < 0x10000`. Class-aware scanning
   prevents matching the same field name on a wrong class
   (server-plugins-4327d1b2).

Schema names are NOT globally unique — common names like `m_iHealth` appear
on many classes with different offsets, so class-filtered scanning is
mandatory.

Wine-specific gotcha: `__m_pChainEntity` schema lookup crashes the schema
system on Deadlock; the CS2 `CNetworkVarChainer` pattern does not apply
(server-plugins-34752d6a).

## Networking, dirty propagation

- Pure direct memory writes to networked fields do NOT propagate to clients.
  Source 2 uses delta-encoded networking; the dirty-notification path is
  mandatory. Setting `FL_EDICT_CHANGED`/`FL_FULL_EDICT_CHANGED` on
  `CEntityIdentity.m_flags` from either plugin or game thread does NOT
  trigger propagation (server-plugins-34752d6a).
- Engine functions like `ChangeTeam` must be called from the **game thread**,
  not an async worker — calling from a plugin thread hangs or deadlocks.
- `ISource2Server::GameFrame` vtable[6] hook never fires — the game's tick
  dispatcher uses a direct call, bypassing the vtable. Inline detour on the
  real function pointer DOES fire.
- `setpause` / `unpause` in Source 2 are actually network messages
  (`CSVCMsg_SetPause`, `CCLCMsg_RequestPause`), dispatched by the ConCommand
  handler — not directly invocable via arbitrary command-buffer writes.
  Running them via netcon works.

## Pause internals

Pause state lives on `CCitadelGameRules` / `CGameRules`
(custom-server-a6b83c6e, server-plugins-1b75db40):

- `m_bGamePaused`, `m_bServerPaused` (0x27D8), `m_iPauseTeam` (0x27DC),
  `m_nPauseStartTick`, `m_nTotalPausedTicks`.
- `m_nPauseStartTick` and `m_nTotalPausedTicks` live on the base
  (`CGameRules`/`CMultiplayRules`) class — scanning for them on
  `CCitadelGameRules` directly fails.
- Three protobuf message classes: client→server `CCitadelClientMsg_Pause`
  (id `CITADEL_CM_Pause`), engine `CCLCMsg_RequestPause` (`clc_RequestPause`),
  server broadcast `CSVCMsg_SetPause` (`svc_SetPause`).
- `citadel_toggle_server_pause` is the ConVar that works through
  `ServerCommand`. Many pause ConVars are FCVAR_CHEAT — need `sv_cheats 1`
  first. See [[deadlock-game]] for the full list.

## ConVars and commands relevant here

- `-netconport <port>` opens a **plain-text, newline-delimited TCP console**,
  NOT Source 1 binary RCON. No password handshake — unauthenticated.
  `rcon_password` applies to the classic Source RCON path, not to netcon
  (server-plugins-81382d9e).
- Source 2 binary RCON auth differs from Source 1: Source 2 replies with one
  `SERVERDATA_AUTH_RESPONSE` packet; Source 1 replies with two
  (empty `SERVERDATA_RESPONSE_VALUE` then `SERVERDATA_AUTH_RESPONSE`). See
  custom-server-2c3ccbd4, custom-server-9a7f664c for full wire format.
- `-usercon + +rcon_password` alone is **not** sufficient; the server needs
  `-netconport` to actually listen for external console connections.
- `condebug` writes `console.log` under **`game/citadel/console.log`**, not
  in `game/bin/win64/` — tailing the wrong file hides all diagnostics
  (deadworks-3beeff54, deadworks-530007be).

## SourceTV (GOTV) and broadcast

Source 2 has **two independent spectating mechanisms** that run simultaneously
(custom-server-3f49d607):

- Traditional SourceTV: `tv_enable 1` creates an in-process master/spectator
  bot bound on `tv_port` (UDP, default 27020). Capacity capped by
  `tv_maxclients` / `tv_maxrate`.
- GOTV+ / Broadcast: `tv_broadcast 1` is an extension **layered on top of**
  `tv_enable`, not a replacement. Engine chunks the SourceTV buffer into
  ~3s HTTP fragments and POSTs them to `tv_broadcast_url`; a relay serves
  them over HTTP.

Consequence: `tv_enable 1` is always required, even for pure broadcast
setups. Set `+tv_maxclients 0` to suppress direct spectator connections
while keeping broadcast alive.

## HUD match clock anchor

The HUD top-bar clock at `citadel_hud_top_bar.xml:16-18` binds to
`{s:game_clock}`. Client computes it as
`game_clock ≈ m_flMatchClockAtLastUpdate + (CurTick − m_nMatchClockUpdateTick) * IntervalPerTick`,
with additional `CurTime − m_fLevelStartTime` factoring. Writing only one of
these fields leaves the anchor stale and the displayed clock keeps climbing
even while the float is pinned (deathmatch-fa5d1d7e, deathmatch-980b8b28).

To freeze or reset the clock from a plugin, write ALL of:

- `m_flGameStartTime`, `m_fLevelStartTime`, `m_flRoundStartTime`
- `m_flMatchClockAtLastUpdate` AND `m_nMatchClockUpdateTick` (together!)

See [[deathmatch]] for the concrete pattern.

## Game events

- `.gameevents` files are in `game_exported/core.gameevents` and
  `game_exported/game.gameevents`, mirrored from
  `SteamTracking/GameTracking-Deadlock`. Used at build time by
  `DeadworksManaged.Generators.GameEventSourceGenerator` (deadworks-d75e1c40,
  deadworks-d416f1ea).
- Server-side event creation: `NativeInterop.CreateGameEvent` wrapped by
  `GameEvents.Create(name, force=true)` → `GameEventWriter` (chainable
  `SetString/SetInt/SetFloat/SetBool`) → `.Fire()` (deathmatch-493a9384).
- Signon state handshake: `CONNECTED → NEW → PRESPAWN → SPAWN → FULL`.

## Boot-time memory profile

Each Source 2 dedicated server (`deadworks.exe`) reserves ~2 GB of virtual
address space up front and eventually uses ~3.5 GiB RSS. Three gamemode
containers on one host → ~10.5 GiB working set (server-plugins-90226db4).

Wine OOM signature at boot:
`err:virtual:allocate_virtual_memory out of memory for allocation, base (nil) size 80000000`
cascading down to `size 00100000` means Wine can't get a large VA
reservation. The usual DLL-load warnings that follow are a consequence,
not the cause.

## Normal boot noise (not crashes)

`warn:module:load_dll` / `LdrGetProcedureAddress` lines for `dbghelp.dll`,
`vfbasics.dll`, `vrfcore.dll`, `psapi.dll`, `vconcomm.dll`, `tier0.dll`,
`iphlpapi.dll`, and `fixme:ntoskrnl:kernel_object_from_handle No constructor
for type "Token"` are normal Proton/Source 2 startup noise. Diagnose
failures by reading the ~200 lines before `exited with code 1`
(server-plugins-90226db4).
