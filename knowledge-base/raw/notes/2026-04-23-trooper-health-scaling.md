---
date: 2026-04-23
task: meaningful per-round HP scaling for TrooperInvasion enemies
files: [TrooperInvasion/TrooperInvasion.cs]
---

Added `ScaleTrooperHealth(CBaseEntity)` called from `OnEntitySpawned` on the
enemy-team branch, after `_aliveEnemyTroopers.Add(idx)`. Formula:
`scale = min(MaxHealthScale, 1 + (round-1) * 0.5 + wave * 0.03)`, capped at
6×. Scale is read from `_roundNum` / `_waveNum` at spawn time — the engine
fires `OnEntitySpawned` during the burst window *after* `RunWave` increments
`_waveNum`, so the read is consistent within a wave.

Non-obvious bits:

1. `CBaseEntity.MaxHealth` (`deadworks/.../CBaseEntity.cs:316`) only writes
   the `m_iMaxHealth` schema field — `m_iHealth` does NOT auto-clamp up to
   the new max. Writing only `MaxHealth` leaves the unit at its vdata
   baseline HP; you have to set `Health = scaled` explicitly.
2. The vdata baseline (`npc_trooper` = 300, `npc_trooper_boss` higher —
   `deadlock-assets-api/scripts/npc_units.vdata:219`) is already applied by
   the time `OnEntitySpawned` fires, so reading `ent.MaxHealth` and
   multiplying preserves the boss-vs-regular differential automatically.
3. Unlike player-pawn post-spawn writes (which need the 1-tick defer in
   `DeferredSpawnRitual` because hero assets aren't settled), NPC health
   writes from `OnEntitySpawned` are safe without deferral — confirmed
   with a build-clean run. No AV reports.
4. Surface for outgoing trooper *damage* is NOT on the trooper entity; it's
   `m_flBulletDamage` on the nested weapon vdata block
   (`npc_units.vdata:213`). Reaching it would require resolving the
   trooper's weapon child handle post-spawn. Not done here — HP alone is a
   meaningful durability lever. The trooper entity only exposes
   receive-side resist fields (`m_flPlayerDamageResistPct` etc.) which
   invert the axis and were considered but rejected for "meaningful" since
   they don't affect what players feel on the receiving end.

Also surfaced the computed `healthScale` in the per-wave console log and the
`ti_wave_started` PostHog event (`health_scale` property, rounded to 2dp)
so telemetry can correlate deaths/gold with the difficulty curve.
