---
date: 2026-04-21
task: session extract — deadworks 4972c10e
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/4972c10e-dbb2-4d45-b351-405fef24e0d0.jsonl]
---

## Deadworks runtime

- `deadworks::Deadworks::PostInit` wires up three hooks/patches whose TUs were absent from the Docker compile list, producing `lld-20: error: undefined symbol` for `hooks::Hook_ReplyConnection`, `hooks::Hook_CheckTransmit`, and `A2SPatch::Apply`.
- Hook implementations live at `deadworks/src/Core/Hooks/ReplyConnection.cpp`, `deadworks/src/Core/Hooks/CheckTransmit.cpp`; `A2SPatch` at `deadworks/src/Core/A2SPatch.cpp` — included via `Deadworks.cpp:22-24` (`ReplyConnection.hpp`, `CheckTransmit.hpp`, `A2SPatch.hpp`).
- Recent branch churn on hooks: `SendNetMessage.cpp/.hpp` deleted, `CheckTransmit.cpp/.hpp` added, "remove signonstate hooks" commit — means the set of hook TUs is unstable across rebases and the cross-compile manifest drifts easily.
- `Logger.hpp:18` and `S2Logger.hpp:24` have `switch (verbosity)` that does not handle the `Fatal` enum value (compiler warning `-Wswitch`).
- `Deadworks.hpp:121` declares `inline std::unique_ptr<Logger> g_Log;` but `Logger` is abstract with a non-virtual destructor — triggers `-Wdelete-abstract-non-virtual-dtor` from the MSVC STL's `default_delete`.

## Plugin build & deployment

- The native cross-compile list is a hand-maintained bash array in `docker/build-native.sh` (not auto-discovered). New hook `.cpp` files must be appended explicitly; missing entries only fail at link time, not compile.
- Fix landed as commit `67a1cd1` on branch `docker-build`: added `Core/Hooks/ReplyConnection.cpp`, `Core/Hooks/CheckTransmit.cpp`, `Core/A2SPatch.cpp` before `Core/Deadworks.cpp` in `build-native.sh`.
- Plugin sources moved from `managed/plugins/` to `examples/plugins/` (commit `189cf2a` "split plugins to new examples solution"). `managed/plugins/` still exists as a build artifact dir but the source tree is under `examples/plugins/`.
- `managed/DeadworksManaged.csproj:18` still has `<Compile Remove="plugins\**" />` even after the split — harmless since sources moved out.
- `docker/Dockerfile` managed stage needed: `COPY examples examples`, change extra-plugin merge target from `managed/plugins/` to `examples/plugins/`, and change the `find` root for plugin `.csproj` discovery to `examples/plugins`. Published artifacts still land in `/artifacts/managed/plugins`.
- Plugin `.csproj` paths work across the move because `examples/` and `managed/` sit side-by-side in the build ctx (`/build`). `examples/Directory.Build.props` is a one-line import of `..\managed\Directory.Build.props`, and plugin csprojs reference `..\..\..\managed\DeadworksManaged.Api\DeadworksManaged.Api.csproj` (e.g. `examples/plugins/ChatRelayPlugin/ChatRelayPlugin.csproj:12`).
- Plugin csprojs use `<Private>false</Private><ExcludeAssets>runtime</ExcludeAssets>` on the `DeadworksManaged.Api` ProjectReference, with a post-build `DeployToGame` target copying `{name}.dll/.pdb` into `$(DeadlockManagedDir)\plugins`.
- `examples/ExamplePlugins.slnx` lists 11 plugins incl. `ChatRelayPlugin` which is **not** present in `managed/plugins/` artifact mirror (only 10 there) — ChatRelay is examples-only.
- Entrypoint deploys managed layer via `cp -rf "${DEADWORKS_SRC}/game/bin/win64/managed/"* "${WIN64_DIR}/managed/"` after `mkdir -p "${WIN64_DIR}/managed/plugins"` (`docker/entrypoint.sh:162-163`).
- Cross-compile toolchain: Ubuntu 24.04 + LLVM 20 (`clang-20`, `lld-20`) + xwin 0.6.5 for Windows SDK/CRT + nethost from .NET Windows hosting pack. `clang-cl` and `lld-link` are shell wrappers — argv[0] driver-mode detection is unreliable in Docker so explicit `--driver-mode=cl` / `-flavor link` flags are used (`docker/Dockerfile:19-23`).
- `build-native.sh:77` note: clang-cl's `/std:c++23` doesn't set `__cplusplus` correctly and breaks MSVC STL; project uses `-Xclang -std=c++23` directly to the front-end instead.
- Repo has two remotes: `origin` = `Deadworks-net/deadworks` (read-only for contributors), `fork` = `raimannma/deadworks` (push target). Pushes to `origin` 403; must `git push --set-upstream fork <branch>`.
