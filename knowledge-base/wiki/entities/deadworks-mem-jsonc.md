---
title: deadworks_mem.jsonc (signature file)
type: entity
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-1dba11a1.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-4881cd7a.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-52a01b09.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-710426a0.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-88df5d67.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-d75e1c40.md
related:
  - "[[deadworks-runtime]]"
  - "[[source-2-engine]]"
created: 2026-04-21
updated: 2026-04-21
confidence: high
---

# `deadworks_mem.jsonc` — memory signature layout

The JSONC file deadworks reads at startup to locate game functions and
vtable slots by byte-pattern against the loaded Source 2 DLLs.

## On-disk location

- Source of truth: `deadworks/config/deadworks_mem.jsonc` in the repo.
- Runtime: `game/citadel/cfg/deadworks_mem.jsonc` — copied in by the
  Docker entrypoint (deadworks-328372c6, deadworks-ddfface7).
- Resolved relative to `deadworks.exe` at startup
  (`startup.cpp:34`, deadworks-52a01b09).
- **Missing file is fatal.**

## Format (JSONC)

Top-level `signatures` map keyed by signature name; each entry has:

- `library` — one of `"engine2.dll"` or `"server.dll"` (only two
  libraries are currently recognised; deadworks-1dba11a1,
  deadworks-710426a0).
- `windows` — a space-separated hex pattern like
  `"48 8B ? ? ? ? ? 48 85 C0 74 ?? ..."`. Wildcards are `?` or `??`.
- Linux key presumably exists too but is not scanned by the validate
  script.

Parsed using nlohmann with comments enabled:
`nlohmann::json::parse(file, nullptr, false, true)` — the fourth arg
`ignore_comments=true` strips both `//` and `/* */` blocks
(deadworks-710426a0).

## Dual payloads

The file holds two fundamentally distinct kinds of entry
(deadworks-d75e1c40):

1. **Byte-pattern memory signatures** — used to locate functions in
   `engine2.dll` + `server.dll` at runtime.
2. **Virtual function offsets (vtable indices)** — for game interfaces.

Both are reverse-engineered manually per game update.

## Scan mechanics

- Scanner implementation: `deadworks/src/Memory/Scanner.cpp`
  (deadworks-1dba11a1, deadworks-710426a0).
- PE parsing lives at `deadworks/src/Lib/Module.hpp` — parses DOS header
  (`e_lfanew` at 0x3C), PE signature (`PE\0\0`), COFF header, iterates
  40-byte section headers to locate `.text`.
- `Scanner::ParseSignature()` tokenizes the space-separated hex pattern;
  `?` / `??` → None in the parsed pattern list.
- `Scanner::FindFirst` uses `std::search` with a custom comparator,
  scanning only the `.text` section of the target DLL.
- Only the `.text` section is scanned; scan size is
  `min(virtual_size, raw_size)` — using either alone is wasteful or
  incorrect.
- `MemoryDataLoader` is a singleton
  (`deadworks::MemoryDataLoader::Get()`); signatures stored keyed by
  fully-qualified `Class::Method` names and accessed via
  `GetOffset(name) -> optional<uintptr_t>` (deadworks-52a01b09).

No fuzzy / prefix search exists in the C++ runtime — `Scanner.hpp` /
`.cpp` expose only `FindFirst`. Fuzzy search is validator-only recovery
logic (deadworks-710426a0).

## Crash behavior

The C++ scanner crashes the process if a required pattern isn't found
(`startup.cpp:40-52` validates ~5 signatures before handoff)
(deadworks-d75e1c40):

- Required boot sigs include `UTIL_Remove`,
  `CMaterialSystem2AppSystemDict::OnAppSystemLoaded`,
  `CServerSideClientBase::FilterMessage`, `GetVDataInstanceByName`,
  `CModifierProperty::AddModifier`.
- There is no graceful fallback — stale signatures = hard crash at
  startup.
- Counts: 41 entries in the file at one validated session time
  (deadworks-d75e1c40).

## Target DLL layout (Linux Steam install)

The validate script probes these two paths (deadworks-710426a0):

- `<DeadlockDir>/game/bin/win64/engine2.dll` (~6.6 MB)
- `<DeadlockDir>/game/citadel/bin/win64/server.dll` (~54 MB)

`server.dll` under `game/citadel/` reflects Deadlock's internal codename
"citadel".

## `scripts/validate-signatures.py`

A zero-pip-dep Python tool that mirrors the C++ scanner's parsing and
matches against the local DLLs (deadworks-1dba11a1, deadworks-710426a0,
deadworks-d75e1c40):

- Reports MATCH / MULTIPLE / BROKEN / SKIP / ERROR per sig.
- `MULTIPLE` = >1 hit (first-match still used at runtime).
- `BROKEN` triggers longest-concrete-prefix fuzzy candidates (min 6 bytes,
  cap 5 candidates).
- Intended workflow: detect sig drift after a Deadlock game update.

Design constraint (deliberate): **auto-fixing BROKEN sigs is rejected**
because a wrong fuzzy-candidate match would silently hook the wrong
function at runtime — worse than crashing. Only validation is automated;
fixes stay human-reviewed.

Input location:

- `DEADLOCK_DIR` env var OR `--deadlock-dir` flag is the override.
- Default probe paths:
  - `~/.steam/steam/steamapps/common/Deadlock`
  - `~/.local/share/Steam/steamapps/common/Deadlock`
- Was previously named `scan-signatures.py`; renamed mid-session to
  `validate-signatures.py`.

Flags: `--json`, `--deadlock-dir`.

## CI limitations

- GitHub Actions runners can't access local Steam installs, and DLLs
  aren't mirrored in SteamTracking (too large for git).
- Running the validator in CI requires SteamCMD / DepotDownloader with
  credentials as secrets — not wired up.
- An intermediate ephemeral skill `~/.claude/skills/update-deadlock-sigs/`
  was created then deleted in favor of deferring to CI-based detection
  (deadworks-d75e1c40).

## Related

- `scripts/update-game-exported.py` and the `update-sourcesdk.yml`
  workflow are sibling upstream-sync tools using the same PR-creation
  pattern.
- [[source-2-engine]] — general PE/schema/module internals.
- Signon-related sigs were deleted in upstream `38a35cc` ("remove
  signonstate hooks") across `config/deadworks_mem.jsonc`,
  `deadworks/src/Core/Deadworks.{cpp,hpp}`, `SendNetMessage.{cpp,hpp}`,
  `ManagedCallbacks.*`, and several managed files (deadworks-4881cd7a).
