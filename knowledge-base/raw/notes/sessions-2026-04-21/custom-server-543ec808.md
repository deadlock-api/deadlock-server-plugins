---
date: 2026-04-21
task: session extract — custom-server 543ec808
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-custom-server/543ec808-d623-440e-a8ed-b11d99bae195.jsonl]
---

## Deadlock game systems

- Deadlock dedicated server `status_json` RCON command returns structured JSON; the server-manager API exposes it at `GET /status` and falls back to a `{ "raw": <text> }` wrapper if parsing fails (api.rs:35-39).
- Default dedicated-server RCON port matches the game port (`SERVER_PORT=27015`); server-manager CLI defaults `--rcon-port` to `27015` (main.rs:30).

## Deadworks runtime

- `deadlock-custom-server` repo is a sibling/external tool, NOT the Deadworks plugin runtime. It is a Rust Cargo workspace (`crates/rcon`, `crates/server-manager`) providing an out-of-process HTTP -> RCON bridge (main.rs:52-55, workspace root Cargo.toml:2-5).
- Server-manager axum routes: `POST /rcon` (exec arbitrary command via `RconClient::exec`) and `GET /status` (status_json) — see api.rs:20-42. State is `Arc<AppState { rcon: RconClient }>`.

## Plugin build & deployment

- Deadlock dedicated server container is based on `cm2network/steamcmd:root` with i386 multiarch enabled and full Proton/Wine runtime libs installed for Source 2 (docker/Dockerfile:1-34). Proton version pinned via `PROTON_VERSION=GE-Proton10-33` in .env.
- Steam compat prefix dir is pre-created at `/home/steam/.steam/steam/steamapps/compatdata/1422450/pfx` — appid `1422450` is Deadlock (docker/Dockerfile:38).
- Host compose mounts: `./server:/home/steam/server`, named volumes `proton:/opt/proton`, `compatdata:/home/steam/.steam/steam/steamapps/compatdata`, and bind `/etc/machine-id:ro` (needed by Proton/Wine for consistent machine identity) — docker-compose.yml:8-12.
- HLTV pipeline in compose: game server sets `TV_ENABLE=1`, `TV_DELAY=0`, `TV_BROADCAST_URL=http://hltv-relay:3000/`; relay is `ghcr.io/deadlock-api/hltv-relay:latest` backed by redis, auth via shared `TV_BROADCAST_AUTH` key (docker-compose.yml:14-53).
- Game container healthcheck uses `start_period: 120s` with 60s interval — Source 2 + Proton cold boot is slow enough that dependent services must `depends_on: { condition: service_healthy }` rather than start order alone (docker-compose.yml:18-23).
- Server-manager added as compose service with `build.context: .` (workspace root) and `dockerfile: crates/server-manager/Dockerfile` — necessary because the multi-stage Rust build needs the workspace `Cargo.toml` + `Cargo.lock` + all `crates/` to resolve the path dep `deadlock-rcon = { path = "../rcon" }` (Cargo.toml:7).
- Multi-stage Dockerfile: `rust:1-slim` builder -> `debian:bookworm-slim` runtime with `ca-certificates` only (crates/server-manager/Dockerfile). Inter-container RCON uses Docker DNS hostname `deadlock` (compose service name) for `--rcon-host`.
