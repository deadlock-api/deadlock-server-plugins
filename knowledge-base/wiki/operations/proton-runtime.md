---
title: Proton runtime (Wine-hosted dedicated server)
type: operation
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/custom-server-2c3ccbd4.md
  - knowledge-base/raw/notes/sessions-2026-04-21/custom-server-3f49d607.md
  - knowledge-base/raw/notes/sessions-2026-04-21/custom-server-543ec808.md
  - knowledge-base/raw/notes/sessions-2026-04-21/custom-server-9a7f664c.md
  - knowledge-base/raw/notes/sessions-2026-04-21/custom-server-a6b83c6e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-328372c6.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-a54dc08d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-aabd306f.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-ddfface7.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-ecd0b4a8.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-7ab56e8f.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-93cfc73e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-9df5d718.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-d63499d3.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-6b481873.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-1b75db40.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-34752d6a.md
related:
  - "[[docker-build]]"
  - "[[plugin-build-pipeline]]"
  - "[[source-2-engine]]"
  - "[[deadlock-game]]"
created: 2026-04-21
updated: 2026-04-21
confidence: high
---

# Proton runtime

The Deadlock dedicated server is a **Windows PE64** running under
**Proton/Wine** on Linux. This page documents the Proton wrapper, Wine
prefix, Steam client DLL triple-copy, SteamCMD forced-platform-type,
and LD_PRELOAD shim patterns used by this stack.

## Why Proton?

- There is no native Linux Deadlock dedicated server binary. SteamCMD
  must be forced to download the Windows build (custom-server-2c3ccbd4,
  server-plugins-9df5d718):
  ```
  steamcmd +@sSteamCmdForcePlatformType windows +app_update 1422450
  ```
- Proton provides the `steamclient` bridging layer that plain Wine
  lacks. Bypassing `proton run` with raw `wine64` loses the
  `lsteamclient` bridge and breaks `SteamClient023` resolution
  (server-plugins-d63499d3).

## GE-Proton

Uses GloriousEggroll's fork, not Valve's release Proton
(server-plugins-9df5d718):

- Default pinned version: **`GE-Proton10-33`** in `.env.example`
  (deadworks-a54dc08d, deadworks-328372c6). Some older scripts still
  default to `GE-Proton9-5`, which is stale.
- Source: `github.com/GloriousEggroll/proton-ge-custom` releases,
  downloaded as tarball on first run into `/opt/proton` via
  `tar --strip-components=1`.
- Symlinked into `${STEAM_PATH}/compatibilitytools.d/` for Steam
  discovery.
- Cached in the `proton` named Docker volume so only first boot pays
  the download cost.
- Marker file `.proton_marker` (or `.proton_wine64_marker`) in the pfx
  dir gates one-shot prefix initialization (deadworks-ddfface7,
  server-plugins-d63499d3). **Changing `PROTON_VERSION` does NOT
  auto-reinit** the prefix (only the proton binary download check
  triggers) — likely gotcha requiring `docker compose down -v` after
  version bumps.

**Version compatibility notes** (server-plugins-d63499d3):

- `GE-Proton9-5` — lacks `SteamClient023`.
- `GE-Proton9-22` — had a patched builtin.
- `GE-Proton10-33` — removed builtin steamclient entirely; relies on
  the Python wrapper bridge.

## Wine prefix

Location: `/home/steam/.steam/steam/steamapps/compatdata/1422450/pfx`
(deadworks-ecd0b4a8, custom-server-2c3ccbd4).

- App id `1422450` is used for the compatdata path even though the
  dedicated-server app id can differ (custom-server-2c3ccbd4 / Rust-era
  notes used `1422460`; server-plugins-34752d6a).
- Pre-created at image build via `mkdir -p` but shadowed by the named
  volume mount at runtime — the build-time mkdir is effectively wasted
  (server-plugins-9df5d718).
- Must be `wineboot --init`'d **as the steam user**, not root +
  chown-after. Wrong ownership produces `wine: '...' is not owned by
  you` and `kernel32.dll status c0000135` (server-plugins-d63499d3).
- `compatdata` volume persists a poisoned prefix across rebuilds —
  `docker compose down -v` required after Proton version or prefix-init
  changes.

## Steam client DLL triple-copy

`steamclient64.dll` + `steamclient.dll` must land in **three locations**
for the Wine-hosted server to find Steam (custom-server-2c3ccbd4,
custom-server-9a7f664c, deadworks-328372c6, deadworks-ddfface7):

1. `pfx/drive_c/Program Files (x86)/Steam/`
2. `<install>/game/bin/win64/` (next to `deadworks.exe` / `deadlock.exe`)
3. `pfx/drive_c/windows/system32/`

All three are needed because Wine's DLL search path checks each. Source:
anonymous SteamCMD install of the **Steamworks SDK Redist** (app id
`1007`):

```
steamcmd +@sSteamCmdForcePlatformType windows +login anonymous \
         +app_update 1007 validate +quit
```

`WINEDLLOVERRIDES='steamclient=n;steamclient64=n'` at launch forces Wine
to use the native (copied) DLLs over built-in stubs
(server-plugins-9df5d718).

**Gotcha**: `WINEDLLOVERRIDES` set in the outer entrypoint shell is
**silently clobbered** by the inner `gosu steam bash -c` block — any
Wine override must be exported inside that inner shell
(custom-server-a6b83c6e, server-plugins-9df5d718). Same for `WINEDEBUG`.

## Required env exports inside the gosu subshell

- `SteamAppId=1422450` AND `SteamGameId=1422450` — both required
  (server-plugins-9df5d718).
- `WINEDLLOVERRIDES='steamclient=n;steamclient64=n'` (plus `version=n,b`
  for any DLL-proxy injection setups).
- `WINEPREFIX=/home/steam/.steam/steam/steamapps/compatdata/1422450/pfx`.
- `DISPLAY=:99`.
- For deadworks: `DOTNET_ROOT='C:\Program Files\dotnet'` (Windows-style
  path inside Proton; deadworks-ddfface7).

## `steam_appid.txt` dual-location

Written to **both** locations so the engine finds the app id without a
running Steam client (custom-server-2c3ccbd4, server-plugins-9df5d718,
custom-server-9a7f664c):

- `<install>/game/bin/win64/steam_appid.txt`
- `<install>/game/citadel/steam_appid.txt`

Contents: `1422450\n`.

## Xvfb virtual display

`Xvfb :99 -screen 0 640x480x24` started before any Wine/Proton
invocation (custom-server-2c3ccbd4, deadworks-a54dc08d). Proton/Wine
refuses to start without an X display — even for a headless dedicated
server. Current scripts use `sleep 2` instead of `xdpyinfo` readiness
polling (server-plugins-9df5d718).

## `/etc/machine-id` bind mount

`/etc/machine-id:/etc/machine-id:ro` must be bind-mounted read-only
(custom-server-2c3ccbd4, deadworks-328372c6). Steam/Proton reads it for
its compat sandbox identity; without a stable machine-id the prefix can
trigger fresh-device prompts or VAC-style rejections.

## CWD requirement

Source 2 expects CWD = game root (`<install>/game/bin/win64/`). Launch
path (server-plugins-9df5d718):

```bash
cd "${INSTALL_DIR}/game/bin/win64"    # outer shell
gosu steam bash -c "
  cd \"${INSTALL_DIR}/game/bin/win64\"  # inner shell — actually takes effect
  proton run ./deadworks.exe ...
"
```

Both `cd`s are needed because `gosu` drops the outer shell's working
directory.

## SteamCMD retry loop

`app_update` is wrapped in a 3-attempt retry loop with `sleep 5`
between attempts (custom-server-3f49d607, custom-server-9a7f664c):

```bash
for i in 1 2 3; do
  steamcmd ... +app_update 1422450 +quit && break
  sleep 5
done
test -f "${INSTALL_DIR}/game/bin/win64/deadlock.exe"   # canonical success check
```

## `.NET 10` runtime in the prefix (deadworks-specific)

Deadworks' managed layer boots via nethost → hostfxr → .NET 10. The
runtime is installed **inside the Wine prefix** as the Windows .NET
runtime, NOT the host's Linux dotnet (deadworks-a54dc08d,
deadworks-ddfface7, deadworks-328372c6):

1. Download `dotnet-runtime-${DOTNET_VERSION}-win-x64.zip` from
   `dotnetcli.azureedge.net/dotnet/Runtime/{ver}/...` into
   `/opt/dotnet-cache` (cached volume).
2. Unzip into `{pfx}/drive_c/Program Files/dotnet/`.
3. Gate via `.dotnet_${DOTNET_VERSION}_marker` so reinstall happens only
   on version change.
4. Export `DOTNET_ROOT='C:\Program Files\dotnet'` in the gosu subshell
   before `proton run ./deadworks.exe`.
5. Verify by checking `host/fxr/*/hostfxr.dll` presence.

Default `DOTNET_VERSION=10.0.0` (deadworks-328372c6).

## Launch flags (composite)

The full deadworks launch under Proton (deadworks-a54dc08d,
deadworks-ddfface7):

```
proton run ./deadworks.exe
  -dedicated -console -dev -insecure
  -allow_no_lobby_connect
  +hostport ${SERVER_PORT}
  +map ${SERVER_MAP}
  +tv_citadel_auto_record 0
  +spec_replay_enable 0
  +tv_enable 0
  +citadel_upload_replay_enabled 0
  ${DEADWORKS_ARGS}
```

`DEADWORKS_ARGS` env var is appended verbatim — escape hatch for extra
ConVars.

For `deadlock.exe` (non-deadworks) paths: add `-usercon`,
`-condebug`, `+ip 0.0.0.0`, `-port`, `-netconport` (same value as
`-port`).

## `-netconport` plain-text console

**NOT Source 1 binary RCON.** `-netconport` opens a plain-text
newline-delimited TCP console, unauthenticated (no password handshake).
`rcon_password` applies to the classic Source RCON path, not to
`-netconport`. First bytes on the wire look like raw text:
`SV: } IGameSystem::LoopActivateAllSystems done`
(server-plugins-81382d9e).

## LD_PRELOAD shim pattern (alternative injection)

From the sibling `deadlock-custom-server` Rust stack
(custom-server-a6b83c6e, custom-server-9cc2f451, custom-server-ecd0b4a8).
Separate from deadworks' DLL-replacement approach but worth knowing:

- Linux `.so` shim loaded via `LD_PRELOAD` on the outer Proton process.
- Shim `dlopen`s, waits for `kernel32.dll.so` to appear, then calls
  `LoadLibraryA` with a `Z:\...` UNC path to side-load a Windows DLL
  into Wine.
- `AppInit_DLLs` registry injection is **unreliable** with Proton's
  Wine — tested and failed (server-plugins-6b481873,
  custom-server-a6b83c6e).
- **`deadlock.exe` does NOT import `version.dll`.** Any DLL-proxy
  injection using `version.dll` silently never loads. Verified with
  `objdump -p` (custom-server-a6b83c6e).
- GE-Proton may rename or relocate Wine's `.so` modules, causing
  wait-for-kernel32 loops to hang.

## Wine boot noise (not crashes)

Normal `warn:module:load_dll` / `LdrGetProcedureAddress` lines for
`dbghelp.dll`, `vfbasics.dll`, `vrfcore.dll`, `psapi.dll`,
`vconcomm.dll`, `tier0.dll`, `iphlpapi.dll`, plus
`fixme:ntoskrnl:kernel_object_from_handle No constructor for type
"Token"` are Proton/Source 2 boot noise (server-plugins-90226db4). The
only meaningful line on failure is `exited with code 1 (restarting)` —
diagnose by reading the ~200 lines preceding the exit.

OOM signature: `err:virtual:allocate_virtual_memory out of memory for
allocation, base (nil) size 80000000` cascading down to `size
00100000`. Fires before the DLL-load warnings when those warnings
appear as a consequence, not the cause.

## `console.log` path

`condebug` writes `console.log` at `${INSTALL_DIR}/game/citadel/console.log`
— the **mod** directory, NOT `game/bin/win64/` (server-plugins-9df5d718,
deadworks-3beeff54). Tailing the wrong file is a common debugging
rathole (fork commits `5d663ad` "console.log path gotcha" and
`2cedee8` "fix: tail correct console.log path written by Source 2").

Upstream `55e81e9` "stream console log to stdout for docker compose
logs" pipes the correct file into `stdout` for `docker compose logs`.

## `chown` expense

`chown -R steam:steam $INSTALL_DIR /home/steam/.steam $PROTON_DIR $COMPAT_DATA`
at entrypoint runs on **every container start** — extremely expensive
on a multi-GB game tree (server-plugins-9df5d718). Only DLL copies +
`steam_appid.txt` actually need re-chown; blanket chown is latent tech
debt.

## HLTV / GOTV broadcast in Docker

`deadlock-custom-server` uses `ghcr.io/deadlock-api/hltv-relay` as the
broadcast relay, with Redis storage (custom-server-2c3ccbd4,
custom-server-3f49d607, custom-server-543ec808):

```
+tv_enable 1 +tv_broadcast 1 +tv_maxclients 0
+tv_delay ${TV_DELAY} +tv_broadcast_url ${TV_BROADCAST_URL}
+tv_broadcast_origin_auth ${TV_BROADCAST_AUTH}
```

See [[source-2-engine]] for the SourceTV-vs-broadcast layering
(`tv_enable` is always required; `tv_broadcast` is a layered
extension).

## `WINEFSYNC` matching

If Proton runs with `WINEFSYNC=1`, ancillary tools (e.g. injector) must
too — otherwise wineserver rejects with "Server is running with
WINEFSYNC but this process is not" (server-plugins-d63499d3).
