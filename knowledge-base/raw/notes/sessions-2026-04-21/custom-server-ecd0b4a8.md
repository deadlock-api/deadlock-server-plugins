---
date: 2026-04-21
task: session extract — custom-server ecd0b4a8
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-custom-server/ecd0b4a8-9402-4d48-9d95-542d634021d9.jsonl]
---

## Deadworks runtime

- Deadlock game server runs under Wine/Proton on Linux via `cm2network/steamcmd:root` base; Steam AppID `1422450` compatdata layout pre-created at `/home/steam/.steam/steam/steamapps/compatdata/1422450/pfx` (`docker/Dockerfile:65`).
- Pause injection = Windows DLL + Linux `LD_PRELOAD` shim pair: `deadlock_pause_injector.dll` (cross-compiled via `x86_64-pc-windows-gnu` target with `x86_64-w64-mingw32-gcc` linker) plus `injector_shim.so` built from `crates/pause-injector/shim/injector_shim.c` with `-ldl -lpthread`, both staged into `/opt/pause-injector/` (`docker/Dockerfile:2-21,60-61`).
- Runtime deps pull both 64-bit and `:i386` variants of freetype, vulkan, X11, GL, glib, dbus, fontconfig, nss, gnutls — required because Proton needs 32-bit ABI available even for 64-bit game (`docker/Dockerfile:27,38-56`). `dpkg --add-architecture i386` must precede apt install.
- Xvfb + gosu + cabextract included in runtime stage — suggests headless X for Source 2, drop-privileges pattern, and .cab extraction (likely Windows-side redist install into prefix).

## Plugin build & deployment

- Two separate Dockerfiles: main `docker/Dockerfile` (multi-stage; Rust cross-compile + runtime) and `crates/server-manager/Dockerfile` (simple 2-stage Rust → debian slim for the management binary).
- Both pinned to `rust:1.94-bookworm` / `rust:1.94-slim` / `debian:bookworm-slim` — session was about upgrading to Trixie (Debian 13), but task was interrupted before any edits landed.
- `deadlock-server-manager` binary built via `cargo build --release --package deadlock-server-manager` and dropped into `/usr/local/bin/` on `debian:bookworm-slim` with only `ca-certificates` runtime dep (`crates/server-manager/Dockerfile:7,9-14`).
