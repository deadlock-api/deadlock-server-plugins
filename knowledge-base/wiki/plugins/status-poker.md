---
title: StatusPoker plugin
type: plugin
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-0b7a496e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-5233473a.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-6d3a9327.md
  - ../StatusPoker/StatusPoker.cs
related:
  - "[[deadworks-runtime]]"
  - "[[plugin-build-pipeline]]"
  - "[[timer-api]]"
  - "[[plugin-config]]"
  - "[[examples-index]]"
created: 2026-04-21
updated: 2026-04-21
confidence: low
---

# StatusPoker plugin

A periodic status-poker / keepalive plugin. It ships in BOTH gamemode
profiles (`normal` and `lock-timer`) per `gamemodes.json`
(server-plugins-0b7a496e) — so it effectively runs on every server
instance, polling some external endpoint at a regular cadence.

## What little the sessions cover

- Plugin lives at `StatusPoker/StatusPoker.cs` (110 lines,
  deathmatch-5233473a).
- Derives from `DeadworksPluginBase` with `Name` override; references
  `DeadworksManaged.Api` as a standalone plugin (deathmatch-6d3a9327).
- Uses `System.Threading.Timer` + `CancellationTokenSource` for polling
  (NOT the host's `Timer.Every`). Reason documented
  (deathmatch-5233473a):
  - `Timer.Every` is **sync-only** — cannot be used for async HTTP
    polling.
- Uses its own `static readonly HttpClient` — there is no centralized
  HTTP facility in the Deadworks API surface.
- Rolls its own env-var reads (`DEADWORKS_ENV_*` prefix) — no env-var
  config helper in host API despite `ConfigManager` / `[PluginConfig]`
  existing (deathmatch-5233473a).

## Why StatusPoker specifically

The [[plugin-build-pipeline]] wiring uses `DEADWORKS_ENV_PORT` per
gamemode instance; StatusPoker is one consumer of that pattern. The
plugin was originally copied from `deadlock-api/deadlock-server-plugins/StatusPoker`
into the sibling `deadlock-deathmatch` repo and has parallel copies
across repos (deathmatch-6d3a9327).

## Build

Uses the same triple-mode csproj pattern as the other plugins in this
repo. `StatusPoker.csproj` conditionally picks `ProjectReference` when
sibling `../../../deadworks/managed/DeadworksManaged.Api/...` exists,
vs bare `<Reference Include="DeadworksManaged.Api">` fallback for
Docker builds (deathmatch-3636296d).

## Open questions (need more sources)

- What endpoint does it poke?
- What payload does it send / what format does it expect back?
- What does it do with the response?
- Is "StatusPoker" a deliberate pun, or is it genuinely about poker?

> This page is thin because the sessions only reference StatusPoker
> incidentally as a counter-example for the host's `Timer.Every` sync
> limitation. Targeted ingest of `StatusPoker/StatusPoker.cs` source
> code would flesh this out.
