---
date: 2026-04-21
task: session extract — deadworks 1dba11a1
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/1dba11a1-083c-4be4-a759-6d1c24ef26d7.jsonl]
---

## Deadworks runtime

- Memory signature scanner lives at `deadworks/src/Memory/Scanner.cpp`; its pattern-parsing logic (`Scanner::ParseSignature`) uses space-separated hex tokens where `?` or `??` denote wildcards. Ported intent: tokens are hex bytes, wildcards are `None` in the parsed pattern list (validate-signatures.py:85-99 mirrors this).
- PE module loading lives at `deadworks/src/Lib/Module.hpp` — parses DOS header (e_lfanew @ 0x3C), PE signature (`PE\0\0`), COFF header, iterates 40-byte section headers to find `.text`, uses `min(virtual_size, raw_size)` as the scan region (validate-signatures.py:44-82).
- Memory signatures are defined in `deadworks/config/deadworks_mem.jsonc` (JSONC, `//` comments). Schema: top-level `signatures` map keyed by signature name; each entry has `library` (e.g. `"engine2.dll"`, `"server.dll"`) and a per-OS pattern string under `windows` (Linux key presumably exists too but is not scanned by the validate script).
- Only two libraries are currently recognised: `engine2.dll` at `game/bin/win64/engine2.dll` and `server.dll` at `game/citadel/bin/win64/server.dll` (validate-signatures.py:18-21). `server.dll` living under `game/citadel/` reflects Deadlock's internal codename "citadel".

## Source 2 engine

- Source 2 engine DLLs are 50-150 MB each per the efficiency review; only the `.text` section is needed for sig scanning, and its location is in the PE section table within the first few KB — full-file read into memory is wasteful.
- `.text` section scan size should use `min(virtualSize, sizeOfRawData)` (truncating the in-memory region to the smaller of the two), not either one alone.

## Plugin build & deployment

- `DEADLOCK_DIR` env var (or `--deadlock-dir` flag) is the canonical override for locating a local Deadlock install; default probe paths: `~/.steam/steam/steamapps/common/Deadlock` and `~/.local/share/Steam/steamapps/common/Deadlock` (validate-signatures.py:23-26, 31).
- `scripts/validate-signatures.py` is the new untracked tool that diffs the committed `config/deadworks_mem.jsonc` signatures against the local DLLs and reports `MATCH` / `MULTIPLE` (>1 hit, first-match still used at runtime) / `BROKEN` / `SKIP` / `ERROR`. Intended workflow for detecting sig drift after a game update; has fuzzy mode that falls back to the longest concrete prefix (min 6 bytes, cap 5 candidates) when the full pattern fails.
- Session was a `/simplify` review on that untracked script; user interrupted before any edits were applied. Review findings not yet incorporated — useful if picking up this task: regex compiled fresh per signature (no `re.compile` cache), full DLL read-into-memory before header parse, hand-rolled JSONC stripper handles `//` only (no `/* */`), DLLs processed serially rather than in parallel.
