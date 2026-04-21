---
date: 2026-04-21
task: session extract — custom-server a6b83c6e
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-custom-server/a6b83c6e-b037-44d7-b823-4ed3cfa5c2d7.jsonl]
---

## Source 2 engine

- Pause is dispatched via three protobuf message classes found by `strings` on the game DLLs: `CCitadelClientMsg_Pause` / `CCitadelClientMsg_Pause_t` (client→server, msg id `CITADEL_CM_Pause`), engine-level `CCLCMsg_RequestPause` (`clc_RequestPause`), and server-broadcast `CSVCMsg_SetPause` (`svc_SetPause`).
- Pause state lives on `CCitadelGameRules` / `CGameRules`: `m_bGamePaused`, `m_bServerPaused`, `m_iPauseTeam`, `m_nPauseStartTick`, `m_nTotalPausedTicks`. Log strings `"CGameRules - paused on tick %d"` / `"unpaused on tick %d"` mark the code paths.
- `engine2.dll` exposes `VEngineCvar007` (ICvar) and `Source2EngineToServer001` via `CreateInterface`. `ServerCommand` is reachable via the `Source2EngineToServer001` vtable — index 36 is the typical Source 2 offset but drifts between builds and must be sig-verified against `"CSource2Server::GameFrame"`.
- Source 2 loading order on the server side: `deadlock.exe` -> `tier0.dll` -> `engine2.dll` -> `server.dll`. `tier0.dll` is imported by every game DLL but is too low-level to safely proxy.
- `deadlock.exe` does NOT import `version.dll`. Any DLL-proxy injection using `version.dll` silently never loads. Session verified this with `objdump -p` on the three copied DLLs (`client.dll`, `host.dll`, `server.dll`).

## Deadlock game systems

- Server pause ConVars (all in `server.dll`): `citadel_toggle_server_pause` (the one you actually call), `citadel_allow_pause_in_match`, `citadel_pause_count` (0=unlimited), `citadel_num_team_pauses_allowed`, `citadel_pause_cooldown_time`, `citadel_pause_countdown`, `citadel_pause_minimum_time`, `citadel_pause_force_unpause_time`, `citadel_unpause_countdown`, `citadel_pause_resume_time`, `citadel_pause_resume_time_disconnected`, `citadel_force_unpause_cooldown`, `citadel_pause_matchtime_before_allow`, `sv_pause_on_tick`.
- Pre-pause config to bypass match-pause limits: `citadel_allow_pause_in_match 1; citadel_pause_count 0; citadel_pause_force_unpause_time 0; citadel_pause_countdown 0`.
- Chat token strings reveal pause UX flow: `CITADEL_CHAT_MESSAGE_CANTPAUSEYET`, `NOPAUSESLEFT`, `AUTO_UNPAUSED`, `PAUSE_COUNTDOWN`, `UNPAUSE_COUNTDOWN`, `NOTEAMPAUSESLEFT`, `CANTUNPAUSETEAM`.
- Main server `gameinfo.gi` lives at `/home/steam/server/game/citadel/gameinfo.gi` inside the container; Source 2's addon system has no "load arbitrary DLL" hook, so gameinfo is not a viable injection vector.

## Deadworks runtime

- Dedicated server is a Windows PE64 running under Proton/Wine inside the `deadlock` Docker service, not a native Linux binary. DLL injection (Windows) is required; `.so` `LD_PRELOAD` only reaches the Linux Proton wrapper, not the Wine-hosted `deadlock.exe` process.
- Proton launch line observed in logs: `/opt/proton/proton run ./deadlock.exe -dedicated -console -usercon -condebug +ip 0.0.0.0 -port 27015 -netconport 27015 -allow_no_lobby_connect -game citadel +map dl_midtown +rcon_password test +tv_enable 1 +tv_broadcast 1 +tv_broadcast_url http://hltv-relay:3000/ +tv_broadcast_origin_auth deadlock-relay`.
- `SteamGameId=1422450` is the Deadlock app id; the shim gates activation on it.
- Proton stdout is captured to `/tmp/proton_stdout.log`; DLL `stderr` surfaces there, not in `docker compose logs`.
- `WINEDLLOVERRIDES` set in the outer entrypoint is silently clobbered by the inner `gosu steam bash -c` block (~line 161 of entrypoint.sh) — any Wine override must be exported inside that inner shell or it never reaches the process.
- `AppInit_DLLs` registry injection under this Proton build is unreliable: session tried it, the container crashed without loading the DLL.
- `LD_PRELOAD` of a Linux `.so` shim reaches the outer Proton process and can `dlopen` + call `LoadLibraryA` with a `Z:\` UNC path to side-load a Windows DLL into Wine. The shim must wait for `kernel32.dll.so` to appear before resolving `LoadLibraryA`. Observed gotcha: GE-Proton may rename or relocate Wine's `.so` modules, causing the wait loop to hang.
- TCP port 27050 was chosen for the out-of-band pause control socket; must be added to `docker-compose.yml` port map for host access.

## Plugin build & deployment

- Cross-compiling a Windows DLL from Linux uses the Rust target `x86_64-pc-windows-gnu` + `gcc-mingw-w64-x86-64` as linker; crate type `cdylib`. Docker build stage uses `rust:1.94-bookworm`.
- `windows-sys` 0.61 moved `BOOL` from `Win32::Foundation` to `windows_sys::core::BOOL`; `TRUE` stayed put but is typed against the new core alias. Breaking change vs 0.59.
- Cargo workspace dep bumps applied this session: `tokio 1.50`, `thiserror 2.0`, `tracing 0.1.44`, `clap 4.6`, `anyhow 1.0`, `tracing-subscriber 0.3.23`, `axum 0.8.8`, `serde 1.0`, `serde_json 1.0`. `pause-injector` crate uses Rust edition 2024.
- Docker compose build context had to move to the repo root so the new crate under `crates/pause-injector/` resolves through the workspace `Cargo.toml`.
- `./server` bind-mount on the host is empty — game files live only inside the running container, so host-side inspection of `deadlock.exe` imports requires `docker compose exec`.
