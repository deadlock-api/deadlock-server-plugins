---
date: 2026-04-21
task: session extract — custom-server 3f49d607
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-custom-server/3f49d607-f9e3-4c53-b852-c0eb5be86d90.jsonl]
---

## Source 2 engine

- Source 2 has **two independent spectating mechanisms** that run simultaneously, not alternatives:
  - Traditional SourceTV: `tv_enable 1` creates an in-process "master server" (a spectator bot) that buffers all game data and listens on `tv_port` (UDP, default 27020, or game-port+10 in CS2) for direct `connect <ip>:<tv_port>` clients. Capacity capped by `tv_maxclients` / `tv_maxrate`.
  - GOTV+/Broadcast: `tv_broadcast 1` is an **extension layered on top of** `tv_enable`, not a replacement. The engine chops the SourceTV buffer into ~3-second HTTP fragments and POSTs them to `tv_broadcast_url`; the relay then serves fragments to viewers over HTTP (CDN-friendly, unbounded viewer count).
- Consequence: the SourceTV master must be running for broadcast mode to have anything to fragment, so the engine **always** binds the UDP listener on `tv_port` when `tv_enable 1` is set, even in pure broadcast setups. Setting only `+tv_broadcast 1` without `+tv_enable 1` would not work.
- To suppress direct connections while keeping broadcast, set `+tv_maxclients 0` (used in the final fix). The UDP socket is still bound internally, but no client can complete a connection. Firewalling the port externally (i.e. not mapping it in Docker) has the same effect.
- `+tv_broadcast_origin_auth` is the launch flag that passes the shared key the relay expects (see `entrypoint.sh:132` — gated on `TV_BROADCAST_AUTH` being set).

## Deadworks runtime

- None in this session.

## Plugin build & deployment

- `deadlock-custom-server` Docker topology (`docker-compose.yml`): `deadlock` service (game server in Proton) `depends_on: hltv-relay (service_healthy)`; relay is `ghcr.io/deadlock-api/hltv-relay:latest` on port 3000 with `HLTV_RELAY_AUTH_MODE=key` + `HLTV_RELAY_AUTH_KEY=${TV_BROADCAST_AUTH}`. Game server pushes to `http://broadcast-relay:3000/` (compose service DNS), matched by `+tv_broadcast_origin_auth`.
- `docker/entrypoint.sh` launch pattern for TV (after fix): `+tv_enable 1 +tv_broadcast 1 +tv_maxclients 0 +tv_delay ${TV_DELAY} +tv_broadcast_url ${TV_BROADCAST_URL} [+tv_broadcast_origin_auth ${TV_BROADCAST_AUTH}]`. `TV_PORT` variable and both docker-compose port mappings were removed — nothing external needs that port when relay is the only viewer path.
- Server launches under Proton with Xvfb `:99` (640x480x24), `WINEDLLOVERRIDES="version=n,b"` plus `steamclient=n;steamclient64=n`, and requires CWD = `${INSTALL_DIR}/game/bin/win64` (Source 2 expects the game-root as CWD — `entrypoint.sh:137`).
- `steam_appid.txt` with app id `1422450` must be written to **both** `game/bin/win64/` and `game/citadel/` so the engine locates the app id without a running Steam client (`entrypoint.sh:114-115`).
- Windows `steamclient64.dll` / `steamclient.dll` are fetched via a second `steamcmd +@sSteamCmdForcePlatformType windows +app_update 1007 validate` (Steamworks SDK Redist, app 1007) and copied into three locations inside the prefix: `drive_c/Program Files (x86)/Steam/`, `game/bin/win64/`, and `drive_c/windows/system32/` to satisfy every DLL search path (`entrypoint.sh:75-90`).
- SteamCMD game-file download is wrapped in a 3-attempt retry loop with `sleep 5` between attempts (`entrypoint.sh:36-47`), and existence of `game/bin/win64/deadlock.exe` is the canonical success check afterwards.
