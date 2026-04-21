---
date: 2026-04-21
task: session extract — deathmatch 73f32122
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-deathmatch/73f32122-83a2-44f0-bf03-91f269c8a13b.jsonl]
---

## Source 2 engine

- `CBaseEntity` exposes HP via two schema fields: `m_iHealth` and `m_iMaxHealth` (CBaseEntity.cs:312-316). `CCitadelPlayerPawn` additionally wires `m_iHealth` and `m_iHealthMax` as read-only accessors in `PlayerEntities.cs:499-533`, meaning the pawn's "max health" is read-only from the schema; the effective cap is computed via a native call, not a settable schema field.
- Native callback table in `NativeInterop.cs:98-99` exposes `GetMaxHealth(void*) -> int` and `Heal(void*, float) -> int`; these are the authoritative source for hero-scaled max HP, not the raw schema int.

## Deadlock game systems

- On `player_respawned` and `player_hero_changed`, the engine does NOT immediately populate the hero-scaled max HP: `GetMaxHealth()` can return 0 for several ticks after the event. Writing `pawn.Health = ...` at event-fire time leaves the player at 0 HP. Fix was to poll `GetMaxHealth()` up to 20 ticks before writing Health (DeathmatchPlugin.cs:277-294 after commit `6e02eb2`).
- `PlayerDataGlobal` (`PlayerEntities.cs:484-`) wraps the networked `PlayerDataGlobal_t` struct on the player controller — read-only access to kills/gold/level/damage. Not the place to set HP.

## Deadworks runtime

- Timer scheduling uses extension `1.Ticks()` to produce per-tick delays; `Timer.Once(delay, Action)` re-entrantly schedules the next attempt — used to build a tick-based retry loop without coroutines.
- Entity handles become stale across ticks: the retry helper captures `pawn.EntityIndex` (int) and re-resolves via `CBaseEntity.FromIndex<CCitadelPlayerPawn>(idx)` inside each tick, rather than capturing the managed pawn reference (DeathmatchPlugin.cs `TryHeal`). Pattern worth reusing for any deferred pawn operation.
- `CCitadelPlayerPawn.Health` (inherited from `CBaseEntity`) has a setter that writes the schema field directly; no `SetHealth` native call is needed for the write side — only the read of max is gated behind `GetMaxHealth()`.

## Plugin build & deployment

- The deathmatch repo (`/home/manuel/deadlock/deadlock-deathmatch`) is a separate git remote (`github.com:raimannma/deadlock-deathmatch.git`) from deadlock-server-plugins; it depends on the sibling `deadworks/managed/` checkout for the API surface (`DeadworksManaged.Api`).
- `dotnet` CLI is not available in the sandbox — build verification had to be skipped; changes were pushed without local compilation (session entry 43-45). Reliance on the API being "proven in the file" is the fallback.
