---
date: 2026-04-21
task: session extract — server-plugins 34752d6a
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/34752d6a-b295-447b-b883-311e54191c45.jsonl]
---

Session goal: extend Rust `entity-plugin` with `set_team` / `set_hero` commands using pure memory manipulation (no RCON). Extensive trial-and-error across Source 2 networking internals — many approaches failed, producing concrete negative findings.

## Source 2 engine

- `CBaseEntity` field offsets (Deadlock build):
  - `m_CBodyComponent` at 0x30
  - `m_NetworkTransmitComponent` (NTC) **inline** at 0x38 (NOT a pointer — double-dereference crashes with garbage like `fn=0xD98B480000000185`)
  - `m_iTeamNum` at 0x33C
  - health at 0x2D0, max_health at 0x2D4, abs_origin at 0xC8
  - `m_nHeroID` at 0x9E4 (nested inside `PlayerDataGlobal_t` on `citadel_player_controller` — NOT a direct network field; passing this offset to `StateChanged` crashes)
- `CEntityInstance` has `m_pEntity` at entity+0x10 → `CEntityIdentity`
- `CEntityIdentity.m_flags` at identity+0x30 — setting `FL_EDICT_CHANGED | FL_FULL_EDICT_CHANGED` (bits 0|2) from plugin thread OR game thread does NOT trigger network propagation to clients. Confirmed empirically.
- `m_NetworkTransmitComponent` at entity+0x38: first qword looks like a vtable pointer (same value across entities, e.g. `0x6FFFF60805F0`), but the region it points into contains ASCII strings like `"Enable : %d SplitScreen %X"` — it is class metadata/typeinfo, NOT a callable vtable. Calling vtable[1] as `StateChanged` crashes with C0000005.
- `__m_pChainEntity` schema lookup (`resolve_offset("CBaseEntity", "__m_pChainEntity")`) **crashes the schema system** on Deadlock — the CS2/CounterStrikeSharp `CNetworkVarChainer` pattern does not apply.
- Pure direct memory writes to networked fields do NOT propagate to clients — Source 2 uses delta-encoded networking; the dirty-notification path is mandatory.
- Game-thread requirement: engine functions like `ChangeTeam` must be called from the game thread, not an async/tokio worker — calling from plugin thread hangs or deadlocks.
- `ISource2Server::GameFrame` vtable hook (patch vtable[6]) **never fires** — the game's own tick dispatcher appears to call GameFrame via a devirtualized/direct call, bypassing the vtable. Inline detour (14-byte `FF 25 …` absolute jmp) on the real function pointer DOES fire.
- `"changing team"` log string (visible in server logs: `Player [0]Manuel_Hexe changing team 0->2 in game mode -1`) lives in server.dll. Scanning server.dll byte-by-byte with `is_readable` per byte causes stack overflow / multi-second hangs; reading the module as a contiguous slice is instant.
- The function reachable from the `"changing team"` LEA is NOT `CBaseEntity::ChangeTeam(int)` — it is a higher-level rules hook like `CCitadelGameRules::OnPlayerChangeTeam` taking `(entity, team, gameMode=-1)` as three args. Calling it from a detoured game frame still crashes (wrong `this`).
- CS2-style `NetworkStateChanged` signature `4C 8B C2 48 8B D1 48 8B 09` DOES match in Deadlock server.dll (found at `0x6FFFF59D2570`). Disassembly: `mov r8, rdx; mov rdx, rcx; mov rcx, [rcx]` — takes `(chainer, &NetworkStateChangedData)` and dereferences chainer's first qword as entity pointer. Constructing a fake chainer with entity as first field and calling it crashes — the function reads additional internal state beyond the first qword.
- Source 2 game state enum sequence observed: `WaitingForPlayersToJoin=2 → HeroSelection=3 → MatchIntro=4 → WaitForMapToLoad=5 → PreGameWait=6 → GameInProgress=7`.
- SignonState sequence: `CONNECTED → NEW → PRESPAWN → SPAWN → FULL`.

## Deadlock game systems

- Steam App ID: game `1422450`, dedicated server `1422460`.
- Team values: `0`=unassigned/spectator, `2`=Amber, `3`=Sapphire.
- Hero example: `hero_id=65` seen for hero_viper; server logs `Loaded hero <pawn_index>/hero_viper` after GC round-trip `k_EMsgServerToGCRequestPlayerHeroData` (10044) → `...Response` (10045).
- Player controller class name: `citadel_player_controller` (index 1 in entity list; pawn at `hero_pawn_index` e.g. 2689).
- Deadlock console commands relevant to team/hero propagation (sanctioned engine paths): `changeteam`, `selecthero <hero_name>` — these do all network propagation internally but user forbade RCON/console use.
- `citadel_toggle_server_pause` is known to work via `ServerCommand` internal engine API (proven by the deadlock-custom-server pause-injector).
- Launch + autoconnect URL: `steam://run/1422450//-connect 127.0.0.1:27016/` (invoked via `xdg-open` in a mise task).

## Deadworks runtime

- No Deadworks C# work in this session — all work was in Rust plugins under `/plugins/entity-plugin/` targeting the native plugin-loader DLL pipeline.
- Plugin registers with `server-manager` over TCP `127.0.0.1:9100` after init. Log line: `registered with server-manager plugin_name="entity-plugin"`.
- Plugin offsets are resolved at init by reading the GameResourceService pointer, then schema-walking: `body_component=0x30 scene_node=0x8 abs_origin=0xC8`.
- Plugin log path inside container: `/tmp/entity_plugin.log` (mapped to `Z:\tmp\entity_plugin.log` under Wine).
- `plugin-common` exports `scan_find_string(module_slice, needle)` which requires the byte AFTER the needle to be `\0` — unusable for matching substrings inside longer printf format strings (e.g. `"changing team"` sits mid-string in `"Player [%d]%s changing team %d->%d..."`). Must walk back to previous null to find the real string start, then scan for a LEA referencing that address.
- Windows VEH (`AddVectoredExceptionHandler`) works from the injected plugin and can log crash code + address + context string. Must be gated by an `IN_DANGEROUS_CALL` flag or it fires for benign access violations during schema init (hundreds of spurious entries).
- Inline detour pattern that works: allocate RWX page, patch target with 14 bytes `FF 25 00 00 00 00 <u64 hook_addr>` via `VirtualProtect` to PAGE_EXECUTE_READWRITE, then restore original bytes inside hook before calling through, re-install after. Guard original bytes with a `Mutex<[u8; 14]>` — plain `static mut` triggers 2024 edition lints.

## Plugin build & deployment

- Server container command: `docker compose exec deadlock /opt/plugins/server-manager send <plugin-name> '<json>'`.
- mise tasks wrap server-manager calls; user has `mise entity:list / dump / dump-entity / set / set-team / set-hero / health / debug` tasks.
- Plugin DLLs are loaded under Wine from `Z:/home/steam/server/game/bin/win64/plugin_loader.dll` which then loads `plugins/entity_plugin.dll`, `game_plugin.dll`, `test_plugin.dll`. Injector invoked via Proton wine64: `/opt/proton/files/bin/wine64 ./injector.exe --dll <path> --process deadlock.exe`.
- WINEPREFIX for the server: `/home/steam/.steam/steam/steamapps/compatdata/1422450/pfx` (note: compatdata uses CLIENT appid 1422450, even though the dedicated-server appid is 1422460).
- `windows-sys` crate: workspace already enables `Win32_System_Diagnostics_Debug`; for `EXCEPTION_POINTERS` also need `Win32_System_Kernel`. `EXCEPTION_RECORD::ExceptionCode` is `i32` in windows-sys (not `u32`).
- Server exits with code 5 on C0000005 access violation from a plugin — the injected plugin shares the server process, so any UB kills the server.
