---
date: 2026-04-21
task: session extract — deadworks 32c45101
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/32c45101-4201-47c6-91cd-5b2aaddd3d51.jsonl]
---

## Deadworks runtime

- Protobuf sources live in two parallel trees in `deadworks`: `protobuf/*.pb.cc`/`.pb.h` (pre-generated C++ used by the native build) and `managed/protos/*.proto` (C# generation inputs). These are committed, not generated at build time.
- Managed C# proto set (19 files) is a strict subset of the C++ set plus a few extras; the maintained update script only copies `.proto` files that already exist in `managed/protos/` to avoid introducing unused deps (e.g., `networksystem_protomessages.proto` appears upstream but is intentionally not included).
- `sourcesdk` submodule (`https://github.com/Deadworks-net/sourcesdk.git`, per `.gitmodules`) is expected to hold `devtools/bin/<platform>/protoc` and `thirdparty/protobuf/src/google/protobuf/descriptor.proto` (well-known types include root). Without the submodule initialized, neither exists.
- The sourcesdk-bundled `protoc` is version `libprotoc 3.21.8`; committed `.pb.h` files check `#if PROTOBUF_VERSION < 3021000` so 3.21.x is the required line.
- CMake-based proto generation infra exists at `sourcesdk/cmake/sourcesdk/proto/generate.cmake` and `sourcesdk/cmake/protobuf.cmake` (defaults `${PROTOBUF_NAME}_BUILD_PROTOC_BINARIES OFF`) but is not wired into the day-to-day flow — pre-generated files are committed instead.

## Plugin build & deployment

- Committed `.pb.h` files in `protobuf/` are a mix of two protoc outputs: some (e.g., `base_gcmessages.pb.h`, `gcsdk_gcmessages.pb.h`) were produced by a Valve-patched protoc that emits `/*final*/` comments in place of the `final` keyword on classes and method overrides; others (e.g., `gameevents.pb.h`) were produced by a standard protoc and contain real `final` on both class decls and methods. Regenerating with the standard sourcesdk protoc 3.21.8 normalizes everything to real `final` keywords — purely cosmetic (the comment has no effect on compiled output, and `.pb.cc` bodies are identical).
- Upstream proto source chosen for auto-update is `SteamTracking/GameTracking-Deadlock` (Protobufs/ dir) — not `SteamTracking/Protobufs` (mixed Steam/Valve) nor `SteamDatabase/Protobufs` (used only indirectly via the `sourcesdk/thirdparty/game_protobufs` submodule which points at `deadlock/*.proto` there).
- Upstream `.proto` files in GameTracking-Deadlock have no explicit `syntax` declaration → default to proto2. Confirmed by inspecting raw file heads from a sparse clone.
- Native Windows build uses clang-cl with `$TARGET /TC /MT /O2 /DNDEBUG -fuse-ld=lld` and iterates `for f in protobuf/*.pb.cc` (see `docker/build-native.sh:~100`); XWIN toolchain supplies MSVC-compatible headers.
- New protobuf update script added at `scripts/update-protos.py` (cross-platform Python 3, no deps): shallow+sparse clones GameTracking-Deadlock `Protobufs/`, copies only `.proto` files already present in `managed/protos/`, compiles 15 files via local sourcesdk protoc (falls back to downloading protoc 3.21.12 if the submodule is absent), requires `-I sourcesdk/thirdparty/protobuf/src` for `google/protobuf/*` well-known types.
- GitHub Action at `.github/workflows/update-protos.yml` runs daily at 06:00 UTC + `workflow_dispatch`, uses `actions/checkout@v6`, `actions/setup-python@v6`, `peter-evans/create-pull-request@v8`; opens PR on `chore/update-protos` branch with `contents: write` + `pull-requests: write`.
- Main build workflow `.github/workflows/build.yml` runs on `windows-latest`, checks out with `submodules: recursive`, uses `actions/setup-dotnet@v5` for .NET 10.
- First auto-update run will produce a large noisy diff (~90% of header churn is `/*final*/` → `final`); subsequent runs will only show real proto content deltas.
- Commits from session: `7066796` (script + CI) then `7412059` (regenerated protos, 18 files, +6021/−5303) — deliberately split so the normalization churn lives in its own commit.
