---
date: 2026-04-21
task: session extract — deathmatch 310fc296
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-deathmatch/310fc296-93e6-4e63-9e5c-3d80273f8857.jsonl]
---

## Plugin build & deployment

- `deadlock-deathmatch` repo ships its own `.github/workflows/docker.yml` that builds the game-server image by checking out `raimannma/deadworks` (with `submodules: recursive`) at path `deadworks/` plus the plugins repo at path `deathmatch/`, then feeds `deathmatch/plugins` into `docker/build-push-action` via a named build context `extra-plugins=deathmatch/plugins` — the deadworks `Dockerfile` consumes plugins from that named context rather than from the main build context (docker.yml:42-45).
- Image is published to `ghcr.io/${{ github.repository }}/game-server:latest` — i.e. under the plugins repo's namespace (`ghcr.io/raimannma/deadlock-deathmatch/game-server:latest`), not under deadworks (docker.yml:47).
- Workflow originally only triggered on `push`/`pull_request` to `main`; session added `workflow_dispatch:` and relaxed the GHCR-login + push gates from `github.event_name == 'push'` to `github.event_name != 'pull_request'` so manual dispatch also logs in and publishes (commit `0dd89cd` on `raimannma/deadlock-deathmatch`, docker.yml:32,46).
- GHA cache mode `type=gha,mode=max` is used for the buildx layer cache (docker.yml:48-49).
