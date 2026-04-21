---
date: 2026-04-21
task: session extract — deadworks 543d455d
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/543d455d-4616-4b46-8bd0-74747ae8a04f.jsonl]
---

## Plugin build & deployment

- Deadworks repo has 4 GitHub Actions workflows: `.github/workflows/update-protos.yml`, `update-game-exported.yml`, `build.yml`, `docker.yml` (session line 13).
- `docker.yml` publishes the deadworks runtime image to `ghcr.io/${{ github.repository }}` using `docker/build-push-action@v7` against `docker/Dockerfile` as context `.` (originally lines 41-50 of docker.yml).
- Image tagging via `docker/metadata-action@v6` rules:
  - `type=raw,value=latest,enable={{is_default_branch}}` — `latest` only on default branch.
  - `type=semver,pattern={{version}}` and `{{major}}.{{minor}}` — applied on `v*` tag pushes.
  - `type=sha` — always adds `sha-<shortsha>`.
  - Per session line 23: push-to-`main` produces only `latest` + `sha-<shortsha>` (semver skipped without tag ref); tag `v1.2.3` produces `1.2.3`, `1.2`, `sha-...`, and also `latest` if the tagged commit is on the default branch (metadata-action's `is_default_branch` evaluates true for tags cut from main).
- Change made this session (docker.yml): added `pull_request: branches: [main]` trigger, gated GHCR login with `if: github.event_name == 'push'`, and made `push:` field conditional (`${{ github.event_name == 'push' }}`) so PRs build-only and main/tag pushes deploy. Pattern worth copying for the plugins repo if PR-level Docker smoke tests are wanted.
- Checkout uses `actions/checkout@v6` with `submodules: recursive` — Deadworks Docker build requires submodules (likely protos/game-exported) at build time.
- Build uses `docker/setup-qemu-action@v4` + `docker/setup-buildx-action@v4` with GHA cache (`cache-from: type=gha`, `cache-to: type=gha,mode=max`) — suggests multi-arch capable build setup.
- Git remote for deadworks: `https://github.com/Deadworks-net/deadworks` (session line 7 fetch output).
