---
date: 2026-04-21
task: session extract — server-plugins 0b7a496e
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/0b7a496e-9e55-4c42-b5dc-9f65f83cf94a.jsonl]
---

## Deadlock game systems

- `gamemodes.json` at repo root maps a server profile name to the list of plugins it loads. Current content:
  `{"normal": ["StatusPoker"], "lock-timer": ["StatusPoker", "LockTimer"]}`. So `StatusPoker` is shared across both profiles; `LockTimer` is exclusive to the `lock-timer` profile; `DeathmatchPlugin` is present in the tree but not yet wired into `gamemodes.json` (as of commit `941501d`).
- Repo currently ships three plugins as sibling `.csproj` dirs at the root: `DeathmatchPlugin/`, `LockTimer/`, `StatusPoker/`. `LockTimer` is the only one with a non-trivial layout (subdirs `Hud`, `Zones`, `Records`, `Timing`, `LockTimer.Tests`, `docs`) plus a `zones.yaml` config and a `logo.png`.

## Plugin build & deployment

- `docker-compose.yml` uses a YAML anchor `x-base`/`&base` with shared volumes `proton`, `gamefiles`, `dotnet-cache` merged into each service via `<<: *base`. Per-instance state lives in dedicated named volumes `gamedata-<mode>` and `compatdata-<mode>` (Proton prefix), while `proton`/`gamefiles`/`dotnet-cache` are intentionally shared across services to avoid duplicating the Deadlock install and Proton runtime. Pattern established by commit `1915da7` ("use per-instance volumes and shared gamefiles for multi-server support").
- Each service ships on its own UDP+TCP port (27015 for `normal`, 27016 for `lock-timer`) and expects TWO env vars that must match: `SERVER_PORT` and `DEADWORKS_ENV_PORT`. Both are set to the same value in compose, suggesting the Deadworks runtime reads `DEADWORKS_ENV_PORT` independently from whatever the server binary reads from `SERVER_PORT`.
- CI publishes one image per gamemode to `ghcr.io/deadlock-api/deadlock-server-plugins/<mode>:latest`. Per commit `c77843c`, CI tags builds by dispatch/branch/PR with "readable refs".
- Project uses no top-level `.gitignore` worth noting (only 10 bytes). `.idea/` was ignored separately in commit `b45db6e`.
- Recent commit history shows repeated csproj churn around cross-environment builds: `da1edf3` split `ProjectReference` for Docker vs local-dev, `59b6e96` restored the `DeadlockDir` HintPath branch for the `build-plugins` CI path, and `2648aa6` added a bare `Reference` fallback for the `deadworks` Docker build — i.e., three different resolution paths coexist in the csproj to satisfy Docker build, the `build-plugins` CI workflow, and local dev.

## Deadworks runtime

- Environment contract surfaced in `docker-compose.yml`: Deadworks reads `DEADWORKS_ENV_PORT` (distinct from the game server's `SERVER_PORT`). Treat these as two separate knobs the operator must keep in sync when running multiple instances on one host.
