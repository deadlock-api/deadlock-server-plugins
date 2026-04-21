---
date: 2026-04-21
task: session extract — deadworks ddfface7
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/ddfface7-4467-4d56-b45b-dda69b1654b3.jsonl]
---

## Deadlock game systems

- Deadlock Steam app id is `1422450` (dedicated server), hard-coded in `docker/entrypoint.sh:7`.
- Default map when none specified is `dl_midtown` (`docker/entrypoint.sh:11`).
- Windows Steamworks SDK Redist DLLs (`steamclient64.dll`, `steamclient.dll`) are fetched via SteamCMD app `1007` (anonymous) and installed into three paths inside the Wine prefix: `Program Files (x86)/Steam/`, `game/bin/win64/` next to `deadworks.exe`, and `windows/system32/` (`docker/entrypoint.sh:67-113`). All three copies are needed for the Proton-hosted server to find Steam.
- Dedicated server launch disables replay/recording by default: `+tv_citadel_auto_record 0 +spec_replay_enable 0 +citadel_upload_replay_enabled 0`. These are always appended regardless of TV mode (`docker/entrypoint.sh:194-197` post-edit).
- HLTV/Source-TV on the Deadlock server is driven by the standard Source convars: `+tv_enable 1 +tv_broadcast 1 +tv_maxclients 0 +tv_delay <sec> +tv_broadcast_url <url> +tv_broadcast_origin_auth <key>`. `tv_maxclients 0` means only the broadcast upstream is used (no direct spectator slots).

## Deadworks runtime

- Deadworks replaces `deadlock.exe` in-place: the entrypoint overwrites the SteamCMD-installed `game/bin/win64/deadlock.exe` location by copying `deadworks.exe` + a `managed/` tree (with `managed/plugins/`) from `/opt/deadworks/game/bin/win64/` into the live install (`docker/entrypoint.sh:150-160`). So `deadworks.exe` and `deadlock.exe` coexist in `WIN64_DIR`; only the one named on the proton cmdline runs.
- A config file `game/citadel/cfg/deadworks_mem.jsonc` is deployed alongside (`docker/entrypoint.sh:162-163`) — implies deadworks reads a JSONC config at runtime for something memory-related (exact semantics not covered in session).
- Managed plugin layer layout: `WIN64_DIR/managed/` (assemblies/host) with a dedicated `WIN64_DIR/managed/plugins/` subdir for plugin DLLs (`docker/entrypoint.sh:158`).
- Deadworks is launched via Proton (GE-Proton10-33 default) with a .NET 10 Windows runtime installed into the Wine prefix at `drive_c/Program Files/dotnet`; `DOTNET_ROOT` is set to the Windows path `C:\Program Files\dotnet` inside the wrapper script (`docker/entrypoint.sh:212`). Presence verified by checking for `host/fxr/*/hostfxr.dll` (`docker/entrypoint.sh:136`).
- Default deadworks startup flags (matches `startup.cpp` per inline comment at `docker/entrypoint.sh:181`): `-dedicated -console -dev -insecure -allow_no_lobby_connect +hostport <port> +map <map>`. Note `-dev` and `-insecure` are in the defaults — this runtime is not intended as a secure/VAC production server in this harness.
- `DEADWORKS_ARGS` env var is appended verbatim to `deadworks.exe` cmdline, acting as an escape hatch for extra convars (`docker/entrypoint.sh:194-196`).

## Plugin build & deployment

- Docker build context assembles deadworks at `/opt/deadworks` inside the image; entrypoint copies from there into the gamedata volume each start (`docker/entrypoint.sh:152-160`). Plugin sources from outside the repo can be mounted via docker compose `additional_contexts: extra-plugins: ../my-deadworks-plugins` (commented template in `docker-compose.yaml:6-8`).
- Persistent named volumes: `proton` (Proton install cached across runs), `gamedata` (Deadlock server files from SteamCMD), `compatdata` (Wine prefix with .NET + markers), `dotnet-cache` (downloaded dotnet runtime zip) — all four survive restarts so only the first boot pays the download cost (`docker-compose.yaml:58-63`).
- `.proton_marker` and `.dotnet_<version>_marker` files in the pfx dir gate one-shot init steps (`docker/entrypoint.sh:86, 119`). Changing `DOTNET_VERSION` triggers a fresh .NET install because the marker name is versioned; changing `PROTON_VERSION` does not auto-reinit the prefix (only proton binary check at `docker/entrypoint.sh:29`) — likely gotcha.
- hltv-relay + redis services are gated behind docker compose profile `tv` (`docker-compose.yaml` post-edit); start with `docker compose --profile tv up`. Without the profile they don't launch, so the deadworks container defaults to TV disabled and nothing tries to connect to `hltv-relay:3000`.
- TV broadcast URL default inside the compose network is `http://hltv-relay:3000/publish` (`.env.example` + `docker/entrypoint.sh:14`); hltv-relay itself listens on container port 3000, exposed on host 8080, auth via `HLTV_RELAY_AUTH_KEY` matching `TV_BROADCAST_AUTH` shared env (`docker-compose.yaml:22-30`).
- Xvfb on `:99` is started before wineboot and the server run — Proton/Wine subprocesses need a display even for a headless dedicated server (`docker/entrypoint.sh:82-83`, `run_server.sh` exports `DISPLAY=:99`).
