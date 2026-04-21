---
date: 2026-04-21
task: session extract — deadworks d48155c8
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/d48155c8-4b21-4697-ada2-e771254244f8.jsonl]
---

## Deadworks runtime

- Recent upstream commits on `Deadworks-net/deadworks` main (as of rebase) include `0635838 expose CBaseEntity.m_fFlags and add IsBot helper`, `985245f expose CCitadelGameRules.m_bServerPaused`, `18bd959 make IsValid check validity of handle and existence of entity`, and earlier `e32ee21 store handles inside of CBaseEntity instead of raw pointers` — signalling an ongoing migration from raw entity pointers to handle-backed storage in the runtime's entity wrappers.
- Launcher versioning bumped to `0.2.10` (`a71ac83`); launcher is versioned independently from the runtime plugin host.

## Plugin build & deployment

- `raimannma/deadworks` fork tracks upstream as `origin` (Deadworks-net) and the personal fork as `fork` remote; the `docker-build` working branch sits on top of upstream `main` via rebase rather than merge.
- The `docker-build` branch carries 6 custom commits layered on upstream: `c74ad02 add Dockerfile and build scripts for cross-compiling deadworks.exe`, `cd7d5ec refactor Dockerfile and build scripts for plugin management and source organization`, `bbce4cf add GitHub Actions workflow for Docker build and push`, `8d6e66d docs: update README with Docker hosting instructions`, `045471f ci: add pull request support and conditional push for Docker workflow`, `f3e2a75 add support for HLTV with configurable broadcast settings in entrypoint.sh and docker-compose`.
- `docker-compose.yaml:1-19` defines the `deadworks` service with build context at repo root, dockerfile at `docker/Dockerfile`, and a commented `additional_contexts: extra-plugins: ../my-deadworks-plugins` escape hatch for injecting out-of-tree plugin sources into the image build.
- Required container volumes: `proton:/opt/proton`, `gamedata:/home/steam/server`, `compatdata:/home/steam/.steam/steam/steamapps/compatdata`, `dotnet-cache:/opt/dotnet-cache`, plus bind-mount `/etc/machine-id:/etc/machine-id:ro` (Proton/Steam reads machine-id for its compat sandbox).
- Server port is parameterised as `${SERVER_PORT:-27015}` and exposed on both UDP and TCP; env comes from `.env` file via `env_file:` (not inline `environment:`).
- Upstream HLTV stack (now removed in this branch at `18d7c31`): `hltv-relay` from `ghcr.io/deadlock-api/hltv-relay:latest` on port 8080→3000, authenticated via `HLTV_RELAY_AUTH_KEY=${TV_BROADCAST_AUTH}` with `HLTV_RELAY_STORAGE=redis` backed by a sibling `redis:8-alpine` service on a `redis` named volume. Both services were gated behind the `tv` compose profile, so they only spun up with `--profile tv`.
