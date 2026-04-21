---
date: 2026-04-21
task: session extract — deadworks d75e1c40
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/d75e1c40-c073-4342-a517-b9c247bcebd1.jsonl]
---

## Deadlock game systems

- `game_exported/` holds two `.gameevents` files sourced from upstream `SteamTracking/GameTracking-Deadlock`: `game/core/pak01_dir/resource/core.gameevents` and `game/citadel/pak01_dir/resource/game.gameevents` (map to `game_exported/core.gameevents` and `game_exported/game.gameevents`).
- Deadlock Steam app ID is `1422450` (confirmed via `appmanifest_1422450.acf`). Local Steam install paths used in scanner defaults: `~/.steam/steam/steamapps/common/Deadlock/`.
- Game DLLs are NOT mirrored in SteamTracking repo (too large for git); must be obtained via SteamCMD / DepotDownloader, which requires Steam credentials for Deadlock (not a free dedicated server).

## Deadworks runtime

- `config/deadworks_mem.jsonc` holds two fundamentally distinct payloads: (1) byte-pattern memory signatures used to locate functions in `engine2.dll` + `server.dll` at runtime, and (2) virtual function offsets (vtable indices) for game interfaces. Both are reverse-engineered manually per game update.
- Target DLL layout (Linux Steam install):
  - `engine2.dll` at `game/bin/win64/engine2.dll` (~6.6 MB)
  - `server.dll` at `game/citadel/bin/win64/server.dll` (~54 MB)
- Runtime behavior: the C++ scanner in `deadworks/src/Memory/Scanner.cpp` crashes the process if a required pattern isn't found — there is no graceful fallback, so stale signatures = hard crash at startup.
- PE parsing for sig scanning is implemented in `deadworks/src/Lib/Module.hpp`; Python port in `scripts/validate-signatures.py` parses DOS header (`e_lfanew` at 0x3C), PE sig, COFF header, then iterates section headers (40 bytes each) to locate `.text` section virtual address + raw offset.
- Pattern format uses `?` / `??` wildcards in hex strings; the Python validator translates these to bytes regex and scans the `.text` section only.
- Current signature count when validated against live DLLs: 41 entries, all matching at session time.

## Plugin build & deployment

- Project already has an upstream-auto-update pattern: `scripts/update-protos.py` + `.github/workflows/update-protos.yml` (daily 06:00 UTC cron + `workflow_dispatch`, creates PR via `peter-evans/create-pull-request@v8` on branch `chore/update-protos`). This is the template for all other upstream sync workflows.
- New in this session — same cron/PR pattern applied to:
  - `scripts/update-game-exported.py` + `.github/workflows/update-game-exported.yml` — shallow clones `SteamTracking/GameTracking-Deadlock` with `git sparse-checkout set` on two resource dirs, copies the two `.gameevents` files. No compile step (simpler than protos).
  - `.github/workflows/update-sourcesdk.yml` — checks out repo with `submodules: true`, does `cd sourcesdk && git fetch origin main && git checkout origin/main`, then `create-pull-request`. Submodule upstream is `Deadworks-net/sourcesdk`.
- Signature validation tooling:
  - `scripts/validate-signatures.py` (renamed mid-session from `scan-signatures.py`) — zero-pip-dep PE parser + pattern matcher; reports MATCH / MULTIPLE / BROKEN / SKIP per sig; runs fuzzy longest-prefix search on BROKEN entries to surface candidates. Supports `--json`, `--deadlock-dir`, and `DEADLOCK_DIR` env var.
  - Design constraint (deliberate): auto-fixing BROKEN sigs is rejected because a wrong fuzzy-candidate match would silently hook the wrong function at runtime — worse than crashing. Only validation is automated; fixes stay human-reviewed.
- CI limitation for signature validation: GitHub Actions runners can't access local Steam installs, and DLLs aren't in SteamTracking; running the validator in CI requires SteamCMD/DepotDownloader with credentials as secrets. An intermediate ephemeral skill at `~/.claude/skills/update-deadlock-sigs/` was created then deleted during session in favor of deferring to CI-based detection.
- All three update workflows share identical scheduling (daily 06:00 UTC) and PR-creation mechanics — consistency is intentional so all upstream syncs land as reviewable PRs on the same morning cadence.
