---
date: 2026-04-21
task: session extract — deadworks bf8a9ef6
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/bf8a9ef6-c5ac-4c17-a2b4-a417ed47be24.jsonl]
---

## Deadworks runtime

- Repo contains its own protobuf pipeline at `scripts/update-protos.py`: .proto files are fetched from upstream `https://github.com/SteamTracking/GameTracking-Deadlock.git` (shallow + sparse-checkout of `Protobufs/`), then used two ways: (a) compiled to C++ (`.pb.cc/.pb.h`) into `protobuf/` for the native runtime, and (b) copied verbatim into `managed/protos/` for the managed side.
- Two distinct proto sets are maintained: `CPP_PROTOS` (C++ compiled) = `netmessages`, `network_connection`, `networkbasetypes`, `source2_steam_stats`; `EXTRA_MANAGED_PROTOS` (copied only) = `citadel_gameevents`, `citadel_usercmd`, `te`, `usercmd`, `usermessages`. The managed-proto copy step iterates `managed/protos/*.proto` already-present and only updates those that still exist upstream — so removing a managed proto file is how you deprecate it from the update flow.
- `protoc` binary + includes come from the `sourcesdk` git submodule, not a system install. Platform paths: `sourcesdk/devtools/bin/{win64|osx64|linuxsteamrt64}/protoc[.exe]` with includes at `sourcesdk/thirdparty/protobuf/src`. The submodule's `linuxsteamrt64` variant is the Steam Runtime build used by Source 2 tooling.
- Submodule origin: `.gitmodules` points `sourcesdk` at `https://github.com/Deadworks-net/sourcesdk.git` (a Deadworks-net fork of the Source 2 SDK), not a Valve URL.

## Plugin build & deployment

- Protoc compile invocation uses dual `-I` flags: `-I{sourcesdk/thirdparty/protobuf/src} -I{upstream Protobufs dir}` with `--cpp_out=protobuf/`, `cwd` set to the upstream clone so relative proto imports resolve. See `compile_cpp_protos()` in `scripts/update-protos.py`.
- CI workflow `.github/workflows/update-protos.yml` runs daily at `0 6 * * *` UTC, checks out with `submodules: recursive` (line 19) so sourcesdk is available for the protoc resolve, then uses `peter-evans/create-pull-request@v8` to open a `chore/update-protos` PR with `delete-branch: true`. Needs `contents: write` + `pull-requests: write`.
- The script deliberately has no download fallback for protoc after this session's simplification — if `sourcesdk` submodule is missing it exits with "Run: git submodule update --init". Previously there was a `download_protoc()` fetching `protoc 3.21.12` release zips from github.com/protocolbuffers/protobuf, kept as a CI/contributor fallback; removed because CI already initializes the submodule and contributors need it for builds anyway.
- Other workflows in `.github/workflows/`: `build.yml`, `update-game-exported.yml`, `update-protos.yml` — suggests a parallel auto-update pipeline for exported game files in addition to protos.
