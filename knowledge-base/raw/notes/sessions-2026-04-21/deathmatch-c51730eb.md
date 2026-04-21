---
date: 2026-04-21
task: session extract — deathmatch c51730eb
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-deathmatch/c51730eb-c0f6-489b-9156-8671871565ad.jsonl]
---

## Deadworks runtime

- `DeadworksPluginBase` exposes `Timer` as an **instance** property (not static):
  `protected ITimer Timer => TimerResolver.Get(this);` at
  `/home/manuel/deadlock/deadworks/managed/DeadworksManaged.Api/DeadworksPluginBase.cs:13`.
  Same pattern added for `Logger` in commit `deb8ff2` (OpenTelemetry rework):
  `protected ILogger Logger => LogResolver.Get(this);`. Consequence: plugin
  helpers that need `Timer`/`Logger` cannot be `static` — they must be instance
  methods, else CS0120 "An object reference is required".
- Upstream `deb8ff2` on deadworks added structured logging + OpenTelemetry:
  dual-sink (NativeEngineLoggerProvider + OTLP), per-plugin `ILogger` via
  `LogResolver`/`PluginLoggerRegistry`, 19 metrics on `Meter("Deadworks.Server")`,
  `ActivitySource` traces on lifecycle, all gated by `DEADWORKS_*` env vars
  (off by default; enable via `DEADWORKS_TELEMETRY_ENABLED=true`). Also
  migrated Console.WriteLine → ILogger across managed codebase.
- Upstream `38a35cc` ("remove signonstate hooks", Apr 17 2026) deleted
  `SendNetMessage.cpp`/`.hpp`, `deadworks.vcxproj` entries, 47 lines of
  `src/Core/Deadworks.cpp`, `ManagedCallbacks.*` bits,
  `managed/EntryPoint.cs` (23 lines), `managed/PluginLoader.cs` (9 lines),
  and `IDeadworksPlugin`/`DeadworksPluginBase` signonstate members. Total 134
  deletions across 13 files.

## Plugin build & deployment

- `/home/manuel/deadlock/deadworks/docker/build-native.sh:149` still listed
  `${SRC}/Core/Hooks/SendNetMessage.cpp` after upstream `38a35cc` removed the
  file; the fork `raimannma/deadworks@5d663ad` never synced this. clang-cl
  fails with `no such file or directory: 'deadworks/src/Core/Hooks/SendNetMessage.cpp'`.
  Fix lives in the fork, not the plugins repo. Pattern: whenever upstream
  removes a hook source, the fork's `docker/build-native.sh` needs a matching
  edit (e.g. `b451069` "fix docker build: add CheckTransmit.cpp" did the
  opposite for an add).
- Deadworks uses **two** remotes on `/home/manuel/deadlock/deadworks/`:
  `origin = Deadworks-net/deadworks` (upstream), `fork = raimannma/deadworks`.
  The GitHub Actions workflow `/home/manuel/deadlock/deadlock-deathmatch/.github/workflows/docker.yml`
  explicitly checks out `raimannma/deadworks` (not upstream) at
  `actions/checkout@v6` step "Checkout deadworks host", then mounts the
  plugins repo as a build-context named `extra-plugins` pointing at
  `deathmatch/plugins`. The Dockerfile at `deadworks/docker/Dockerfile:108`
  then walks `extra-plugins/*/*.csproj` and runs `dotnet publish -c Release
  -o /artifacts/managed/plugins --no-self-contained`, with an
  auto-generated `extra-plugins/Directory.Build.targets` setting
  `AssemblySearchPaths=$(AssemblySearchPaths);/artifacts/managed` so plugin
  csprojs can resolve `DeadworksManaged.Api` from the already-published
  managed output.
- Deadworks native cross-compile uses clang-cl targeting
  `x86_64-pc-windows-msvc` via xwin headers (`/xwin/crt/include`,
  `/xwin/sdk/include/{ucrt,um,shared}`). C++23 flag is forced through
  `-Xclang -std=c++23` because clang-cl's `/std:c++23` doesn't set
  `__cplusplus` correctly and breaks MSVC STL (comment in `build-native.sh`
  around the `CXX23_FLAGS` array). `/D__restrict=` empties the `__restrict`
  keyword. Protobuf is compiled C++17 separately, Zydis as C (`/TC`).
  Project includes pull from `sourcesdk/public/{tier0,tier1,appframework,
  mathlib,entity2,engine}`, `sourcesdk/game/shared`, `sourcesdk/common`,
  plus `/protobuf-src/src` and `/nethost`.
- Fix applied in this repo (`caa9b05`): `HealToFull` and `TryHeal` in
  `/home/manuel/deadlock/deadlock-server-plugins/plugins/DeathmatchPlugin/DeathmatchPlugin.cs:282,290`
  switched from `private static` to `private` so they can read the
  instance `Timer` property from `DeadworksPluginBase`.
