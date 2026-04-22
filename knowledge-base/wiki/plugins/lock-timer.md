---
title: LockTimer plugin
type: plugin
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-0b7a496e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-65d13a2e.md
  - ../LockTimer/LockTimerPlugin.cs
  - ../LockTimer/zones.yaml
related:
  - "[[deadworks-runtime]]"
  - "[[deadlock-game]]"
  - "[[plugin-build-pipeline]]"
  - "[[command-attribute]]"
  - "[[timer-api]]"
  - "[[plugin-config]]"
  - "[[examples-index]]"
created: 2026-04-21
updated: 2026-04-21
confidence: medium
---

# LockTimer plugin

A zone-based lock-timer gamemode plugin. Exclusive to the `lock-timer`
gamemode profile per `gamemodes.json` (server-plugins-0b7a496e).

## Layout

LockTimer is the most structurally-involved plugin in this repo
(server-plugins-0b7a496e):

- `LockTimer/LockTimerPlugin.cs` — main plugin entry.
- `LockTimer/Hud/` — HUD helpers.
- `LockTimer/Zones/` — zone definitions and spatial logic.
- `LockTimer/Records/` — best-time / record tracking.
- `LockTimer/Timing/` — timing primitives.
- `LockTimer/LockTimer.Tests/` — unit tests.
- `LockTimer/docs/plan.md` — design document.
- `LockTimer/zones.yaml` — per-zone configuration (YAML).
- `LockTimer/logo.png`.

## Chat commands (registered)

From `LockTimer/LockTimerPlugin.cs:216-252` (server-plugins-65d13a2e):

- `[ChatCommand("zones")]`
- `[ChatCommand("reset")]`
- `[ChatCommand("pos")]`
- `[ChatCommand("speed")]`

**Bug flag** (server-plugins-65d13a2e): LockTimer registers these as
**bare names** (without the `!` prefix), which diverges from Deadworks
convention (`[ChatCommand]` strings should include `!`). The plugin's
own design doc at `LockTimer/docs/plan.md:1616` uses the `!`-prefixed
form, so bare-name registration is likely **broken / latent**. Not
reconciled in the session that surfaced this.

## Metrics API

The plugin is described as having a "metrics API"
(`knowledge-base/wiki/overview.md`), but detail on the metrics surface
did NOT appear in the extracted sessions. Likely ties into the
host-level OpenTelemetry infrastructure from deadworks upstream
`deb8ff2` — see [[deadworks-runtime]].

> More sources needed to detail the metrics API and zone-logic
> internals.

## Gamemode wiring

`gamemodes.json`:

```json
{
  "normal": ["StatusPoker"],
  "lock-timer": ["StatusPoker", "LockTimer"]
}
```

Image tag: `ghcr.io/deadlock-api/deadlock-server-plugins/lock-timer:latest`.
Ships on UDP+TCP port `27016` (per compose).

## Build

Uses the same triple-mode csproj pattern as the other plugins in this
repo (`DeadlockDir` HintPath / sibling `ProjectReference` / bare
`<Reference>` for Docker). See [[plugin-build-pipeline]] for the full
pattern.

## Open questions

- What does `zones.yaml` configure? (Likely zone geometries + names,
  but no session extracted the schema.)
- How does the Records subsystem persist? File? In-memory?
- Metrics endpoint / exposition format?
