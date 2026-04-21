---
date: 2026-04-21
task: session extract — custom-server 9cc2f451
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-custom-server/9cc2f451-fc44-448b-94d4-4ce23ca5f7d7.jsonl]
---

Session is sibling repo `deadlock-custom-server` (Rust LD_PRELOAD injection stack), not the C# deadworks plugins in this repo. Captured for cross-ecosystem context — this is an *alternative* server-side manipulation path separate from deadworks.

## Deadworks runtime

- Custom-server approach rejects RCON-only manipulation: "the injected library handles the game-level manipulation plugins need" while RCON stays for admin tasks (status/kick/map change). Implies deadworks-in-process hooking is the only path for deep manipulation (pause, hero swap, teleport), RCON alone is insufficient.
- IPC design choice: Unix domain socket + `bincode` over length-prefixed frames between in-process injected `.so` and external Rust server-manager. Shared `deadlock-protocol` crate (`serde` enums `Request`/`Response`/`Event`) gives compile-time type safety across the process boundary. Relevant whenever a C#/deadworks plugin needs to talk to an external controller.
- Function hooking library chosen for the Rust cdylib: `retour` (Rust equivalent of MinHook/Detours). Useful to know if deadworks ever exposes a similar detour facility.

## Plugin build & deployment

- LD_PRELOAD plugin loader pattern: container entrypoint reads a `PLUGINS` comma-separated env var, resolves each name against `.so` files in `/opt/plugins/`, and builds `LD_PRELOAD` before the game launch. Each plugin is a separate `.so` (cdylib), toggled by env var — contrasts with deadworks' single-DLL-per-plugin model loaded by the server itself.
- Multi-stage Dockerfile: stage 1 `rust:1-bookworm` compiles entire workspace `--release`, copies `target/release/deadlock-server-manager` and `librandom_pause.so` to `/build/out/`; stage 2 runtime image does `COPY --from=builder` into `/opt/plugins/` + `/usr/local/bin/`. Build context had to move from `./docker` to `.` so Cargo workspace files were reachable.
- `.dockerignore` gotcha (deadlock-custom-server/.dockerignore originally excluded `Cargo.toml`, `Cargo.lock`, `src/`, `target/`). When the Dockerfile gained a Rust build stage those exclusions broke the build — had to be removed. Parallel risk exists in this repo if `.dockerignore` ever excludes `*.csproj` or build inputs.
- LD_PRELOAD is evaluated inside Proton/Wine launch path — Linux `.so` is the primary target, Windows DLL fallback only. Deadworks itself ships a Windows DLL model; the two approaches are not interchangeable.

## Source 2 engine

- None substantive (session treats engine as opaque, hooks via native function detours only; no struct reversing or VMT detail surfaced in transcript).

## Deadlock game systems

- None substantive (first plugin `random-pause` is just a pause trigger; hero-swap plugin idea mentioned as "will teleport players to random positions" but no game-system internals captured).
