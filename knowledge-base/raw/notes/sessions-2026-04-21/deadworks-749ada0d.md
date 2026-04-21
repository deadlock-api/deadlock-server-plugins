---
date: 2026-04-21
task: session extract — deadworks 749ada0d
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/749ada0d-3679-4366-8d49-a2f575fe4f30.jsonl]
---

## Deadworks runtime

- Protobuf sources in deadworks were historically vendored as both `.proto` files under `managed/protos/` and pre-compiled `.pb.cc`/`.pb.h` under `protobuf/`. Commit `a92d744` ("remove pre-compiled protobuf sources, generate from sourcesdk submodule at build time") deletes all of them in favor of build-time generation — the raimannma fork tried to maintain an auto-update workflow against the old vendored copies which is now incompatible.
- Full vendored proto set (all removed by `a92d744`, per rebase output): `base_gcmessages`, `citadel_clientmessages`, `citadel_gameevents`, `citadel_gcmessages_common`, `citadel_usercmd`, `citadel_usermessages`, `gameevents`, `gcsdk_gcmessages`, `netmessages`, `network_connection`, `networkbasetypes`, `networksystem_protomessages`, `source2_steam_stats`, `steammessages`, `steammessages_steamlearn.steamworkssdk`, `steammessages_unified_base.steamworkssdk`, `te`, `usercmd`, `usermessages`, `valveextensions` — these are the Source 2 / Deadlock (Citadel) wire-protocol schemas deadworks depends on.

## Plugin build & deployment

- `a92d744` introduces a new file `deadworks/protobuf.targets` (MSBuild targets fragment) that drives the build-time protobuf generation. Combined with the 365,828-line deletion (fully vendored generated C++), the sourcesdk git submodule is now the single source of truth for proto definitions, regenerated on each build.
- raimannma fork (`git@github.com:raimannma/deadworks.git`, remote name `fork`) maintained two commits — `148cfaa` "add protobuf auto-update script and GitHub Actions workflow" and `d7d9675` "chore: update protobuf definitions from upstream" — that are semantically obsoleted by the submodule-driven approach. Dropping them via `git rebase --onto 8981bfd d7d9675` produces delete/modify conflicts on every `.proto` and `.pb.*` file because the auto-update commits modify files that the later commit deletes; resolution is `git rm` on all conflicted paths.
- Commit order on raimannma/main at session start: `c513a7f` (Docker CI) → `8981bfd` (Docker hosting README) → `148cfaa` (proto auto-update script + workflow) → `d7d9675` (proto defs from upstream) → `0d8ff0c` (game-exported-updates workflow) → `a92d744` (drop vendored protos, add `protobuf.targets`).
