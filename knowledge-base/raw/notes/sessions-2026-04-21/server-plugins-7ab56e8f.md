---
date: 2026-04-21
task: session extract — server-plugins 7ab56e8f
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/7ab56e8f-30fd-4693-bf48-a3eeaa2d8cc9.jsonl]
---

Note: session dated 2026-03-24 on `master` branch — predates the current C# Deadworks plugin repo. Captures an earlier Rust-based DLL-injection plugin system (workspace `plugins/entity-plugin`, `crates/plugin-sdk`, `crates/injector`, etc.).

## Source 2 engine
- Target process is `deadlock.exe` located at `/home/steam/server/game/bin/win64/deadlock.exe` (entrypoint.sh:43) — Windows x64 binary launched under Proton/Wine on Linux.
- Plugin DLLs are built as `x86_64-pc-windows-gnu` with `x86_64-w64-mingw32-gcc` linker (`.cargo/config.toml:1-5`); injected into the running engine process.

## Deadlock game systems
- Steam App ID for Deadlock dedicated server is `1422450` (entrypoint.sh:3); compat prefix at `steamapps/compatdata/1422450/pfx`.
- Default map `dl_midtown`, default port `27015` (entrypoint.sh:6-7).
- SteamCMD must run with `+@sSteamCmdForcePlatformType windows` to fetch the Windows build on Linux (entrypoint.sh:34, :55). Dedicated-server download retried up to 3 times (entrypoint.sh:30-41).
- Separate download of Windows Steam client DLLs (AppID `1007`, anonymous login) into `/home/steam/steam_client` for `steamclient64.dll` (entrypoint.sh:50-59) — required by the Windows game binary running under Wine.

## Deadworks runtime
- Plugin↔host IPC is newline-delimited JSON over TCP, not a file/pipe (`crates/plugin-common/src/socket.rs:41-44`). Protocol:
  - plugin→server handshake: `{"type":"register","plugin":"...","version":"..."}`
  - server→plugin: `{"id":"...","data":{...}}`
  - plugin→server response: `{"type":"response","id":"...","data":{...}}`
- The entity plugin (inside Wine) connects OUT as a TCP client to host `server-manager` on `0.0.0.0:9100` (daemon.rs:12). There was no `server-manager` binary in the repo before this session — session built it.
- `server-manager` daemon binds two sockets: `:9100` for plugin connections, `:9101` for CLI control (daemon.rs:12-13). `COMMAND_TIMEOUT = 10s` (daemon.rs:14).
- Plugin registry keyed by plugin name string; each `PluginHandle` holds an mpsc command tx and a pending-requests map (`Arc<Mutex<HashMap<String, oneshot::Sender<serde_json::Value>>>>`) for correlating responses by id (daemon.rs:16-23).
- Gotcha: protocol structs for CLI side must derive both `Serialize` (request) and `Deserialize` (response) — initial build failed on `CliRequest: Serialize` missing (line 298 build error; fixed line 299).
- Existing test scripts under `tests/test-entity-*.sh` use a file-based command mechanism that does NOT match the current TCP protocol in the entity plugin (assistant note, line 227). Mismatch was left unresolved.

## Plugin build & deployment
- Cargo workspace resolver `"3"`, edition `2024` (`Cargo.toml:2,14`). Members include `crates/plugin-sdk`, `crates/plugin-common`, `crates/plugin-loader`, `crates/injector`, `plugins/entity-plugin`, `tests/test-plugin`, `tests/test-harness`.
- Shared deps pin `windows-sys = 0.61.2` with a large feature set for Win32 process/memory/toolhelp/debug APIs (`Cargo.toml:25`) — used by injector for `CreateRemoteThread`/`LoadLibraryA`-style injection.
- Dockerfile is multi-stage: `plugin-builder` (rust + mingw) cross-compiles DLLs for `x86_64-pc-windows-gnu`, then base stage `cm2network/steamcmd:root` adds i386 architecture and installs Wine/Proton/Source 2 32- and 64-bit deps (freetype, vulkan, X11, GL, SDL2, gnutls, nss, fontconfig, gtk3) (Dockerfile:1-45).
- Artifacts copied from builder: `target/x86_64-pc-windows-gnu/release/*.dll` and `*.exe` into `/opt/plugins/` (Dockerfile:53).
- Proton source: GloriousEggroll release tarball from GitHub, default `GE-Proton10-33` (entrypoint.sh:10, :18-22), symlinked into `${STEAM_PATH}/compatibilitytools.d/`. Cached in named docker volume `proton-10-33`.
- Wine prefix initialised using Proton's bundled `wine64` at `${PROTON_DIR}/files/bin/wine64` (entrypoint.sh:63). Xvfb `:99` at 640x480x24 required for Wine init (entrypoint.sh:66).
- Test infra: `docker-compose.test.yaml` runs each integration test as its own service; entrypoint.sh backgrounded, test script runs, server PID killed on exit (docker-compose.test.yaml:20-28). Shares `proton-10-33` + `gamedata` volumes; mounts `/etc/machine-id` read-only (steam requirement).
- Session added `server-manager` as a third build target: new Dockerfile stage `manager-builder` builds a Linux-native binary (in parallel with plugin-builder), copied to `/opt/plugins/server-manager`; daemon started from `entrypoint.sh` before the game server; `rust-toolchain.toml` gained `x86_64-unknown-linux-gnu` target alongside the windows-gnu one.
- Mise tasks added: `mise entity:list`, `mise entity:health <index> <value>`, `mise entity:set <index> <x> <y> <z>` — all route via `docker exec` into the running container invoking `server-manager send <plugin> '<json>'` CLI mode (port `9101` internal, no host port exposure needed).
