---
date: 2026-04-21
task: session extract — server-plugins b68a4591
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/b68a4591-f125-40df-9f29-48e32a4153f0.jsonl]
---

Note: session is about a sibling Rust `plugins/entity-plugin` (not the C# plugins in this repo). Findings still touch Source 2 / Deadworks runtime concerns.

## Source 2 engine

- Entity position pointer chain: `CEntity -> CBodyComponent -> CGameSceneNode -> m_vecAbsOrigin` (Vector of 3 floats). Only entities with a `CBodyComponent` (physical/rendered — props, NPCs, players) expose a readable/writable position. Logic entities, triggers, sound entities have no body component and position read/write both fail.
- Class/designer name lives at `CEntityIdentity + 0x18`, read as a pointer-to-string. Many valid name pointers fail a naive "looks like a pointer" heuristic (alignment/range), so class resolution returns `?` / `unknown` for many entities even when the pointer is fine.
- Distinction that matters: validating "is this address mapped memory I can safely read?" is NOT the same as "does the 8 bytes here look like a plausible pointer?". Vector float payloads (e.g. coords like `12345.5`) will fail pointer-shaped validation even though the memory is readable.

## Deadworks runtime

- Plugin initialization is slow — "Initialization complete" log line takes ~55s after server boot in the Docker test harness. Tests poll for that sentinel before sending commands; default test `TIMEOUT` of 120s is used.
- Plugin command protocol (entity-plugin) is file-based: write to `/tmp/set_position_cmd.txt`, read back from `/tmp/set_position_result.txt`, log at `/tmp/set_position.log`. Filesystem mtime granularity can mask consecutive writes — sleep between command writes or the plugin won't pick up the second command.
- Entity index 0 is the world entity; scanning tests should skip it. Using `list 16384` (matches existing list-test practice) ensures enough entities are walked to find one with a resolved position, since most low indices have no body.
- Result format for `set`: `OK: [<index>] (x, y, z)`. Rust's default float formatter elides trailing `.0` (so `12345.0` prints as `12345`), which breaks naive substring asserts — tests must use non-round values like `12345.5`.

## Plugin build & deployment

- Rust entity-plugin integration tests live in sibling repo under `docker-compose.test.yaml` with per-test services (`test-entity-set`, `test-entity-list`, ...). Each service runs a bash script bundled into the image via the Dockerfile.
- `mise.toml` has per-test tasks (e.g. `test:entity-set`) plus an aggregate `test` task that depends on them. New tests must be registered both in the Dockerfile (COPY the script) and as a compose service AND as a mise task + dep.
- Bash gotcha hit during test authoring: negative coords like `-6789.5` get interpreted as grep flags. Use `grep -F --` or reorder args. Also: `grep -E '^\s*\[[0-9]+\].*\(-?[0-9]+\.[0-9]+,'` requires a decimal in the position tuple — entities with no position (printed as `(?, ?, ?)`) won't match.
- Docker buildx `mybuilder` instance (docker-container driver) is used for the test image builds; Wine warnings flood stdout and can truncate visible test output — filter the relevant lines or redirect.

## Bug/fix observed in session

- `read_ptr` helper in `plugins/entity-plugin/src/lib.rs` was used for two distinct jobs: (1) validating pointer fields before deref, (2) validating that an address is safe to read arbitrary bytes from. It rejects anything that doesn't pass pointer heuristics, so reading back a Vector's float bytes through `read_ptr` fails whenever coords don't happen to look like pointers. Fix: introduce `is_readable()` that only checks the page is mapped, and use it for non-pointer memory. Same bug pattern suspected in `get_entity_class_name` for the designer-name pointer (not fixed in-session).
