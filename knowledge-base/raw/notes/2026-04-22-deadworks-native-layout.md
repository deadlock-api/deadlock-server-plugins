---
date: 2026-04-22
task: scan deadworks native C++ source tree
files:
  - ../deadworks/deadworks/src/
  - ../deadworks/docker/build-native.sh
  - ../deadworks/docker/Dockerfile
---

# Native C++ source tree layout + Docker build gotchas

## Source tree (`deadworks/src/`)

```
Core/
  Hooks/
    AbilityThink.cpp
    AddModifier.cpp
    BuildGameSessionManifest.cpp
    CBaseEntity.cpp
    CCitadelPlayerController.cpp
    CCitadelPlayerPawn.cpp
    CServerSideClientBase.cpp
    CheckTransmit.cpp
    CoreHooks.cpp
    EntityIO.cpp
    GameEvents.cpp
    NetworkServerService.cpp
    PostEventAbstract.cpp
    ProcessUsercmds.cpp
    ReplyConnection.cpp
    Source2GameClients.cpp
    Source2Server.cpp
    TraceShape.cpp
  A2SPatch.cpp
  Deadworks.cpp
  ManagedCallbacks.cpp
  NativeAbility.cpp
  NativeCallbacks.cpp
  NativeDamage.cpp
  NativeHero.cpp
Hosting/
  DotNetHost.cpp
Lib/
Logging/
Memory/
  MemoryDataLoader.cpp
  Scanner.cpp
SDK/
  Interfaces.cpp
  Schema/Schema.cpp
Utils/
pch.{hpp,cpp}
startup.cpp
```

## Hook families (one cpp per family under Core/Hooks)

Notable hooks and what they do:
- `Source2Server.cpp` — top-level server frame / appsystem hooks
- `CoreHooks.cpp` — the `OnAppSystemLoaded` installer (called before
  `Source2Main` handoff); installs the remaining ~30 hooks
- `GameEvents.cpp` — engine `IGameEventManager2` fire-event interception
  → managed `DispatchGameEvent`
- `TakeDamage` (older path) — damage hook; superseded by
  `NativeDamage.cpp` in newer versions. Note: an older `TakeDamageOld.cpp`
  was removed upstream at some point
- `AddModifier.cpp` — modifier apply hook
- `AbilityThink.cpp` — per-tick ability processing (fires
  `OnAbilityAttempt`)
- `ProcessUsercmds.cpp` — raw usercmd dispatch
- `CheckTransmit.cpp` — per-player PVS calculation (fires
  `OnCheckTransmit`)
- `ReplyConnection.cpp` — connection accept/reject
- `BuildGameSessionManifest.cpp` — addon / resource manifest
- `EntityIO.cpp` — entity I/O firing
- `TraceShape.cpp` — `IPhysicsQuery::TraceShape` (VPhys2)
- `A2SPatch.cpp` — Steam A2S query response patch
- `CBaseEntity.cpp` / `CCitadelPlayerPawn.cpp` /
  `CCitadelPlayerController.cpp` — per-class hook clusters (spawn,
  think, take damage, etc.)
- `CServerSideClientBase.cpp` — client-state transitions
- `PostEventAbstract.cpp` — usermessage post
- `Source2GameClients.cpp` — ISource2GameClients vtable patches
- `NetworkServerService.cpp` — net-server-service level hooks

## Native/Managed surface split

- `NativeCallbacks.cpp` — functions EXPOSED to managed. These are the
  C exports the managed `NativeInterop` static delegates get populated
  with. If a plugin uses a method like `pawn.Hurt(damage, attacker)`,
  the path is: managed method → `NativeInterop.Hurt` function pointer →
  `NativeCallbacks.cpp` C function → engine call.
- `NativeDamage.cpp`, `NativeAbility.cpp`, `NativeHero.cpp` —
  subdomain-specific native helpers exposed to managed (likely through
  `NativeCallbacks`)
- `ManagedCallbacks.cpp` — functions managed-to-native CALLS. Mirror
  image: when the native side needs to fire into managed (event
  dispatch, frame tick, precache callback), it calls through these.

## Docker native build — `docker/build-native.sh`

**Hand-maintained source list** (lines 140-171). Every new `.cpp` file
under `deadworks/src/` must be appended here or the build will fail to
link with `undefined symbol`:

```bash
for f in \
    ${SRC}/startup.cpp \
    ${SRC}/Hosting/DotNetHost.cpp \
    ${SRC}/Core/Hooks/CoreHooks.cpp \
    # ... ~25 more files explicitly listed ...
    ${SRC}/SDK/Schema/Schema.cpp; do
    clang-cl "${PROJECT_FLAGS[@]}" /c "$f" "/Fo${OBJ}/${name}.obj"
done
```

This is a **fork-tracking hazard**: when upstream Deadworks adds a new
hook file, our fork's Dockerfile must be manually updated. Symptom is
a link error naming the missing symbol.

## clang-cl C++23 quirk

Lines 76-82:

```bash
# clang-cl's /std:c++23 doesn't set __cplusplus correctly, breaking the MSVC STL.
# Passing -Xclang -std=c++23 goes directly to the compiler frontend.
CXX23_FLAGS=(
    $TARGET /EHsc /MT /O2
    -Xclang -std=c++23
    /D__restrict=
    ...
)
```

Must use `-Xclang -std=c++23`, NOT `/std:c++23`. The msvc driver flag
fails to set `__cplusplus` to the expected value. Also the empty
`/D__restrict=` define is needed because the Source SDK's `bitbuf.h` and
similar have `__restrict` usage that conflicts with clang-cl's view of
the macro.

## Source SDK C++17 vs Deadworks C++23

Source SDK files (`entityidentity.cpp`, `entitykeyvalues.cpp`,
`entitysystem.cpp`, `convar.cpp`, `keyvalues3.cpp`) compile with
`/std:c++17`. Deadworks own code (and vendor's `safetyhook.cpp`) with
`-Xclang -std=c++23`. Protobuf also compiles with `/std:c++17`. Three
different C++ standards in one build.

## Link

`lld-link` produces `out/deadworks.exe`. Core libs:
- `tier0.lib` (from `sourcesdk/lib/win64/`)
- `libprotobuf.lib` (from protobuf-build stage)
- `libnethost.lib` (from nethost stage)
- Windows: `advapi32.lib`, `ole32.lib`
- xwin CRT/SDK libs

## Protobuf file pickup

Line 101: `for f in protobuf/*.pb.cc` — picks up `.pb.cc` from a
`protobuf/` directory. This is after the build-time `update-protos.py`
step (commit `a92d744`) that generates via the sourcesdk's protoc.
Earlier eras used vendored files at this path directly.
