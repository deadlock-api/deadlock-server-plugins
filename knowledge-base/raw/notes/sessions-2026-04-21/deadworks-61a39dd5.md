---
date: 2026-04-21
task: session extract — deadworks 61a39dd5
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/61a39dd5-f6df-4a12-9a6d-b378018b5fc7.jsonl]
---

## Plugin build & deployment

- Commit `3bf9486 chore: reduce C++ protobuf codegen to essential protos only` trimmed the deadworks `protobuf/` directory: `networksystem_protomessages.pb.{cc,h}` (and its entry in `scripts/update-protos.py`) were dropped as "non-essential", but the user later wanted it restored — it is in fact referenced by other generated units.
- `scripts/update-protos.py` has a `CPP_PROTOS` list controlling which `.proto` files get compiled to `.pb.{cc,h}` in `protobuf/`. Ordering is alphabetical; new entries slot between `networkbasetypes` and `source2_steam_stats` (e.g. restore step inserted `"networksystem_protomessages"` at that position).
- `networksystem_protomessages.proto` defines 5 net messages: `NetMessageSplitscreenUserChanged` (slot), `NetMessageConnectionClosed` / `NetMessageConnectionCrashed` (reason + message), `NetMessagePacketStart`, `NetMessagePacketEnd` — all low-level Source 2 connection-lifecycle signalling, not gameplay messages.
- Two CI workflows auto-refresh this generated content: `.github/workflows/update-game-exported.yml` (runs `scripts/update-game-exported.py`) and `.github/workflows/update-protos.yml` (runs `scripts/update-protos.py`). Commit `e92583d` ("add CI workflows to auto-update game exported files and protobufs") introduced both script/workflow pairs together — i.e. the CI and the generator script are treated as one logical unit when squashing.
- Gotcha: restoring a deleted generated `.pb.cc`/`.pb.h` pair via `git checkout HEAD~2 -- <path>` only brings back the file contents; you must separately re-add the proto name to `CPP_PROTOS` in `scripts/update-protos.py`, otherwise the next CI run will delete it again.
