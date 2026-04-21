---
date: 2026-04-21
task: session extract — custom-server 2c3ccbd4
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-custom-server/2c3ccbd4-a6fd-4801-ae63-0fb984482128.jsonl]
---

Session context: `/init` run against sibling repo `deadlock-custom-server` (Rust workspace, not this plugins repo). Generated `CLAUDE.md` at `/home/manuel/deadlock/deadlock-custom-server/CLAUDE.md`. Findings below are about the Deadlock dedicated-server/RCON side that hosts the deadworks runtime.

## Source 2 engine

- RCON auth handshake differs between Source 1 and Source 2: Source 1 sends an empty `SERVERDATA_RESPONSE_VALUE` (type 0) packet followed by a `SERVERDATA_AUTH_RESPONSE` (type 2); Source 2/Deadlock sends only the single `SERVERDATA_AUTH_RESPONSE`. Client must accept both by peeking the first packet's type. See `crates/rcon/src/connection.rs:57-70` in the custom-server repo.
- RCON packet wire format confirmed: little-endian i32 size/request_id/packet_type, body (UTF-8), then two null terminators. `MIN_BODY_SIZE = 10`, `MAX_PACKET_SIZE = 4096`. Types: `AUTH=3`, `EXECCOMMAND=2`, `AUTH_RESPONSE=2`, `RESPONSE_VALUE=0` (note type `2` is overloaded across send/recv directions). `packet.rs:5-13`.
- Multi-packet command responses terminated by sentinel trick: after sending the real command, send a second empty `EXECCOMMAND` and read packets until the server echoes back the sentinel's request_id. `connection.rs:96-117`.
- Auth-failed signalled by `request_id == -1` in the AUTH_RESPONSE (`AUTH_FAILED_ID = -1`). `connection.rs:14,79`.

## Deadlock game systems

- Deadlock Steam app ID: **1422450**. Same ID is used for both SteamCMD install and Proton `compatdata/1422450`. `docker/entrypoint.sh:17,42,113-114`.
- Default map slug: `dl_midtown`. Game directory name under install: `game/citadel`. `.env.example:6`, `entrypoint.sh:114`.
- Dedicated-server launch args (Source 2 / Deadlock): `-dedicated -console -usercon -condebug +ip 0.0.0.0 -port <p> -allow_no_lobby_connect -game citadel +map <m>` — `-allow_no_lobby_connect` is the flag that lets the server run without a Steam lobby. Optional: `+sv_password`, `+rcon_password`, GOTV flags `+tv_enable 1 +tv_broadcast 1 +tv_maxclients 0 +tv_delay N +tv_broadcast_url <url> +tv_broadcast_origin_auth <key>`. `entrypoint.sh:117-132`.
- Source 2 expects CWD to be the game root (`game/bin/win64`) when launching; `steam_appid.txt` with `1422450` is written to both `game/bin/win64/` and `game/citadel/` so the engine finds the app ID without a running Steam client. `entrypoint.sh:112-115,135-136`.
- GOTV broadcast target in this stack is `ghcr.io/deadlock-api/hltv-relay` with Redis storage; relay auth mode `key` using `TV_BROADCAST_AUTH` env. `docker-compose.yml:30-53`.
- `status_json` RCON command returns JSON-parseable server status (used by `/status` HTTP endpoint). `crates/server-manager/src/api.rs:35`.
- `docs/rcon-commands.md` in the custom-server repo has a 506-line table of Deadlock RCON commands with flags (`sv`, `cheat`, `release`, `vconsole_fuzzy`, `norecord`). Notable `citadel_*` commands: `citadel_activate_cps_for_team`, `citadel_assume_pawn_control`, `citadel_create_unit [hero_name]` (flagged `sv, vconsole_fuzzy`), `citadel_boss_tier_3_testing_reset`, `citadel_bot_give_team_gold`, `citadel_disable_*`/`enable_*` toggles for duplicate heroes, fast cooldowns, fast stamina, no-hero-death, unlimited ammo. `docs/rcon-commands.md:35-50`.

## Deadworks runtime

- _No findings — session did not touch the deadworks plugin runtime._

## Plugin build & deployment

- Docker base for Deadlock dedicated server is `cm2network/steamcmd:root` with `dpkg --add-architecture i386` and a long list of 32-bit + 64-bit Wine runtime deps (freetype, vulkan, x11, xrandr, xcomposite, xcursor, xi, xfixes, xrender, gl, glib, dbus, fontconfig, nss, gtk3, sdl2, gnutls). `docker/Dockerfile:1-34`.
- Proton variant pinned via env `PROTON_VERSION` (default `GE-Proton9-5` in the script, `GE-Proton10-33` in `.env.example` — script default is stale). Downloaded from GitHub `GloriousEggroll/proton-ge-custom` releases into `/opt/proton`, symlinked into `~/.steam/steam/compatibilitytools.d/`. `entrypoint.sh:14,20-28`; `.env.example:11`.
- SteamCMD must be run with `+@sSteamCmdForcePlatformType windows` so it downloads the Windows build of app 1422450 (the dedicated server is the Windows binary run under Proton, not a native Linux binary). `entrypoint.sh:39`.
- Windows `steamclient64.dll`/`steamclient.dll` are fetched separately via an anonymous install of Steamworks SDK Redist (app 1007 validate) and manually copied into three locations the game searches: `pfx/drive_c/Program Files (x86)/Steam/`, `game/bin/win64/` alongside the exe, and `pfx/drive_c/windows/system32/`. `entrypoint.sh:56-89`.
- `WINEDLLOVERRIDES='steamclient=n;steamclient64=n'` is set at launch so Wine prefers the native (copied) DLLs over built-in stubs. `entrypoint.sh:148`.
- Virtual display required: `Xvfb :99 -screen 0 640x480x24` started before Proton invocation. `entrypoint.sh:98-100`.
- Docker volumes: `./server:/home/steam/server` (game files, host-mounted so SteamCMD caches persist), named volumes `proton` (Proton install), `compatdata` (Wine prefix), `redis` (relay data). Host `/etc/machine-id` is bind-mounted read-only (Steam needs it). `docker-compose.yml:8-12`.
- Healthcheck starts 120s after container boot (game takes that long to initialize under Proton). `docker-compose.yml:22`.
- `deadlock-server-manager` HTTP API (Axum) exposes `POST /rcon` and `GET /status`, wraps the auto-reconnecting `RconClient` in an `Arc<AppState>`. `crates/server-manager/src/main.rs:52-55`, `api.rs:20-42`.
