---
title: Protobuf pipeline
type: entity
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-0656dd61.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-1bb13986.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-32c45101.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-3beeff54.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-61a39dd5.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-749ada0d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-bf8a9ef6.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-ec2918a5.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-fa5d1d7e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-3636296d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-90226db4.md
related:
  - "[[deadworks-sourcesdk]]"
  - "[[deadworks-runtime]]"
  - "[[plugin-build-pipeline]]"
created: 2026-04-21
updated: 2026-04-21
confidence: high
---

# Protobuf pipeline

How Source 2 / Deadlock `.proto` files become compiled types used by both
the native `deadworks.exe` and the C# managed layer.

## Evolution (three eras)

### Era 1 — vendored `.pb.cc/.pb.h` (historical)

(deadworks-32c45101, deadworks-749ada0d, deadworks-ec2918a5)

- Pre-generated C++ (`.pb.cc`/`.pb.h`) committed in top-level `protobuf/`
  (~30 files, ~367K LOC).
- Managed `.proto` files committed in `managed/protos/` (19 files, strict
  subset + a few extras).
- `deadworks.vcxproj` (MSVC/Windows) consumed the pre-compiled files.
- `docker/Dockerfile` also had `COPY protobuf protobuf` — Linux build was
  not actually running protoc despite CMake wiring.
- Committed `.pb.h` files showed a mix of two protoc outputs: some
  (e.g. `base_gcmessages.pb.h`) produced by a Valve-patched protoc that
  emits `/*final*/` comments instead of the `final` keyword; others
  (e.g. `gameevents.pb.h`) produced by standard protoc with real `final`.
  Cosmetic only — `.pb.cc` bodies identical (deadworks-32c45101).

### Era 2 — fork's auto-update workflow (obsolete)

(deadworks-0656dd61, deadworks-1bb13986, deadworks-32c45101,
deadworks-749ada0d)

Fork `raimannma/deadworks` carried two commits: `148cfaa` "add protobuf
auto-update script and GitHub Actions workflow" + `d7d9675` "chore:
update protobuf definitions from upstream".

- `scripts/update-protos.py` fetches `.proto` files from
  `https://github.com/SteamTracking/GameTracking-Deadlock.git` (shallow +
  sparse-checkout of `Protobufs/`).
- Two parallel proto sets:
  - **CPP_PROTOS** — compiled to `.pb.cc/.pb.h` in `protobuf/`.
    Fork list: `netmessages`, `network_connection`, `networkbasetypes`,
    `source2_steam_stats` (4 protos). Upstream list was larger — fork
    deliberately retained a narrower flow.
  - **EXTRA_MANAGED_PROTOS** / managed-only — copied verbatim into
    `managed/protos/`: `citadel_gameevents`, `citadel_usercmd`, `te`,
    `usercmd`, `usermessages`, plus Deadlock-specific GC/client messages
    (`citadel_clientmessages`, `citadel_gcmessages_common`,
    `citadel_usermessages`).
- Protoc discovery looks in sourcesdk submodule first
  (`sourcesdk/devtools/bin/<platform>/protoc[.exe]`), falls back to
  downloading protoc 3.21.12 (later simplified to require the submodule).
- Dual `-I` for well-known types: `-Isourcesdk/thirdparty/protobuf/src`
  + `-I<upstream Protobufs dir>`.
- CI: `.github/workflows/update-protos.yml` runs daily at `0 6 * * *` UTC,
  opens PR on `chore/update-protos` via
  `peter-evans/create-pull-request@v8` with `delete-branch: true`
  (deadworks-bf8a9ef6, deadworks-1bb13986).

Obsoleted by commit `a92d744` (deadworks-749ada0d).

### Era 3 — build-time generation from sourcesdk (current)

Upstream commit **`a92d744`** "remove pre-compiled protobuf sources,
generate from sourcesdk submodule at build time" (deadworks-749ada0d,
deadworks-ec2918a5):

- **All vendored `protobuf/*.pb.cc/*.pb.h` deleted.** (365,828-line
  deletion.)
- `managed/protos/` deleted; `<Protobuf>` items in
  `DeadworksManaged.Api.csproj` point directly at
  `sourcesdk/thirdparty/game_protobufs/deadlock/`.
- New `deadworks/protobuf.targets` MSBuild fragment imported after
  `Microsoft.Cpp.targets` in the vcxproj:
  - Defines `ProtocExe`, `ProtobufSdkInclude`, `ProtoSrcDir` (=
    `sourcesdk\thirdparty\game_protobufs\deadlock`), `ProtoOutDir` (=
    `$(IntDir)protobuf\`).
  - `GenerateProtobufs` target with `BeforeTargets="ClCompile"` using
    `Inputs/Outputs` for incremental regeneration.
  - `.pb.cc` files added to `ClCompile` dynamically at build time.
- vcxproj `AdditionalIncludeDirectories`: replaced
  `$(ProjectDir)..\protobuf` with `$(ProtoOutDir)`.
- Docker Linux build: `docker/Dockerfile` downloads host-native Linux
  `protoc 3.21.12` via release zip to `/usr/local`. Windows protobuf
  library build (3.21.8 via xwin/clang-cl) retained for
  `libprotobuf.lib` linkage. `COPY protobuf protobuf` removed.
- `docker/build-native.sh` gained `PROTO_SRC="sourcesdk/thirdparty/game_protobufs/deadlock"`
  + `PROTO_OUT="generated/protobuf"`; `/Iprotobuf` → `/I${PROTO_OUT}`.
- `scripts/update-protos.py` and `.github/workflows/update-protos.yml`
  removed — `sourcesdk` submodule is the single source of truth.
  `protobuf/` added to `.gitignore`.
- `local.props.example`'s `ProtobufIncludeDir` can be removed.

**Version gotcha**: Windows protobuf runtime build (3.21.8) and host
Linux `protoc` (3.21.12) are deliberately different versions. Runtime/
generator ABI compat within the 3.21.x line is relied upon; bumping
either independently may break (deadworks-ec2918a5).

## Which protos matter

- The native deadworks DLL consumes **14 C++ protos**
  (deadworks-ec2918a5): `base_gcmessages`, `citadel_clientmessages`,
  `citadel_gcmessages_common`, `citadel_usermessages`, `gameevents`,
  `gcsdk_gcmessages`, `netmessages`, `network_connection`,
  `networkbasetypes`, `networksystem_protomessages`, `steammessages`,
  `steammessages_steamlearn.steamworkssdk`,
  `steammessages_unified_base.steamworkssdk`, `valveextensions`.
- `source2_steam_stats` was previously checked in but is NOT used by any
  deadworks source and not in the deadlock proto dir — dropped.
- `networksystem_protomessages` was trimmed once (commit `3bf9486`,
  deadworks-61a39dd5) then restored; defines 5 low-level Source 2
  connection-lifecycle messages: `NetMessageSplitscreenUserChanged`,
  `NetMessageConnectionClosed/Crashed`, `NetMessagePacketStart`,
  `NetMessagePacketEnd`.

## Managed side via `DeadworksManaged.Api.csproj`

(deadworks-d416f1ea, deathmatch-3636296d, deathmatch-fa5d1d7e)

```xml
<PackageReference Include="Grpc.Tools" Version="2.69.0" />
<PackageReference Include="Google.Protobuf" Version="3.29.3" />
<Protobuf Include="..\protos\**\*.proto"
          ProtoRoot="..\protos"
          GrpcServices="None" />
```

After Era 3, `<Protobuf>` points directly at `sourcesdk/thirdparty/game_protobufs/deadlock/`.

- `Grpc.Tools` 2.69.0 provides codegen at build time.
- `Google.Protobuf` 3.29.3 is the managed runtime.
- Both must be shared with plugins (via [[deadworks-plugin-loader]]'s
  SharedAssemblies map) to avoid duplicate-assembly identity issues.
- Generated C# proto classes land in
  `DeadworksManaged.Api/obj/Debug/net10.0/*.cs`
  (e.g. `CitadelUsermessages.cs` containing
  `CCitadelUserMsg_HudGameAnnouncement`).

## Plugin use of protobuf types

Plugins that call `NetMessages.Send<T>` (constrained to `T : IMessage<T>`)
need `Google.Protobuf` at compile time. The `DeadworksManaged.Api` DLL
alone is NOT enough — its build output does not ship `Google.Protobuf.dll`
as a loose file (deathmatch-3636296d).

**Local dev**: transitive ProjectReference through sibling
`DeadworksManaged.Api` csproj resolves the package.

**Docker CI**: links against the already-published
`/artifacts/managed/DeadworksManaged.Api.dll` and does NOT see the
transitive package. Causes CS0311 + cascading CS0246 for unrelated types.

**Fix** added to plugin csprojs (server-plugins-90226db4,
deathmatch-fa5d1d7e):

```xml
<PackageReference Include="Google.Protobuf" Version="3.29.3"
                  Private="false" ExcludeAssets="runtime" />
```

`Private=false` + `ExcludeAssets=runtime` matches the pattern for
`DeadworksManaged.Api` — compile-time only, host provides the runtime
copy.

## Auto-update workflows (current)

Two daily 06:00 UTC GitHub Actions workflows in deadworks
(deadworks-1bb13986, deadworks-d75e1c40):

- `.github/workflows/update-game-exported.yml` — runs
  `scripts/update-game-exported.py` (shallow + sparse-clone
  `SteamTracking/GameTracking-Deadlock`, copies `.gameevents` files to
  `game_exported/`).
- `.github/workflows/update-sourcesdk.yml` — bumps the `sourcesdk`
  submodule to track its upstream `main`.

Both use `peter-evans/create-pull-request@v8`. Consistency is intentional
so all upstream syncs land as reviewable PRs on the same morning cadence.
Opens PRs via `peter-evans/create-pull-request@v8` — never direct
commits.
