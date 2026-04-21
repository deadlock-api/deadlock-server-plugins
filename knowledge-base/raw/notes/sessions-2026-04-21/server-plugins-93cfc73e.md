---
date: 2026-04-21
task: session extract — server-plugins 93cfc73e
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/93cfc73e-12fe-4531-a196-26cda9f622b6.jsonl]
---

Note: session operated on a **prior Rust-based incarnation** of this repo (branch `master`, 4 commits ending `60e1992`), not the current C#/Deadworks codebase. Findings below describe that earlier architecture.

## Source 2 engine

- Entity access path confirmed: `engine2.dll` exports `CreateInterface`; request `GameResourceServiceServerV001`; `CGameEntitySystem*` sits at +0x58 from that interface pointer (`GAME_RESOURCE_SERVICE_ENTITY_SYSTEM_OFFSET`, entity-plugin/src/lib.rs:40).
- Entity identity list inside `CGameEntitySystem` is chunked: **512 entities per chunk**, each `CEntityIdentity` is **120 bytes (0x78)**, max index 16384 (entity-plugin/src/lib.rs:43-52). Chunk-list pointer offset is NOT stable — plugin probes candidates `[0x10, 0x18, 0x20, 0x08, 0x28, 0x30, 0x38, 0x40, 0x48, 0x50, 0x58, 0x60, 0x78]` and validates via alignment + plausibility (lib.rs:281).
- `CEntityInstance +0x10` → `CEntityIdentity*`; `CEntityIdentity +0x18` → `m_designerName` (const char*) — used for class-name readback (lib.rs:351-354).
- Schema system access via `schemasystem.dll` → `SchemaSystem_001`. **vtable indices**: `FindTypeScopeForModule` = 13, `CSchemaSystemTypeScope::FindDeclaredClass` = 2 (lib.rs:61-62). Scope lookup uses literal `"server.dll"`.
- `SchemaClassInfoData_t` layout discovered: +0x08 name, +0x10 module, +0x18 size(i32), +0x1C field_count(i16), +0x28 fields pointer (lib.rs:184-192). `SchemaClassFieldData_t`: name@0x00, type@0x08, offset@0x10, metadata_size@0x14, metadata@0x18.
- Binary-scan fallback for schema offsets: finds the null-terminated field-name string in `server.dll`, then scans for an 8-byte pointer-to-string, reads `+0x10` as offset. Sanity range: `0 < offset < 0x10000` (lib.rs:229-272).

## Deadlock game systems

- Steam App ID **1422450** (entrypoint.sh:3). Game binary is `game/bin/win64/deadlock.exe` within the server install tree (entrypoint.sh:43).
- Schema field resolution for teleport uses three lookups: `CBaseEntity::m_CBodyComponent`, `CBodyComponent::m_pSceneNode`, `CGameSceneNode::m_vecAbsOrigin` (lib.rs:440-442). These are the minimum offsets needed to write entity position.
- Default map used by container: `dl_midtown`; integration test asserts ≥100 entities load on this map (test-entity-list.sh:76).
- Running server process name inside the Wine prefix is **`project8_server.exe`** (injector default target — tools/injector/src/main.rs:18). This is what the DLL injector attaches to, not `deadlock.exe`.

## Deadworks runtime (prior Rust plugin framework)

- `plugin_sdk::plugin_entry!(on_attach, on_detach)` macro generates `DllMain`; on `DLL_PROCESS_ATTACH` it **spawns a std::thread** to run attach logic, explicitly to avoid loader-lock deadlocks (crates/plugin-sdk/src/lib.rs:20-46). `hinst` is cast through `usize` to cross the thread boundary.
- `plugin_loader.dll` is a cdylib entry DLL that, on attach, does `GetModuleFileNameA`, strips to dir, then `FindFirstFileA("plugins\\*.dll")` + `LoadLibraryA` for each match (crates/plugin-loader/src/lib.rs). Plugins must sit in a `plugins/` subdir next to the loaded process's exe.
- Entity plugin communicates via filesystem: reads commands from `Z:\tmp\set_position_cmd.txt`, writes results to `Z:\tmp\set_position_result.txt`, logs to `Z:\tmp\set_position.log` (lib.rs:461,624,83). `Z:` is Wine's mapping to Linux `/`. Commands: `set <idx> <x> <y> <z>`, `list [max]`, `probe`, `dump`. Poll interval 100ms, change-detected by mtime.
- Init sleeps 3s before resolving modules — modules may still be initializing when plugin attaches (lib.rs:402).
- `read_ptr` validator requires `0x100000 < val < 0x7FFF_FFFF_FFFF` and 8-byte alignment, plus `IsBadReadPtr` check (lib.rs:304-314) — used pervasively before every dereference to survive uninitialized entity slots.

## Plugin build & deployment

- Rust workspace, edition 2024, target `x86_64-pc-windows-gnu`, linker `x86_64-w64-mingw32-gcc` (.cargo/config.toml). All six crates cross-compile from Linux to Windows DLLs/EXEs.
- Dockerfile is 3-stage: `plugin-builder` (FROM rust, mingw) → `base` (FROM cm2network/steamcmd:root + Proton deps + i386 libs) → `production` | `test` targets. Plugins land at `/opt/plugins/` in the image (Dockerfile:54-56).
- Proton runtime: **GE-Proton10-33** downloaded from GloriousEggroll GitHub release at first boot, cached via Docker volume `proton-10-33` (entrypoint.sh:10,19). Marker file `${PFXDIR}/.proton_wine64_marker` triggers prefix rebuild if Proton version changes (entrypoint.sh:70-82).
- Wine prefix needs Steam client DLLs copied from **app 1007 (Steamworks SDK Redist)** — `steamclient64.dll`/`steamclient.dll` installed into three locations: `Program Files (x86)/Steam`, `game/bin/win64`, and `windows/system32` (entrypoint.sh:88-99).
- Test harness is a Windows EXE (`test-harness.exe`) that calls `LoadLibraryA("plugin_loader.dll")` then sleeps 2s — used under Wine standalone, without the game, for fast injection smoke testing (tests/test-harness/src/main.rs).
- `docker-compose.test.yaml` defines two services: `test-injection` (runs harness under Wine) and `test-entity-list` (full game boot + plugin + 120s polling for `Initialization complete` in the log, then sends `list 16384`).
- Injector uses classic Win32 injection: `OpenProcess(PROCESS_ALL_ACCESS)` → `VirtualAllocEx` → `WriteProcessMemory` → `CreateRemoteThread(LoadLibraryA, path)` → `WaitForSingleObject` → `GetExitCodeThread` (zero means load failed) (tools/injector/src/main.rs:106-189).
- Post-session outcome: mise.toml was added with tasks `build`, `build:debug`, `check`, `clean`, `fmt`, `docker:build|up|down`, `test`, `test:injection`, `test:entity`, `entity:build|list|test`. Config required `mise trust` (jsonl line 113-115).
- Note: `.env` in the session logs **leaked real Steam credentials** (STEAM_LOGIN=jgg1balp / STEAM_PASSWORD). `.gitignore` does exclude `.env`, but the creds were echoed in the session transcript.
