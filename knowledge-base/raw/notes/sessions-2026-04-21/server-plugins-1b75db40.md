---
date: 2026-04-21
task: session extract — server-plugins 1b75db40
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/1b75db40-3412-4f83-8852-a967b95d9a14.jsonl]
---

NOTE: This session was run in a Rust-based sibling project (not the C# deadlock-server-plugins repo). Findings are about Source 2 / Deadlock internals and transfer directly to the C# plugin work. File paths reference the Rust plugin codebase.

## Source 2 engine

- `CreateInterface` pattern: every engine DLL exports `CreateInterface` (C function). Resolve via `GetProcAddress(GetModuleHandleA("engine2.dll"), "CreateInterface")`, then call with `(name_cstr, null)` to obtain a named interface pointer. Used for `GameResourceServiceServerV001` (engine2.dll) and `SchemaSystem_001` (schemasystem.dll).
- Schema system vtable layout (from community RE, stable across CS2/Deadlock): `CSchemaSystem::FindTypeScopeForModule` at vtable index **13**, `CSchemaSystemTypeScope::FindDeclaredClass` at vtable index **2**. `SchemaClassInfoData_t` layout: `+0x00 pSelf, +0x08 name, +0x10 module, +0x18 size(i32), +0x1C field_count(i16), +0x28 fields(SchemaClassFieldData*)`. `SchemaClassFieldData`: `+0x00 name, +0x08 type*, +0x10 offset(i32)`.
- **Schema system vtable calls crash under Wine/Proton.** Binary scan of module memory for `SchemaClassInfoData_t` matching `class_name` is reliable; vtable calls are not. Prefer `scan_for_field_offset` over `schema_find_offset`. See plugins/entity-plugin/src/lib.rs:332-346 (fallback logic) — the scan-first-then-vtable pattern.
- Entity system layout: `GameResourceServiceServerV001 + 0x58` → entity system pointer. Entity system has inline chunk array at `+0x10` (NUM_CHUNKS=64, first 32 non-networkable + 32 networkable). Each chunk = 512 `CEntityIdentity` (0x70 bytes each). `CEntityIdentity`: `+0x00 instance_ptr, +0x20 designer_name*`. MAX_ENTITIES = 32768.
- Hardcoded offsets on CBaseEntity (dl build ~6411): `m_iTeamNum=0x33C, m_nSubclassID=0x314, m_iHealth=0x2D0, m_iMaxHealth=0x2D4, m_CBodyComponent=0x30`. CBodyComponent: `m_pSceneNode=0x8`. CGameSceneNode: `m_vecAbsOrigin=0xC8`.
- CCitadelPlayerController: `m_hHeroPawn=0x984, m_steamID=0x708` (CBasePlayerController). `PlayerDataGlobal_t::m_nHeroID=0x9E4`. CCitadelPlayerPawn: `m_nLevel=0xEB8`.
- Entity handle convention: low 15 bits (`& 0x7FFF`) are the entity index; `0xFFFFFFFF` = invalid.
- Server log shows state transitions: `ss_waitingforgamesessionmanifest -> ss_loading -> ss_active`. Match states enum (`m_eGameState`): 0-11, named Init/WaitingForPlayers/…/GameInProgress(7)/PostGame/End/Abandoned.

## Deadlock game systems

- **CCitadelGameRules discovery**: walk entity system for entity with designer_name `citadel_gamerules` (the `CCitadelGameRulesProxy` entity). Read `m_pGameRules` at proxy offset **0x4A0** → pointer to actual `CCitadelGameRules` instance.
- CCitadelGameRules resolved offsets (dl build ~6411, from binary scan): `m_eGameState=0xFC, m_bServerPaused=0x27D8, m_iPauseTeam=0x27DC, m_flMatchClockAtLastUpdate=0x27E4, m_nMatchClockUpdateTick=0x27E0, m_flGameStartTime=0xE8, m_bFreezePeriod=0xE0, m_eMatchMode=0x12C, m_eGameMode=0x130, m_unMatchID=0x2870`.
- `m_nPauseStartTick` and `m_nTotalPausedTicks` are **not on CCitadelGameRules directly** — they live on a base class (`CGameRules`/`CMultiplayRules`). Binary scan on `CCitadelGameRules` fails for these. Need to scan their parent class instead.
- **Writing `m_flMatchClockAtLastUpdate` is ineffective** — the game recalculates match clock every tick; direct memory writes are overwritten. Same likely true of paused flag writes. Use console commands for control, memory only for reads.
- Pause command gotchas: `citadel_toggle_server_pause` requires a `PlayerId` (designed for player-initiated pause — debug string `CCitadelGameRules:Pause = true PlayerId=%d fDelay=%4.2f`). `setpause`/`unpause`/`sv_pausable` are engine commands in engine2.dll but some are FCVAR_CHEAT. Many pause convars are cheat-protected (`citadel_pause_minimum_time`, `citadel_pregame_wait_duration`, `citadel_match_intro_duration_*`) — need `sv_cheats 1` first.
- **`setpause`/`unpause` in Source 2 are actually network messages** (CSVCMsg_SetPause, CCLCMsg_RequestPause), dispatched by the ConCommand handler — not directly invocable via arbitrary buffers. Running them via netcon works; writing to CCommandBuffer::AddText does not reliably trigger the handler.
- Deadlock bot-match console knobs: `citadel_solo_bot_match 1`, `citadel_one_on_one_match`, `citadel_num_team_pauses_allowed`, `citadel_allow_pause_in_match`, `citadel_pause_allow_immediate_if_single_player`, `citadel_pause_cooldown_time`, `citadel_force_unpause_cooldown`. Cheat-protected: intro/pregame durations.
- Dedicated-server launch flags used: `-dedicated -console -condebug -insecure -allow_no_lobby_connect -game citadel +map dl_midtown -port 27015 -netconport 27015`. Netcon port = server port (same 27015). App ID = 1422450 (client) / 1422460 (dedicated server reset in `ResetBreakpadAppId`).

## Deadworks runtime

- (This session used the Rust plugin host, not Deadworks C#.) Architecture patterns observable:
- Plugin loader injects `plugin_loader.dll` (copied to `game/bin/win64/`) via `injector.exe` using classic CreateRemoteThread+LoadLibraryA. Plugin DLLs live in `game/bin/win64/plugins/`.
- Per-plugin tracing log at `Z:\tmp\<plugin>_plugin.log` (Wine Z: mount). init 3s → reduced to 1s because dedicated server without players auto-exits quickly.
- Plugin-side socket IPC: `PluginSocket::connect("127.0.0.1:9100", plugin_name, version)` registers with `server-manager` (host-side daemon on :9100, CLI on :9101). Protocol is JSON envelopes `{id, data}`. Commands are dispatched from `server-manager send <plugin> '<json>'`. Server-manager also provides `rcon <host> <port> <cmd>` that talks to the Source 2 netcon.

## Plugin build & deployment

- Dockerfile has three stages: `plugin-builder` (Rust cross-compiling to `x86_64-pc-windows-gnu` with mingw for plugin DLLs), `manager-builder` (native Linux for server-manager), then `base` layered on `cm2network/steamcmd:root`. Production and test targets both derive from base; test target layers `test-*.sh` scripts.
- Proton runtime: GE-Proton10-33 downloaded on first boot to `/opt/proton`, with a `.proton_wine64_marker` inside the pfx to skip re-init. Wine prefix = `/home/steam/.steam/steam/steamapps/compatdata/1422450/pfx`. Steam client DLLs copied from redist into `pfx/drive_c/Program Files (x86)/Steam/` AND into the game's `win64/` dir AND into `windows/system32/`.
- Server runs under `proton run ./deadlock.exe ...` (not raw wine64 — Proton handles steamclient bridging). Injector uses `wine64 ./injector.exe` directly (no Proton needed, since injector is a plain Win32 PE).
- docker-compose.test.yaml pattern: each `test-*` service spawns `entrypoint.sh &` in background, then runs the test script, then kills the server PID. Shares `proton-10-33` and `gamedata` named volumes across tests so SteamCMD download is cached.
- mise.toml is the developer UX layer: `mise test` fan-outs to all test services; `mise entity:*`, `mise game:*` pass JSON to `server-manager send` via docker compose exec. mise tasks that take args use `$1` placeholders inside the run string.
- When plugin init takes too long the dedicated server auto-exits before the plugin's socket register call — always minimize the pre-socket-connect work.
- `entrypoint.sh` clears `/tmp/*_plugin.log` before each run and dumps them at exit — a reliable tail for debugging crashes that happen before the plugin reports anything to server-manager.
- CCommandBuffer discovery (DEAD END — kept for future reference so we don't redo it): CCommandBuffer has NO vtable; constructor initializes fields at offsets 0x8020+. Sizeof = **0x86D8**. Global instances live in engine2.dll `.data` as an array; callers compute `this = base + slot * 0x86D8 [+ inner_offset]`. One call site uses `this = RSI + slot*0x86D8 + 0x588` with `unknown3=true` (server buffer?). The mangled export from tier0.dll is `?AddText@CCommandBuffer@@QEAA_NPEBDHH_NN_K@Z` (sig: `bool(this, text, source_i32, u2_i32, u3_bool, u4_f64, flags_u64)`). `SetRequiredFlags` is `?SetRequiredFlags@CCommandBuffer@@QEAA_K_K@Z`. **Dispatching to this buffer only worked for `changelevel`** — `setpause`, `mp_restartgame`, `sv_pausable` silently no-op'd regardless of buffer offset / flags=0 / unknown3=true. The working solution was netcon TCP from inside the plugin (reusing the existing `rcon` crate), which goes through the engine's full ConCommand pipeline.
