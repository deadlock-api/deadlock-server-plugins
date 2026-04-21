---
date: 2026-04-21
task: session extract — server-plugins d63499d3
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/d63499d3-be58-4571-89af-61447eb4239e.jsonl]
---

Historic session (2026-03-24): initial Rust-based plugin toolchain (pre-Deadworks/C# era). Built `plugin_loader.dll`, `entity_plugin.dll`, and a DLL injector, and debugged the Docker/Proton launch pipeline.

## Source 2 engine

- `server.dll` imports only `tier0.dll` + `steam_api64.dll`; other interfaces resolved at runtime via `CreateInterface` (L117). Runtime-needed DLLs: `engine2.dll` (for `GameResourceServiceServerV001`), `schemasystem.dll` (for `SchemaSystem_001`), `tier0.dll` (for `VEngineCvar007`).
- Key interface versions discovered: `Source2Server001`, `SchemaSystem_001`, `GameResourceServiceServerV001`, `VEngineCvar007` (L117).
- Entity system pointer lives at **offset 0x58** on `GameResourceServiceServerV001` (L191). The chunk list pointer is at `EntitySystem + 0x10`.
- Offsets resolved by binary-scanning `server.dll` for schema field metadata (locate field-name string, find `SchemaClassFieldData_t` that references it). Schema vtable calls via `SchemaSystem_001 -> FindTypeScopeForModule("server.dll") -> FindDeclaredClass -> iterate fields` were tried but crashed repeatedly (L600); binary scan used as the safe path.
- Discovered offsets (build at time of session): `m_CBodyComponent = 0x30`, `m_pSceneNode = 0x8`, `m_vecAbsOrigin = 0xC8` (L634). Position chain: `entity -> m_CBodyComponent -> m_pSceneNode -> m_vecAbsOrigin`.
- `CEntityIdentity` name offset 0x18 was wrong for the build — class names read as `?` while iteration still worked (L985).
- Entity 0 is a static singleton whose instance pointer lives inside `server.dll`'s mapped range (not heap); its `+0x30` etc. point into vtable memory, so naïve body-component traversal on entity 0 segfaults (L897, L953).
- Chunk list is **not** a flat 32-pointer array — reading past the first chunk's pointer crashes (L921). Chunks are 512 entities each; most slots are zero on a fresh map.
- `CGameEntitySystem` access from `GameResourceServiceServerV001` + offset 0x58 validated live: 861 entities on `dl_midtown` after full 16384-slot scan (L988).

## Deadlock game systems

- Dedicated-server launch cmd captured: `./deadlock.exe -dedicated -console -condebug +ip 0.0.0.0 -port 27015 -netconport 27015 -allow_no_lobby_connect -game citadel +map dl_midtown` (L324).
- Without a GSLT, Valve's GC kicks the server after ~18s with `SteamLearn: Invalid HMAC encoding`, exit code 5 (L721, L765). Workaround: add `-insecure` to skip VAC/GC auth — server then stays up indefinitely (L784).
- Server binary name for injector matching: `project8_server.exe` by default, actually `deadlock.exe` in Docker launch (L272, L324).
- Example entities seen on `dl_midtown`: `combine_watcher_blue`, `combine_t2_boss_purple`, `960_box`, `bounce_pad_sound`, `fog_blinded` (L991) — useful as sanity-check names for entity iteration.

## Deadworks runtime

- Early plugin-loading chain: injector.exe -> `CreateRemoteThread` + `LoadLibraryA` -> `plugin_loader.dll` -> scans `<exe_dir>/plugins/*.dll` (L223, L227). Predates the current Deadworks C# path.
- File-based IPC used `Z:\tmp\set_position_cmd.txt` / `_result.txt` / `.log` — Wine's `Z:` maps to Linux `/` so container `/tmp` is shared (L707). Gotcha: file ownership matters — root-created command files block the Wine-side steam user from overwriting/truncating (L652, L658).
- `catch_unwind` does **not** catch SIGSEGV in Wine — segfaults kill the whole process. Must use `IsBadReadPtr` before every dereference when walking unknown pointer chains (L971, L985). This is the single most load-bearing safety primitive for the entity walker.

## Plugin build & deployment

- DLL injection via Wine requires matching fsync/esync flags: if Proton runs with `WINEFSYNC=1` the injector must too, else wineserver rejects the connection with `Server is running with WINEFSYNC but this process is not` (L555).
- Proton `proton run` wrapper is a Python script; bypassing it with `${PROTON_DIR}/files/bin/wine64` directly loses the `lsteamclient` bridge that provides `SteamClient023` (L503). Solution: go back through `proton run` once the prefix is properly initialized.
- Wine prefix must be **wineboot --init'd as the steam user** (not root + chown after), else `wine: '...' is not owned by you` and `kernel32.dll status c0000135` (L404, L452, L459).
- GE-Proton version matters: GE-Proton9-5 lacked `SteamClient023`; GE-Proton9-22 had a patched builtin; GE-Proton10-33 removed builtin steamclient entirely and relies on the Python wrapper bridge (L464, L471, L503). Session settled on 10.33 via `proton run`.
- Docker entrypoint gotcha: backgrounding `deadlock.exe` with `&` and redirecting output caused Proton to exit code 53 with zero output — Proton's wrapper needs a foreground terminal. Fix: run server in foreground, run injector in background (L333).
- `WINEDLLOVERRIDES=plugin_loader=n` was initially used to autoload, but doesn't actually fire a `DllMain` thread that touches the game; the explicit injector replaced it (L318).
- `compatdata` volume persists a poisoned prefix across rebuilds — `docker compose down -v` required after Proton version or prefix-init changes (L448, L459).
- Cross-compile target: `x86_64-pc-windows-gnu`; required features in workspace: `Win32_System_ProcessStatus`, `Win32_System_Threading` (L179). Rust 2024 edition warns on nested `unsafe` in `unsafe fn` bodies (L191).
