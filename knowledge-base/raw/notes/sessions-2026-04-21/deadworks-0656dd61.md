---
date: 2026-04-21
task: session extract — deadworks 0656dd61
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/0656dd61-f6e8-43af-b253-cc51c478fb27.jsonl]
---

## Deadworks runtime

- The deadworks fork tracks the `fork/main` remote (not `origin/main`); local `main` is normally in sync with `fork/main`. Upstream `origin/main` diverges — session shows fork ahead 8 / behind-diverged 26 vs upstream after rebase.
- `sourcesdk` is a git submodule; `scripts/update-protos.py` (fork variant) hard-fails if not initialized and calls `git submodule update --init`. Upstream variant instead downloads protoc 3.21.12 as a fallback (`PROTOC_VERSION = "3.21.12"`).
- `scripts/update-protos.py` fetches `.proto` files from `https://github.com/SteamTracking/GameTracking-Deadlock.git`, compiles some to C++ (`protobuf/*.pb.cc/h`) and copies others to `managed/protos/*.proto`. Fork's CPP_PROTOS list is a strict subset of upstream's — upstream additionally compiles `base_gcmessages`, `citadel_clientmessages`, `citadel_gcmessages_common`, `citadel_usermessages`, `gameevents`, `gcsdk_gcmessages`, `steammessages*`, `valveextensions` (conflict diff at session line 14).
- Upstream deleted those generated `protobuf/*.pb.h` / `*.pb.cc` from the tree (commit `d7d9675` "update protobuf definitions from upstream" tried to modify files the fork HEAD had already deleted — resolved by `git rebase --skip`). Implication: upstream now generates protobuf C++ at build time rather than committing the outputs.
- Upstream PR #6 (`4ce1fa2` "CI Workflow to update Games Files") independently re-introduces both `scripts/update-protos.py` and `scripts/update-game-exported.py`, which the fork had already added in `148cfaa` / `0d8ff0c`. Rebasing produces add/add conflicts on both; the resolution was `git checkout --ours` for each (keep fork variants) — so the fork deliberately retains the narrower protoc-from-submodule flow and its own game-exported script.

## Plugin build & deployment

- Fork carries 8 Docker/CI commits on top of upstream (session line 33, reapplied after rebase):
  - `ccf178d` Dockerfile + build scripts cross-compiling `deadworks.exe`
  - `41795e0` Dockerfile/plugin-management refactor
  - `070d27c` GHA workflow for Docker build+push
  - `2d13b83` README Docker hosting docs
  - `00da037` GHA workflow to update `sourcesdk` submodule
  - `7ca6abd` script validating Deadlock game DLL signatures
  - `d6f28aa` HLTV support with configurable broadcast settings in `entrypoint.sh` + `docker-compose`
  - `7916f76` `.env` to `.gitignore`
- These are the fork-specific additions that make deadworks hostable in Docker (separate from the upstream dev-oriented build). Anything touching `entrypoint.sh`, Dockerfile, or `docker-compose.yml` lives only on the fork.
- Rebase gotcha: fork's `b0ff012` was reported as "zuvor angewendeten Commit übersprungen" (already applied), meaning upstream absorbed an equivalent change — candidate for deletion from fork history on next rebase to reduce divergence.
- `scripts/__pycache__/` is untracked but not gitignored in the fork — minor hygiene item.
