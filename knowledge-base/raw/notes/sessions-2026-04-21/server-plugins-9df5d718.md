---
date: 2026-04-21
task: session extract — server-plugins 9df5d718
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/9df5d718-aabd-4b94-b748-de5e86a972ec.jsonl]
---

Session was `/simplify` run against a pre-plugins version of the repo (branch `master`) when it contained only `Dockerfile` / `entrypoint.sh` / `docker-compose.yaml` — no C# plugins yet. Three parallel review agents surfaced infrastructure facts worth keeping.

## Source 2 engine

- Deadlock Source 2 server **must be launched with CWD = game root**; comment at `entrypoint.sh:117` notes "Source 2 expects CWD to be the game root". The double `cd` (outer shell `entrypoint.sh:118`, inner `gosu` subshell `entrypoint.sh:132`) exists because `gosu` does not inherit the outer shell's working directory — only the inner `cd` actually takes effect.
- Dedicated server launch args baseline at `entrypoint.sh:111`: `-dedicated -console -condebug +ip 0.0.0.0 -port ${SERVER_PORT} -netconport ${SERVER_PORT} -allow_no_lobby_connect -game citadel +map ${SERVER_MAP}`. `-game citadel` is the subdir name; `-allow_no_lobby_connect` lets clients join without a Steam lobby. `+sv_password` is optionally appended at `entrypoint.sh:114`.
- Engine looks for `steam_appid.txt` in two locations (written at `entrypoint.sh:107-108`): `game/bin/win64/steam_appid.txt` AND `game/citadel/steam_appid.txt`. Needed so the engine finds the app ID without a running Steam client.
- `condebug` causes the engine to write `console.log` at `${INSTALL_DIR}/game/citadel/console.log` (`entrypoint.sh:145`) — primary post-mortem log source.

## Deadlock game systems

- Steam app id for Deadlock dedicated server is **1422450**; hardcoded 6+ times across `entrypoint.sh:12,37,107,108,109,124,125` and `Dockerfile:38`. Also exported as both `SteamAppId` and `SteamGameId` inside the `gosu` subshell (`entrypoint.sh:124-125`) — both are required.
- Default map is `dl_midtown` (`entrypoint.sh:6`).

## Deadworks runtime

- Server runs under **GE-Proton (GloriousEggroll fork)**, default `GE-Proton9-5` (`entrypoint.sh:9`), downloaded from `github.com/GloriousEggroll/proton-ge-custom/releases` tarball at `entrypoint.sh:17`, extracted into `/opt/proton`.
- Proton needs `steamclient64.dll` + `steamclient.dll` in **three locations** (`entrypoint.sh:72-84`): wine prefix `drive_c/Program Files (x86)/Steam/`, game `bin/win64/`, and `drive_c/windows/system32/` — the last so Wine finds them on the DLL search path. Source is `steamapps/common/Steamworks SDK Redist` (populated by anonymous `+app_update 1007 validate` at `entrypoint.sh:56-61`).
- `WINEDLLOVERRIDES='steamclient=n;steamclient64=n'` (`entrypoint.sh:130`) forces Wine to use the native (copied-in) DLLs instead of builtin stubs.
- Xvfb on `DISPLAY=:99` (`entrypoint.sh:93-94`) is required even for dedicated server — Proton/Wine refuses to start without an X display. Current code uses arbitrary `sleep 2` instead of `xdpyinfo` readiness poll.
- `/etc/machine-id` is bind-mounted read-only (`docker-compose.yaml:8`) — Steam/Proton uses it for identity.
- `WINEDEBUG` conflicts between outer shell (`err+all,warn+module,trace+seh` line 102) and inner gosu subshell (`err+all,warn+module` line 129); only inner one takes effect because `gosu` drops env. Reviewer called `trace+seh` a likely "unresolved debugging artifact".
- SteamCMD is invoked with `+@sSteamCmdForcePlatformType windows` (`entrypoint.sh:34,57`) — server is the Windows build run under Proton, not a native Linux build.
- Blanket `chown -R steam:steam $INSTALL_DIR /home/steam/.steam $PROTON_DIR $COMPAT_DATA` at `entrypoint.sh:87` runs on **every container start** — extremely expensive on multi-GB game tree. Only DLL copies + `steam_appid.txt` actually need re-chown.

## Plugin build & deployment

- `compatdata` is a **named volume** (`docker-compose.yaml:11`) mounted at `/home/steam/.steam/steam/steamapps/compatdata` — shadows the Dockerfile pre-created `compatdata/1422450/pfx` tree (`Dockerfile:37-40`), so that build-time `mkdir -p` is effectively wasted. Same for `/opt/proton` volume shadowing its pre-created dir.
- Game install dir is a **host bind mount** `./server:/home/steam/server` (`docker-compose.yaml:9`) — `app_update 1422450` runs against it on every restart, performing full manifest validation.
- `${SERVER_PORT:-27015}` is used in `docker-compose.yaml:5-6` port mapping but **not** forwarded as a container env var — latent misconfig where host port mapping and in-container `SERVER_PORT` can silently diverge. No `environment:` section, no `restart:` policy.
- Proton tarball is cached in the `proton` named volume (`docker-compose.yaml:10`); `ln -sfn` into `compatibilitytools.d` (`entrypoint.sh:23`) runs unconditionally even when cached.
- Dockerfile base is `cm2network/steamcmd:root`; requires `dpkg --add-architecture i386` + large list of 32-bit libs (freetype, vulkan, x11, gtk, gnutls, sdl2, etc.) for Wine/Proton. `gosu` is used (not `su`/`sudo`) to drop from root to `steam` user.
- The diff that triggered the `/simplify` run was a Dockerfile `COPY` path fix `docker/entrypoint.sh` → `entrypoint.sh` — the script lives at repo root, not in a `docker/` subdir.
