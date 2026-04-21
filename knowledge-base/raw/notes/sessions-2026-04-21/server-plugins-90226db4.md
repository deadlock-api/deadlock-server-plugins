---
date: 2026-04-21
task: session extract — server-plugins 90226db4
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/90226db4-3ca8-4bb1-8169-0e72bd5ccb66.jsonl]
---

## Source 2 engine

- Wine `warn:module:load_dll` / `LdrGetProcedureAddress` lines for `dbghelp.dll`, `vfbasics.dll`, `vrfcore.dll`, `psapi.dll`, `vconcomm.dll`, `tier0.dll`, `iphlpapi.dll`, plus `fixme:ntoskrnl:kernel_object_from_handle No constructor for type "Token"` are **normal Proton-based Source 2 boot noise** — every gamemode container emits them even on successful starts. They are not a crash signal; the only meaningful line on failure is `exited with code 1 (restarting)`. Diagnose by reading the ~200 lines immediately preceding the exit.
- Each Source 2 dedicated server (`deadworks.exe`) reserves ~2 GB of virtual address space up-front at boot. Real-world observed RSS per container ~3.5 GiB. Running three gamemode containers on one host implies ~10.5 GiB working set.
- Boot-time Wine OOM signature: `0148:err:virtual:allocate_virtual_memory out of memory for allocation, base (nil) size 80000000` cascading down to `size 00100000` means Wine cannot get a large VA reservation from the kernel. Fires before the usual DLL-load warnings — when those warnings appear after the OOM lines they are a consequence, not the cause.

## Deadlock game systems

- `gamemodes.json` keys must match the on-disk plugin **folder name**, not the csproj `AssemblyName`. The CI stager (`staged-plugins/`) copies directories selected by this map into `extra-plugins=`. Renaming `DeathmatchPlugin/` → `Deathmatch/` required updating `gamemodes.json` from `"DeathmatchPlugin"` to `"Deathmatch"` or the gamemode image ships with no plugin.

## Deadworks runtime

- The Deadworks Docker build resolves the `DeadworksManaged.Api` reference through a `Directory.Build.targets` that the deadworks Dockerfile drops into `extra-plugins/`. That targets file injects `/artifacts/managed` into `$(AssemblySearchPaths)`, which lets a bare `<Reference Include="DeadworksManaged.Api" />` (**no HintPath**) resolve at compile time inside the container. Plugin csprojs must not hard-code a HintPath for this case — the bare Reference is what the container expects.
- MSBuild auto-discovers `deadworks/examples/Directory.Build.props` by walking up from `/build/examples/plugins/<P>/` during the Docker build; that file in turn imports `deadworks/managed/Directory.Build.props` and sets `$(DeadlockManagedDir)`. A `Directory.Build.props` at the **server-plugins repo root** is never on that walk — so it has zero effect on CI/Docker and only exists for local-dev auto-deploy.
- `DeployToGame` target must be guarded `Condition="'$(DeadlockManagedDir)' != ''"` — otherwise it fires in the CI container where `DEADLOCK_GAME_DIR` is unset and tries to copy into a non-existent path.
- `Google.Protobuf` must be a compile-only `PackageReference` with `ExcludeAssets=runtime`; the deadworks host provides the runtime copy. Hard-coding a HintPath to `$(DEADLOCK_GAME_DIR)\game\bin\win64\managed\Google.Protobuf.dll` silently fails in CI where the env var is unset.

## Plugin build & deployment

- Three mutually-exclusive reference-resolution modes coexist in each plugin csproj (`Deathmatch.csproj`, `LockTimer.csproj`, `StatusPoker.csproj`) and all three must be preserved:
  1. `$(DeadlockDir)` set → HintPath against a downloaded Deadlock release ZIP. Used by the standalone `build-plugins.yml` CI validation job.
  2. Sibling `../../deadworks/managed/DeadworksManaged.Api` exists → `ProjectReference`. Used by local dev with sibling deadworks checkout.
  3. Neither → bare `<Reference Include="DeadworksManaged.Api" />` fallback, resolved by deadworks' injected `Directory.Build.targets` inside the Docker build (path `../../../managed/` does **not** exist in that build context — earlier commit `da1edf3` attempted a `ProjectReference` there and it was wrong).
- Commit arc for the fix: `da1edf3` (initial Docker/local-dev split with wrong Docker path) → `59b6e96` (restored `DeadlockDir` HintPath branch for `build-plugins` CI) → `2648aa6` (added bare Reference fallback for deadworks Docker).
- Upstream nuisance: `deadworks/managed/DeadworksManaged.Api.csproj:38` produces MSB3023 ("no target specified for copy"). Not fixable from this repo.
- Host sizing: on a 7.8 GiB host with zero swap and `vm.overcommit_memory=0` (heuristic), a running Source 2 server consuming ~3.47 GiB will cause a second container's ~2 GiB VA reservation to fail at startup. Mitigations in order of preference: one gamemode per host; add swap + `vm.overcommit_memory=1` (fits 2 containers, not 3); more RAM (16 GiB fits all three).
