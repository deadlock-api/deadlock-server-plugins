---
title: Project Overview
type: overview
sources:
  - ../docker-compose.yml
  - ../gamemodes.json
related: []
created: 2026-04-21
updated: 2026-04-21
confidence: medium
---

# deadlock-server-plugins — Overview

A monorepo of C# plugins for Valve's Deadlock dedicated-server runtime,
packaged as Docker images and deployed as multiple game-server profiles.

## What's here

Three plugins live at the repo root, each a standalone `.csproj`:

- **DeathmatchPlugin** — deathmatch gamemode logic.
- **LockTimer** — zone-based lock-timer gamemode, with YAML-configured zones
  and a metrics API.
- **StatusPoker** — periodic status poker/keepalive plugin.

Server variants are composed via `gamemodes.json`, which maps a server profile
(e.g. `normal`, `lock-timer`) to the set of plugins it loads. `docker-compose.yml`
runs those profiles side-by-side with per-instance gamedata/compatdata volumes
and shared Proton/gamefiles volumes across instances.

## Build and deploy

CI builds a Docker image per gamemode and tags by branch/PR/dispatch (see recent
commit `c77843c`). Images are published to `ghcr.io/deadlock-api/deadlock-server-plugins/<mode>`.

## Where to go next

- [[glossary]] — project terminology
- Plugin pages under `wiki/plugins/` (populate via ingest)
- Operations pages under `wiki/operations/` (CI, deploy, multi-instance volumes)

> _This overview is intentionally thin. It will fill in as sources are ingested._
