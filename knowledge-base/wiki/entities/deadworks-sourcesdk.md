---
title: sourcesdk submodule
type: entity
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-0656dd61.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-1bb13986.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-32c45101.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-749ada0d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-bf8a9ef6.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-ec2918a5.md
related:
  - "[[deadworks-runtime]]"
  - "[[protobuf-pipeline]]"
  - "[[deadworks-mem-jsonc]]"
created: 2026-04-21
updated: 2026-04-21
confidence: high
---

# sourcesdk submodule

The **`sourcesdk`** git submodule on the deadworks repo is the single
source of truth for Source 2 protobuf definitions, protoc binaries, and
(partially) the SDK headers/libs used to cross-compile `deadworks.exe`.

## Origin

- URL: `https://github.com/Deadworks-net/sourcesdk.git` (a Deadworks-net
  fork of the Source 2 SDK, NOT a Valve URL) (deadworks-bf8a9ef6).
- Configured in `.gitmodules` as the `sourcesdk` entry.
- Submodules must be initialized recursively in CI:
  `actions/checkout@v6` with `submodules: recursive`
  (deadworks-1bb13986, deadworks-32c45101).
- Without the submodule initialized, `scripts/update-protos.py` (the fork
  variant) hard-fails with "Run: `git submodule update --init`"
  (deadworks-0656dd61). The upstream variant falls back to downloading
  protoc 3.21.12.

## What it ships

```
sourcesdk/
├── devtools/bin/{win64,osx64,linuxsteamrt64}/protoc[.exe]  # protoc 3.21.8
├── thirdparty/protobuf/src/                                 # protobuf headers
│   └── google/protobuf/descriptor.proto
├── thirdparty/game_protobufs/
│   ├── artifact/
│   ├── csgo/
│   └── deadlock/                                           # citadel_*.proto family
├── cmake/sourcesdk/proto/generate.cmake
├── cmake/protobuf.cmake
├── public/{tier0,tier1,appframework,mathlib,entity2,engine}/
├── game/shared/
├── common/
└── lib/win64/tier0.lib
```

- `protoc.exe` at `devtools/bin/win64/protoc.exe` — version **`libprotoc
  3.21.8`**. Committed `.pb.h` files check `#if PROTOBUF_VERSION < 3021000`
  so **3.21.x is the required line** (deadworks-32c45101).
- The `linuxsteamrt64` variant is the Steam Runtime build used by Source 2
  tooling (deadworks-bf8a9ef6).
- The Deadlock `.proto` set under `thirdparty/game_protobufs/deadlock/`
  includes `citadel_gcmessages_server.proto`, `citadel_gamemessages.proto`,
  `citadel_usercmd.proto`, `citadel_gameevents.proto`, etc., alongside
  shared Source 2 protos (`netmessages`, `networkbasetypes`,
  `network_connection`, `gameevents`, `usermessages`, `demo`, `te`,
  `prediction_events`) (deadworks-1bb13986).

## Upstream proto source

`.proto` files are mirrored from
`https://github.com/SteamTracking/GameTracking-Deadlock.git` (Protobufs/
dir). The deadworks sourcesdk's deadlock protos track this upstream
(deadworks-1bb13986, deadworks-32c45101).

- NOT `SteamTracking/Protobufs` (mixed Steam/Valve), NOT
  `SteamDatabase/Protobufs`.
- Proto files have no explicit `syntax` declaration → default to proto2.

## Role as build-time source of truth

Post-commit `a92d744` ("remove pre-compiled protobuf sources, generate
from sourcesdk submodule at build time"; deadworks-749ada0d,
deadworks-ec2918a5):

- Vendored `.pb.cc` / `.pb.h` under top-level `protobuf/` were deleted
  (365,828-line deletion).
- New file `deadworks/protobuf.targets` (MSBuild targets fragment)
  imported after `Microsoft.Cpp.targets` in the vcxproj:
  - `ProtocExe` = `sourcesdk\devtools\bin\win64\protoc.exe`
  - `ProtobufSdkInclude` = `sourcesdk\thirdparty\protobuf\src`
  - `ProtoSrcDir` = `sourcesdk\thirdparty\game_protobufs\deadlock`
  - `ProtoOutDir` = `$(IntDir)protobuf\`
  - `GenerateProtobufs` target with `BeforeTargets="ClCompile"` and
    `Inputs/Outputs` for incremental regeneration.
  - `.pb.cc` files must also be added to ClCompile at build time (not
    statically in vcxproj).
- Docker Linux build: `docker/Dockerfile` now downloads host-native Linux
  `protoc 3.21.12` via release zip as its build-time protoc; the
  `libprotobuf.lib` runtime is still 3.21.8 from the xwin cross-compile.
- `local.props.example`'s `ProtobufIncludeDir` can now be removed —
  includes resolve via the submodule path.
- `managed/protos/` directory was deleted; `<Protobuf>` items in
  `DeadworksManaged.Api.csproj` point directly at
  `sourcesdk/thirdparty/game_protobufs/deadlock/`.

## Fork vs upstream divergence history

Prior state (pre-`a92d744`; deadworks-0656dd61, deadworks-749ada0d):

- Fork (`raimannma/deadworks`) carried `148cfaa` ("add protobuf auto-update
  script and GitHub Actions workflow") and `d7d9675` ("chore: update
  protobuf definitions from upstream") — pulling from
  `SteamTracking/GameTracking-Deadlock` directly.
- Upstream commit `a92d744` obsoletes both. The fork's auto-update
  commits are now semantically obsolete.
- Fork's `CPP_PROTOS` list was narrower than upstream's; upstream
  additionally compiled `base_gcmessages`, `citadel_clientmessages`,
  `citadel_gcmessages_common`, `citadel_usermessages`, `gameevents`,
  `gcsdk_gcmessages`, `steammessages*`, `valveextensions`.

See [[protobuf-pipeline]] for the detailed pipeline evolution.

## Known commits

- `a92d744` — remove pre-compiled protobuf, generate from sourcesdk at
  build time
- `3bf9486` — chore: reduce C++ protobuf codegen to essential protos only
  (deadworks-61a39dd5) — trimmed `networksystem_protomessages` etc.
- `e92583d` — add CI workflows to auto-update game exported files and
  protobufs (deadworks-61a39dd5)

## Related tools

- [[deadworks-mem-jsonc]] — memory signature layout file, validated via
  `scripts/validate-signatures.py`.
- `scripts/update-protos.py` (now removed/obsolete in favour of
  `protobuf.targets`).
- `scripts/update-game-exported.py` — mirrors `.gameevents` files from
  GameTracking-Deadlock.
