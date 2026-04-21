---
date: 2026-04-21
task: session extract — custom-server 9a7f664c
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-custom-server/9a7f664c-f074-40a0-9138-a5d365324a54.jsonl]
---

## Source 2 engine

- Deadlock/Source 2 server requires **`-netconport <port>`** launch arg to open a TCP RCON listener; `-usercon` + `+rcon_password` alone are not sufficient. The fix added `-netconport ${SERVER_PORT}` to `SERVER_ARGS` in `docker/entrypoint.sh:130`, sharing the game UDP port number with RCON TCP.
- Full working Deadlock server launch args (`docker/entrypoint.sh:130`): `-dedicated -console -usercon -condebug +ip 0.0.0.0 -port <N> -netconport <N> -allow_no_lobby_connect -game citadel +map <map>`. Map defaults to `dl_midtown`.
- Source 2 RCON protocol differs from Source 1: Source 1 sends two packets on auth (empty `RESPONSE_VALUE` + `AUTH_RESPONSE`), Source 2 (Deadlock) sends only a single `AUTH_RESPONSE`. Client handles both (`crates/rcon/src/connection.rs:57-70`).
- RCON packet structure: `size(i32) + request_id(i32) + packet_type(i32) + body + 2 null terminators`. Packet type constants: `AUTH=3, AUTH_RESPONSE=2, EXECCOMMAND=2, RESPONSE_VALUE=0`. Max packet size 4096 bytes (`crates/rcon/src/packet.rs:5-13`).
- Multi-packet RCON responses terminated via sentinel pattern: send the real command, then send an empty-body `EXECCOMMAND` with a new request_id; the server echoes back the sentinel id, at which point reading stops and all bodies are concatenated (`crates/rcon/src/connection.rs:96-117`).
- Source 2 engine expects CWD to be the game root at launch; entrypoint does `cd ${INSTALL_DIR}/game/bin/win64` before invoking `proton run ./deadlock.exe` (`docker/entrypoint.sh:149`).
- `steam_appid.txt` must be written to both `game/bin/win64/` and `game/citadel/` so the engine finds AppID 1422450 without a running Steam client (`docker/entrypoint.sh:125-128`).

## Deadlock game systems

- Deadlock Steam AppID is `1422450`; `-game citadel` is the mod directory (not "deadlock").
- `SERVER_MAP` default is `dl_midtown`.
- Pause-injector DLL is deployed via Wine `version.dll` proxy hijack: built with MinGW for `x86_64-pc-windows-gnu`, copied into `${INSTALL_DIR}/game/bin/win64/version.dll`, and activated by `WINEDLLOVERRIDES=version=n,b` so the PE loader picks up the proxy first. It forwards real version.dll calls to system32 and spawns a pause control listener on TCP :27050 (`docker/entrypoint.sh:108-121`).
- Pause injector exposed to host via compose port `${PAUSE_INJECTOR_PORT:-27050}:27050/tcp` (`docker-compose.yml:9`).

## Deadworks runtime

- `server-manager` is a Rust/axum HTTP service (port 3000) with two routes: `POST /rcon` (arbitrary command pass-through) and `GET /status` (runs `status_json`, parses as JSON, falls back to `{raw: ...}` on parse failure) — `crates/server-manager/src/main.rs:52-55`, `crates/server-manager/src/api.rs:20-42`.
- RCON errors bubble back to HTTP as `502 Bad Gateway` with `{error: <Display>}` body (`crates/server-manager/src/api.rs:44-48`).
- `RconClient` uses lazy connect + persistent-with-retry pooling pattern (`crates/rcon/src/client.rs:9-43`): single `Mutex<Option<RconConnection>>`, no connect at startup; first call connects and stores the conn; on any error (IO/auth/timeout/invalid) the connection is dropped and immediately recreated once. No second retry — if reconnect-exec also fails, error returned to caller.
- Timeouts are hardcoded: `CONNECT_TIMEOUT=5s`, `IO_TIMEOUT=10s` (`crates/rcon/src/connection.rs:12-13`).
- `next_request_id` wraps on overflow and resets to 1 (never 0 or negative, which would collide with `AUTH_FAILED_ID=-1`) — `crates/rcon/src/connection.rs:122-129`.
- `server-manager` configured entirely via CLI/env: `--rcon-host` (default `127.0.0.1`), `--rcon-port` (default `27015`), `RCON_PASSWORD` env; compose sets `RCON_HOST=deadlock` to hit the game container by service name (`docker-compose.yml:37-41`).

## Plugin build & deployment

- `.dockerignore` excluding `Cargo.toml`/`Cargo.lock`/`src/` breaks the server-manager Dockerfile which needs those at context root — symptom is `failed to compute cache key: ... "/Cargo.toml": not found`. Cleaned-up `.dockerignore` should only contain `.git .idea target/ server/` (lines 2-5).
- The deadlock Dockerfile (`docker/Dockerfile`) has a dll-builder stage that does `COPY Cargo.toml Cargo.lock ./` and `COPY crates ./crates`, so its build context must be the repo root, not `./docker`. Fix: `docker-compose.yml` deadlock service uses `context: .` + `dockerfile: docker/Dockerfile`, and Dockerfile `COPY` paths become `docker/entrypoint.sh` / `docker/healthcheck.sh` (Dockerfile:64-65).
- pause-injector is cross-compiled Linux->Windows via `rustup target add x86_64-pc-windows-gnu` + `gcc-mingw-w64-x86-64`, with `.cargo/config.toml` setting `linker = "x86_64-w64-mingw32-gcc"` generated inline (`docker/Dockerfile:1-17`). Output copied from build stage: `deadlock_pause_injector.dll` → `/opt/pause-injector/version.dll`.
- Runtime base is `cm2network/steamcmd:root` plus extensive i386 multiarch packages (vulkan, x11, gnutls, freetype, etc.) for Wine/Proton — `dpkg --add-architecture i386` is required (`docker/Dockerfile:23`).
- Proton is GE-Proton (default `GE-Proton9-5`), downloaded from GloriousEggroll GitHub release on first run into `/opt/proton`, then symlinked into `${STEAM_PATH}/compatibilitytools.d/` (`docker/entrypoint.sh:20-28`).
- SteamCMD must force Windows platform to download the Deadlock Windows binaries on Linux: `+@sSteamCmdForcePlatformType windows` + `+app_update 1422450` (`docker/entrypoint.sh:38-43`). Wrapped in 3-retry loop.
- Windows `steamclient64.dll`/`steamclient.dll` fetched via a separate anonymous SteamCMD `+app_update 1007` in Windows mode, then copied to three locations: pfx `drive_c/Program Files (x86)/Steam`, the game `bin/win64`, and pfx `drive_c/windows/system32` — the last is for Wine's DLL search path (`docker/entrypoint.sh:73-89`).
- Xvfb on `:99` is started before Proton because the engine still needs a display even in dedicated mode (`docker/entrypoint.sh:98-99`).
- Compose port conflict gotcha: at one point both `server-manager` and `hltv-relay` were mapped to host `3000:3000`; hltv-relay was later moved to `8080:3000` (`docker-compose.yml:55`).
- `server-manager` compose service has `depends_on: deadlock { condition: service_healthy }` — the healthcheck passing only means the container is up, not that RCON is accepting connections, so first requests can still `Connection refused` until the game finishes map load.
