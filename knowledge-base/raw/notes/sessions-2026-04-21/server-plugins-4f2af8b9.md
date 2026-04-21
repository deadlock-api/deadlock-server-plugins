---
date: 2026-04-21
task: session extract — server-plugins 4f2af8b9
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/4f2af8b9-add9-46e6-ab6b-d3cc34b1d584.jsonl]
---

## Source 2 engine (entities, schema, convars, events, Valve class names)

- `GameResourceServiceServerV001` (from `engine2.dll`) holds a pointer to the entity system at offset `0x58` (`GAME_RESOURCE_SERVICE_ENTITY_SYSTEM_OFFSET`); the entity-system pointer is not stable across map loads so plugins should cache `game_resource_svc` and re-read `entity_system()` fresh each call (plugins/entity-plugin/src/lib.rs init + `EngineState::entity_system`).
- Entity chunk table layout was corrected this session: the chunk pointer array is **inline** at `entity_system + 0x10` with 32 direct `CEntityIdentity*` entries at 8-byte stride — not a pointer-to-pointer (`ENTITY_SYSTEM_CHUNK_ARRAY_OFFSET = 0x10`, `NUM_CHUNKS = 64`, `ENTITIES_PER_CHUNK = 512`, `MAX_ENTITIES = 32768`). Chunks 0-31 are non-networkable, 32-63 networkable.
- `CEntityIdentity` is `0x70` (112) bytes (was wrongly `120`). Instance pointer at `+0x00` (no vtable — plain struct), designer-name string pointer at `+0x20` (previous code read `+0x10` → `+0x18` via double-indirect and was broken). The designer-name pointer is **not 8-byte aligned** so cannot go through `read_ptr` which rejects misaligned values — must raw-read `*const usize` and range-check (`0x10000..=0x7FFF_FFFF_FFFF`).
- Hardcoded version-dependent entity field offsets now captured as constants (plugins/entity-plugin/src/lib.rs):
  - `CBaseEntity::m_iTeamNum` = `0x33C`
  - `CBaseEntity::m_nSubclassID` = `0x314` (type `CUtlStringToken`, u32)
  - `CCitadelPlayerController::m_hHeroPawn` = `0x984` (CHandle; low 15 bits `& 0x7FFF` is the entity index, `0xFFFFFFFF`/`0` mean invalid)
  - `PlayerDataGlobal_t::m_nHeroID` = `0x9E4` (controller + `0x9E4`; PlayerDataGlobal sub-struct at controller + `0x9C8` i.e. `+0x1C` in the sub-struct)
  - `CBasePlayerController::m_steamID` = `0x708` (u64, SteamID64 individual-account range `76561190000000000..76561210000000000`)
  - `CCitadelPlayerPawn::m_nLevel` = `0xEB8`
- Known Valve class names used for dispatch: `citadel_player_controller` (gates controller-only fields), `player` / `CLASS_PLAYER_PAWN` (gates m_nLevel).
- vtable-sanity check pattern: a valid entity pointer must itself point to a non-null, 8-byte-aligned, in-userland vtable pointer (`> 0x10000`, `< 0x7FFF_FFFF_FFFF`, `% 8 == 0`); used both for `get_entity` validation and memory-scan entity discovery.

## Deadlock game systems (heroes/abilities/items/teams/gamemodes/CLI flags)

- Team-number plausibility range is `0..100` (sentinel for unreadable/garbage slots).
- Hero ID is a u32 enum surfaced straight from `PlayerDataGlobal_t::m_nHeroID`; `0` means unset. No name lookup table in this plugin.
- Player level validity range used: `1..=100`.

## Deadworks runtime (plugin host C# API, lifecycle, bridges)

- New out-of-process coordinator: `server-manager` (crates/server-manager, Linux x86_64 binary) runs as a daemon alongside the dedicated server inside the container. It owns two TCP ports:
  - `0.0.0.0:9100` — plugins dial in and send a `RegisterMessage {plugin, version}` as the first newline-delimited JSON line.
  - `0.0.0.0:9101` — CLI control channel; clients send `CliRequest {plugin, data}`, daemon routes to the named plugin by name, correlates by UUID, returns `CliResponse::{Ok|Error}` tagged by `status` field.
- Wire protocol is newline-delimited JSON over TCP in both directions (crates/server-manager/src/daemon.rs + protocol.rs). Command timeout hard-coded to 10s (`COMMAND_TIMEOUT`). Pending-request map is per-plugin (`HashMap<id, oneshot::Sender>`); on timeout the entry is cleaned up explicitly to avoid leak.
- `PluginResponse` has `{id, data}`; `PluginCommand` has `{id, data}` — the `id` is a UUIDv4 string minted daemon-side and echoed by the plugin.
- Protocol types duplicated between `crates/server-manager` (Linux-native) and `crates/plugin-common` (Windows-target for Proton-loaded plugins); deliberate because they compile to different targets and sharing would require cross-target dependency gymnastics (aggregated reviewer finding, skipped as false positive).
- CLI usage exposed via `server-manager send <plugin> '<json>'`; daemon mode is the zero-arg invocation. See crates/server-manager/src/main.rs and `mise.toml` tasks `entity:list/health/debug/dump/set` which shell into the container with `docker compose exec deadlock /opt/plugins/server-manager send entity-plugin '...'`.
- The entity-plugin log path was renamed this session: `/tmp/set_position.log` → `/tmp/entity_plugin.log` (entrypoint.sh `rm -f` list and dump block).

## Plugin build & deployment (Docker/Proton/CI/csproj)

- Dockerfile gained a second builder stage `manager-builder` that targets `x86_64-unknown-linux-gnu` (distinct from the existing `plugin-builder` which targets `x86_64-pc-windows-gnu` for Proton). Both pinned to `rust:1.94-slim`. Output copied to `/opt/plugins/server-manager` in the final stage.
- `rust-toolchain.toml` now installs both targets: `["x86_64-pc-windows-gnu", "x86_64-unknown-linux-gnu"]`.
- `entrypoint.sh` launches `/opt/plugins/server-manager` in the background before the Deadlock server, records `MANAGER_PID`, `sleep 1` to let ports bind, and kill-waits it on shutdown alongside `INJECTOR_PID`.
- `.gitignore` now ignores `/dumps` (output directory for `mise entity:dump`).
- `crates/server-manager/Cargo.toml` tokio features: `rt-multi-thread, net, io-util, sync, macros, time`; uses `uuid = {features=["v4"]}`.
- Simplify session removed legacy `entity:build` / `entity:test` mise tasks in favour of docker-exec-driven tasks that speak to the daemon.
