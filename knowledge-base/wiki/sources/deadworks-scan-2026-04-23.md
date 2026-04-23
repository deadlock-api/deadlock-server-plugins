---
title: Deadworks Scan — 2026-04-23
type: source-summary
sources:
  - raw/notes/2026-04-23-plugin-native-dll-resolution.md
  - raw/notes/2026-04-23-telemetry-env-vars.md
  - raw/notes/2026-04-23-entity-io-api.md
  - raw/notes/2026-04-23-trace-api.md
  - raw/notes/2026-04-23-soundevent-builder.md
related:
  - "[[deadworks-plugin-loader]]"
  - "[[deadworks-runtime]]"
  - "[[plugin-api-surface]]"
  - "[[entity-io]]"
  - "[[trace-api]]"
  - "[[deadworks-scan-2026-04-22]]"
created: 2026-04-23
updated: 2026-04-23
confidence: high
---

# Deadworks Scan — 2026-04-23

Second pass over `../deadworks/` after the 2026-04-22 deep scan. Follows
up on upstream changes since `2026-04-14` and picks up several
pre-existing API surfaces (`EntityIO`, `Trace`, full `SoundEvent`
builder) that the prior scan left as only partially covered.

## Commits since last scan

From `git log --since="2026-04-21"` on `main`:

| SHA | What | Relevance |
|-----|------|-----------|
| `a4fd4d6` | rename `CustomEvents` → `PluginBus` | already ingested 2026-04-23 ([[plugin-bus]]) |
| `21ed440` | add `CustomEvents` (predecessor) | superseded |
| `f9a876c` | `PluginLoadContext.LoadUnmanagedDll` override | **NEW** — this scan |
| `eaa2d8d` | docker: tail correct console.log path | docker-build already covers log streaming |
| `b886e5f` | docker: clean managed dir before deploy | docker-build already covers |
| `0bf561a` | docker: shared gamefiles volume, flock, per-instance machine-id | docker-build already covers |
| `9d5aa79` | docker: stream console.log to stdout | docker-build already covers |
| `224d660` | OpenTelemetry + `ILogger` rework | **partial** — runtime page cites wrong SHA |
| `71cd111` | docker: forward `DEADWORKS_ENV_*` to game process | runtime page already covers |
| `590ef79` | docker: HLTV broadcast settings | operations pages already cover |
| `c0f977b` | full `SoundEvent` builder API | **NEW** — this scan (plugin-api-surface undersells) |
| `b85f5ea` | expose `HeroID` on `CCitadelHeroComponent` | [[deadworks-0.4.5-release]] mentions |
| `e6f11ea` | fix `CCitadelPlayerController.PrintToConsole` | [[deadworks-0.4.5-release]] mentions |
| `31df69a` | chat reply on invalid commands, dedupe aliases | minor — in `[[command-attribute]]` |
| `548317f` | `[Command]` system / deprecate old attributes (merge) | already ingested |

## Pre-existing surfaces the prior scan missed

The 2026-04-22 scan focused on `[Command]`, `Timer`, events, schema
accessors, `NetMessages`, `PluginConfig`, and examples. Three
plugin-facing subsystems were either not documented at all or only
mentioned as filename references:

- **`EntityIO.HookOutput` / `HookInput`** — Valve entity I/O hooks. New
  entity page: [[entity-io]].
- **`Trace` static class** — VPhys2 ray and shape casting. New entity
  page: [[trace-api]].
- **`SoundEvent` builder** — beyond `Sounds.Play/PlayAt`, a full
  parameter-builder API with GUID-based lifecycle control. Updated
  [[plugin-api-surface]] with proper mention.

## Wiki corrections

1. **Commit SHAs unreachable on `main`.** `[[deadworks-runtime]]` cites
   `deb8ff2` for the telemetry rework; `[[deadworks-plugin-loader]]`
   cites `211583e` for the native-DLL fix. Both commit objects exist
   but neither is reachable from any local branch — they appear to be
   pre-rebase versions. Canonical main-branch SHAs are `224d660`
   (telemetry) and `f9a876c` (native-DLL). Original SHAs still resolve
   via `git rev-parse`, so citations are not broken, just non-canonical.
2. **`SoundEvent` plugin-api-surface entry.** Said "v0.4.5 adds
   single-player target path" — that understated the post-v0.4.5
   `SoundEvent` builder commit `c0f977b` (2026-04-22).

## Scope NOT covered in this scan

- `launcher/` — still out of scope (separate Tauri app).
- `DeadworksManaged.Tests/` — intentionally deferred.
- `docker/entrypoint.sh` deep re-read — the ops pages already cover
  what changed.
- Protobuf pipeline changes — no relevant commits since last scan.
- `examples/plugins/` — no new plugins added; existing ones already
  catalogued.
