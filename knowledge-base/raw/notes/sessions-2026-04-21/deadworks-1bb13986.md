---
date: 2026-04-21
task: session extract — deadworks 1bb13986
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/1bb13986-3257-431a-ae94-dfce9c035e87.jsonl]
---

## Source 2 engine

- Valve's SteamTracking/GameTracking-Deadlock is the canonical upstream mirror of Deadlock's `.proto` files; deadworks' `scripts/update-protos.py` clones it via `REPO_URL = "https://github.com/SteamTracking/GameTracking-Deadlock.git"` to regenerate protobuf bindings.
- Full per-game Source 2 proto sets live under `sourcesdk/thirdparty/game_protobufs/{artifact,csgo,deadlock}/`; the Deadlock subdir contains the Deadlock-specific `citadel_*.proto` family (e.g. `citadel_gcmessages_server.proto`, `citadel_gamemessages.proto`, `citadel_usercmd.proto`, `citadel_gameevents.proto`) alongside shared Source 2 protos (`netmessages`, `networkbasetypes`, `network_connection`, `gameevents`, `usermessages`, `demo`, `te`, `prediction_events`).

## Deadworks runtime

- C++ codegen surface was deliberately shrunk to just four protos: `netmessages`, `network_connection`, `networkbasetypes`, `source2_steam_stats`. This is encoded in `scripts/update-protos.py` `CPP_PROTOS` (around lines 25-36) and mirrored by `SOURCESDK_NEEDED_PROTOS` at `sourcesdk/CMakeLists.txt:66-74`. Protos like `networksystem_protomessages`, `gameevents`, and `usermessages` used to be in the list and were removed because deadworks' C++ side no longer needs them — only managed/C# code consumes those.
- Managed (C#) side has a separate list beyond `CPP_PROTOS`, copied into `managed/protos/`. Observed additions include `citadel_clientmessages`, `citadel_gcmessages_common`, `citadel_usermessages` — i.e. Deadlock-specific GC/client messages are handled only on the managed side, not compiled to C++.
- `update-protos.py`'s protoc discovery first looks for protoc inside the `sourcesdk` submodule (built from source) and only falls back to downloading protoc 3.21.12 if not found.
- Repo uses git submodules named "Protocol Buffers" and "Game Protobufs" (see `.gitmodules`); `sourcesdk` itself is a submodule whose `CMakeLists.txt` controls the C++ proto build, so any proto-list change needs to land in the upstream `sourcesdk` repo rather than being patched in-tree.

## Plugin build & deployment

- Removing a proto from `CPP_PROTOS` must be paired with deleting the corresponding `protobuf/<name>.pb.cc` and `protobuf/<name>.pb.h` checked-in files — these are committed generated artifacts, not build output. Example deletion set from this session: `base_gcmessages`, `citadel_clientmessages`, `citadel_gcmessages_common`, `citadel_usermessages`, `gameevents`, `gcsdk_gcmessages`, `networksystem_protomessages`, `steammessages`, `steammessages_steamlearn.steamworkssdk`, `steammessages_unified_base.steamworkssdk`, `valveextensions` (all `.pb.cc` + `.pb.h` pairs under `protobuf/`).
- Two daily GitHub Actions workflows drive game-file freshness, both creating PRs rather than direct commits:
  - `.github/workflows/update-protos.yml` runs `scripts/update-protos.py` to regenerate protobuf from GameTracking-Deadlock.
  - `.github/workflows/update-game-exported.yml` runs `scripts/update-game-exported.py` to pull `.gameevents` exported files.
- A third related workflow `add GitHub Actions workflow to update sourcesdk submodule` (commit 2869bb2) exists on main — submodule bumps are themselves automated, so the proto update pipeline is: sourcesdk submodule workflow → update-protos workflow → PR.
- CI jobs must checkout with `submodules: recursive` because protoc and the proto sources both live inside `sourcesdk`.
- Deadworks repo has two remotes in practice: `origin` = `Deadworks-net/deadworks` (canonical), `fork` = contributor's fork (e.g. `raimannma/deadworks`); feature branches like `ci/update-game-files` track the fork while PRs target `Deadworks-net/deadworks`. Rebasing onto `origin/main` (not `main`) is required when the local `main` lags behind upstream.
