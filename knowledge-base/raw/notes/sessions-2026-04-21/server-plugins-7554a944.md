---
date: 2026-04-21
task: session extract — server-plugins 7554a944
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/7554a944-8671-4275-a74d-8eebdba24be6.jsonl]
---

## Source 2 engine

- `CGameEntitySystem` layout (reverse-engineered from engine2.dll + server.dll for Deadlock build):
  - Chunk array is **inline at `entity_system + 0x10`**, 8-byte pointer stride, **64 entries** (not 32). Chunks 0-31 = non-networkable, chunks 32-63 = networkable entities. Players/heroes live in the networkable range (index >= 16384).
  - `ENTITIES_PER_CHUNK = 512`; confirmed by disassembly pattern `shr ecx, 9` (÷512) followed by `and ecx, 0x1FF` (&511). Total `MAX_ENTITIES = 32768`.
  - `CEntityIdentity` stride is **0x70 (112 bytes)** in Deadlock, not the 120 found in some CS2 refs. 8-byte drift compounds across indices.
- `CEntityIdentity` field layout used by the entity plugin:
  - `+0x00` = `m_pInstance` (pointer to entity object on heap) — NOT a vtable. Earlier guess of `+0x08` was wrong; that slot holds a code/class-metadata pointer.
  - `+0x08` = code pointer (class metadata).
  - `+0x20` = `m_designerName` (char* to class name like `"citadel_player_controller"`). Note: designer-name string pointers can be **non-8-byte-aligned** (e.g. ends in 0xE), so pointer-alignment guards reject them unless relaxed.
- `GameResourceServiceServerV001 + 0x58` holds the live entity system pointer. **Must be re-read per command** — caching it at init (before map load) yields a stale/empty pool.
- `CBodyComponent + 0x08` = `m_pSceneNode`; `m_vecAbsOrigin` at scene node + 0xC8. Resolvable via schema system at init.

## Deadlock game systems

- `CCitadelPlayerController`:
  - `+0x708` = SteamID64.
  - `+0x9C8` = `m_PlayerDataGlobal`; inside that `+0x1C` → **`m_nHeroID`** (absolute offset `+0x9E4`). Earlier guess of `+0x9D0` was off; confirmed against memory dump where hero_id=19 matched Shiv.
  - `+0x9D8` = 840 (looks like current max health mirror), `+0x9EC` = 600 (base health).
  - `m_hPawn` / `m_hHeroPawn` handle — decoded index pointed at the player pawn (e.g. idx 2689).
- `CCitadelPlayerPawn`:
  - Designer name is `"player"` (not a per-hero class name). `m_nSubclassID` at `+0x314` is 0 for player pawns.
  - `m_iTeamNum` at `+0x33C` (Deadlock teams: 2=Amber, 3=Sapphire).
  - `m_iHealth` at entity `+0x2D0`; `m_lifeState` at `+0x2D8` — confusing them produced fake zero health.
- Weapon entities (e.g. `citadel_weapon_shiv`) DO populate `m_nSubclassID` at `+0x314` — a `CUtlStringToken` (MurmurHash2 of subclass name). Useful as a proxy for hero identification when the pawn itself doesn't carry hero info.
- Health can **exceed `m_iMaxHealth`** in Deadlock (items grant bonus HP), so `health <= max_health` sanity checks drop valid hero entities.

## Deadworks runtime

- `entity-plugin` is a Rust plugin under `plugins/entity-plugin/src/lib.rs`; commands dispatched as JSON via `server-manager send entity-plugin '{"cmd":"..."}'`.
- `mise entity:list` / `mise entity:dump` tasks wrap the send call; `entity:dump` writes raw memory to `dumps/entity_memory_dump.json` for offline offset archaeology (chunk pointer array + per-chunk data + per-entity 0x1000-byte slabs).
- Plugin logs land at `/tmp/entity_plugin.log` inside Wine (Wine `Z:\tmp\` == Linux `/tmp/`). `entrypoint.sh` previously tailed the wrong file (`/tmp/set_position.log`) which hid all diagnostics.
- `read_ptr` helper enforces 8-byte alignment — safe for vtables/struct pointers, but breaks on char* strings (designer names). Class-name reads need an alignment-agnostic variant.
- `IsBadReadPtr` (used by `is_readable`) is **unreliable under Wine** — don't rely on it for init-time probing; defer risky probes to command time where a crash just fails one request.
- Schema-offset resolution via binary scan of `server.dll` can segfault if `get_module_bounds` returns bad bounds; wrap `resolve_offset` calls defensively.

## Plugin build & deployment

- Production Dockerfile was building `test-plugin`/`test-harness` and the game server auto-loaded `test_plugin.dll`. Fix: restrict the release build to `plugin-loader`, `entity-plugin`, and `injector` only.
- Plugin changes require rebuilding the Docker image before they take effect in-game; caching the entity-system pointer at init (3 s post-injection) pre-dated map load and was a recurring source of "stale empty pool" symptoms.
