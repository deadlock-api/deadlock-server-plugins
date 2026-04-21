---
date: 2026-04-21
task: session extract — deadworks 710426a0
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/710426a0-8ddc-4bad-8855-2f12b78847c0.jsonl]
---

Session: `/simplify` review of the new `scripts/validate-signatures.py` in the deadworks repo (`/home/manuel/deadlock/deadworks/`). Surfaced concrete facts about deadworks' PE loader, memory signature scanner, and build-side tooling layout.

## Deadlock game systems

- Deadlock ships two DLLs that deadworks scans/hooks by signature: `engine2.dll` at `game/bin/win64/engine2.dll` and `server.dll` at `game/citadel/bin/win64/server.dll` (validate-signatures.py:18-21). Note the `citadel` subpath for server.dll — this is the in-game module name.
- Linux Steam install dirs deadworks probes: `~/.steam/steam/steamapps/common/Deadlock` and `~/.local/share/Steam/steamapps/common/Deadlock` (validate-signatures.py:23-26). `DEADLOCK_DIR` env var overrides.

## Deadworks runtime

- Memory signatures live in `config/deadworks_mem.jsonc` with schema `{signatures: {<name>: {library: "engine2.dll"|"server.dll", windows: "<hex pattern>"}}}`. JSONC — line comments like `// this is a bad sig` are present (config/deadworks_mem.jsonc:117, noted by reuse agent).
- Signature wildcard tokens are `?` or `??`; concrete bytes are hex — parsed by `Scanner::ParseSignature()` in `deadworks/src/Memory/Scanner.cpp`, mirrored by validate-signatures.py:85-99.
- Scanner matching: C++ uses `std::search` with a custom comparator (Scanner.cpp:31-36) rather than regex. Python validator ports this via `re.finditer` with `re.DOTALL` + per-byte `re.escape`.
- Scanner API surface is minimal: only `FindFirst` is exposed by `Scanner.hpp`/`Scanner.cpp` — no fuzzy/prefix search exists in the C++ runtime (the Python validator's `fuzzy_search` is validator-only recovery logic).
- PE module loader lives at `deadworks/src/Lib/Module.hpp` (lines 73-90). `Module::LoadSections()` walks section headers via Win32 `IMAGE_FIRST_SECTION` and populates a map keyed by section name, exposed via `GetSectionMemory(".text")`. Only `.text` is scanned.
- `MemoryDataLoader.cpp:17` parses deadworks_mem.jsonc using nlohmann with comments enabled: `nlohmann::json::parse(file, nullptr, false, true)` — the fourth arg (`true`) is `ignore_comments`, which also strips `/* */` blocks, not just `//`.
- Reported RVA in tooling = section virtual address + offset-in-section-bytes (validate-signatures.py:248). Matches how the C++ side consumes scanner hits against module base.

## Plugin build & deployment

- `scripts/` layout in deadworks repo has three Python utilities: `update-game-exported.py`, `update-protos.py`, `validate-signatures.py`. First two define an identical `get_project_root() = Path(__file__).resolve().parent.parent` (update-game-exported.py:28-29, update-protos.py:53-54); third inlines it. No shared `_common.py` yet.
- `update-protos.py` branches on `platform.system()` for cross-platform proto compilation (reuse agent finding). Worth knowing when touching proto regen.
- `validate-signatures.py` workflow: locate Deadlock install → load `.text` section of each DLL once (cached by library name) → for every signature, regex-scan .text → classify as MATCH (1 hit) / MULTIPLE (>1) / BROKEN (0) / SKIP (no pattern/unknown library) / ERROR. BROKEN triggers longest-concrete-prefix fuzzy candidates (min 6 bytes, cap 5 candidates).
- Post-review edit reduces DLL memory use by reading only 4KB of headers then `f.seek(raw_offset); f.read(size)` for the `.text` section — relevant because engine2.dll is 50-100MB and the prior `f.read()` loaded the whole file.
