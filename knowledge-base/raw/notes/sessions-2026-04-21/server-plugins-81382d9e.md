---
date: 2026-04-21
task: session extract — server-plugins 81382d9e
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/81382d9e-ead7-4eac-86fa-81369e7e2fd5.jsonl]
---

Session context: Rust workspace variant of this repo (crates/server-manager, crates/rcon). Task was to implement RCON in the `server-manager` crate based on `example-rcon-implementation/`. Ended after pivoting from binary RCON to text netcon when the binary parser failed against the live Deadlock server.

## Source 2 engine

- Source 2 `-netconport` opens a **plain-text, newline-delimited TCP console**, NOT the binary Source 1 RCON protocol. Initial assistant assumption (binary RCON) was wrong; live test produced raw text like `SV: } IGameSystem::LoopActivateAllSystems done` as the first bytes, which the binary packet parser rejected.
- Netcon is **unauthenticated** — no password handshake. The `rcon_password` cvar applies to the classic Source RCON path, not to `-netconport`. Password was removed from both the library and the `server-manager rcon` subcommand signature after this realization (session line 438, 448, 450).
- If one were to implement binary Source RCON: Source 1 auth replies with two packets (empty `SERVERDATA_RESPONSE_VALUE` then `SERVERDATA_AUTH_RESPONSE`); Source 2 replies with only the single `SERVERDATA_AUTH_RESPONSE`. See `example-rcon-implementation/src/connection.rs:57-70` for the branch handling both.
- Source RCON binary packet layout (example-rcon-implementation/src/packet.rs:5-13): 4B size + 4B request_id + 4B packet_type + body + 2 null terminators; `MAX_PACKET_SIZE = 4096`; `SERVERDATA_EXECCOMMAND` and `SERVERDATA_AUTH_RESPONSE` both equal `2` (distinguished by context).
- Source RCON multi-packet responses have no length hint; the implementation sends an empty sentinel EXEC packet right after the real command and reads until a reply with the sentinel's request_id arrives (`connection.rs:96-117`).

## Deadlock game systems

- Deadlock AppID is `1422450` (entrypoint.sh:3).
- Server-side `status` RCON output contains keywords like `hostname|map|version|players|udp` — used as the test assertion signal that RCON is reaching the game.
- Default server port is 27015 TCP+UDP; docker-compose maps host 27016 → container 27015 on both protocols.

## Deadworks runtime

- Dedicated server is Windows-only; entrypoint.sh runs it through Proton GE-Custom (default `GE-Proton10-33`) under `gosu steam`. Proton tarball is downloaded from GloriousEggroll's GitHub release and symlinked into `compatibilitytools.d/`.
- Server launch args (entrypoint.sh:149): `-dedicated -console -condebug -insecure +ip 0.0.0.0 -port $PORT -netconport $PORT -allow_no_lobby_connect -game citadel +map $MAP`. `-netconport` and `-port` are reused (both 27015) — a single TCP port serves game + netcon.
- `-condebug` writes console output to `console.log` on disk.

## Plugin build & deployment

- Rust workspace exists alongside the C# plugins: `crates/server-manager/` (tokio binary with `main.rs`, `cli.rs`, `daemon.rs`, `protocol.rs`) and a separate `crates/rcon/` library crate. `server-manager/Cargo.toml` pulls tokio features `rt-multi-thread, net, io-util, sync, macros, time` plus `serde`, `tracing-subscriber`, `uuid v4`.
- Example RCON crate (`example-rcon-implementation/Cargo.toml`) is minimal — only `tokio`, `thiserror`, `tracing` (all `workspace = true`), confirming the workspace exposes those as shared deps.
- Integration tests run via `docker-compose.test.yaml` with an `--abort-on-container-exit test-rcon` service; test script polls `nc -z 127.0.0.1 27015` up to `TEST_TIMEOUT=120s` before exercising RCON. Passed in ~40s in the session run.
- `server-manager` binary is installed into the container at `/opt/plugins/server-manager` (tests/test-rcon.sh:15). Injector log at `/tmp/injector.log` is dumped on test failure.
- Session wired RCON password through `.env` loaded by both `mise` and `docker compose` (user turn line 177) before later discovering the password is unused for netcon.
