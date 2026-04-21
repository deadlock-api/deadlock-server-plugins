---
date: 2026-04-21
task: session extract — server-plugins 4327d1b2
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/4327d1b2-968a-4ed5-adbc-9dc2808079ff.jsonl]
---

Session worked on a separate Rust `entity-plugin` (not the C# plugins in this repo) — a DLL injected into `deadlock.exe` via `plugin_loader.dll`. Findings still apply to Source 2 / Deadlock / injection-style runtime.

## Source 2 engine

- `CBaseEntity` has 79 fields in the current Deadlock `server.dll` build. It was found at `module+0x2FED450` in one run.
- Confirmed offsets resolved via schema scan on `CBaseEntity`: `m_CBodyComponent` at `0x30`. Chain for world position: `CBaseEntity::m_CBodyComponent` (0x30) → `CBodyComponent::m_pSceneNode` (0x8) → `CGameSceneNode::m_vecAbsOrigin` (0xC8). `CBodyComponent` has 2 fields, `CGameSceneNode` has 32.
- Health lives directly on `CBaseEntity` as `m_iHealth` + `m_iMaxHealth` (two adjacent `i32` fields) — no pointer chain needed, unlike position.
- Source 2 schema field names are NOT globally unique. Binary-scanning `server.dll` for a field-name string without class filtering finds the first match across all classes — works for unique names like `m_CBodyComponent`/`m_pSceneNode`/`m_vecAbsOrigin`, fails for common names like `m_iHealth` which appears on many classes with different offsets. Symptom seen: bogus `hp=0/28671` / `hp=78600/0`.
- Fix pattern: class-aware binary scan — first locate the `SchemaClassInfoData_t` by finding the class-name string address then searching for a pointer to it; iterate that struct's fields array to find the named field's offset. Avoids the schema-system vtable path entirely.
- The schema-system vtable call (calling a resolver through a vtable on the schema system pointer) crashes the `deadlock.exe` process (exit code 5) and cannot be guarded — it dies before even producing a plugin log entry. `std::panic::catch_unwind` does NOT catch native access violations from bad vtable calls. Binary scan is the only safe path.
- `SchemaClassFieldData_t` appears to be 0x20 bytes (3 pointers + 2 i32). If treated as larger (e.g. 0x28), `fields_ptr.add(j)` misaligns after the first entry and later field-name reads crash.
- `scan_find_string` for `"CBaseEntity\0"` may match other occurrences in memory — the null terminator in the pattern is important but not always sufficient; a safer iteration is to find the field-name string address once, then `memmem`-search the fields-array bytes for that pointer value instead of dereferencing each field entry's name pointer.

## Deadlock game systems

- On an idle server without players, there are ~860–885 entities total, ~133–145 with positions, ~546 with readable health in one observed run.
- `combine_watcher_blue` (class name) is a real game entity that exists on an idle server with valid health (`4000/4000`) — useful target for health-read/write tests. Entity `[0]` and other low-index entities often have class `?` (unresolvable) and should be skipped.
- Entity index `[10]` observed as class `?` at `(0.0, 0.0, 0.0)` — likely a non-game placeholder, not a real entity. Tests should pick entities that have both a real class name and `hp=` in the `list` output.

## Deadworks runtime

- Entity-plugin init pattern (from log): "Plugin loaded, starting init..." → locates `server.dll` base/size → probes entity list (`Entity system: 0x...` and `Entity list probe: offset 0x10 looks valid (chunk_list=..., first_chunk=..., first_entity=...)` — entity system uses a chunked allocator, first chunk list pointer at `+0x10`).
- Runtime plugin commands are issued via a result-file IPC: the plugin watches a command channel, writes output to a result file; tests poll `/tmp/.../tasks/b*.output` (30s timeout seen — `[test] FAIL: No result file after 30s for list command` indicates server crash or hang).
- `IsBadReadPtr` is unreliable under Wine/Proton — can miss guard pages or itself trigger the access violation it's supposed to prevent. Working mitigation: do ONE large `is_readable` check covering the entire entity struct range once up front, rather than per-field checks. Per-field checks did not prevent crashes; one-shot range check did.
- Defensive pattern for optional Source 2 fields: resolve offsets as `Option<i32>` in `EngineState`, have read/write helpers return `None` if offset is unresolved, and log but don't abort init on resolution failure. This kept the plugin functional when `m_iHealth` couldn't be resolved.

## Plugin build & deployment

- Plugin DLLs are injected into `deadlock.exe` via `plugin_loader.dll` + a separate `injector.exe` run under Proton: `/opt/proton/files/bin/wine64 ./injector.exe --dll Z:/home/steam/server/game/bin/win64/plugin_loader.dll --process deadlock.exe`. WINEPREFIX is `/home/steam/.steam/steam/steamapps/compatdata/1422450/pfx` (1422450 = Proton/Deadlock steam app id).
- Injected DLL path: `Z:/home/steam/server/game/bin/win64/plugins/<name>.dll`. Wine warns `cannot find builtin library for L"...\plugins\..."` — benign.
- Integration tests live in `tests/test-<name>.sh` and are orchestrated via `docker-compose.test.yaml` (one service per test) + `mise.toml` tasks. `mise` `test` task uses `depends` to run tests sequentially.
- Gotcha: running the compose file directly uses `--abort-on-container-exit` which kills all services as soon as the fastest (`test-injection`) exits. Tests must be run one-per-compose-invocation (via mise `depends`) or in separate compose runs.
- Gotcha: new test scripts must be added to the Dockerfile `COPY` list — not picking this up produces silent test skips / missing script errors inside the container. Adding a test requires three touch points: `tests/test-X.sh`, new `docker-compose.test.yaml` service, `mise.toml` task in `test` depends, AND Dockerfile COPY.
- Typical test boot time: plugin init timeout is 120s (Docker image build + Deadlock server start). Individual tests may take many minutes.
