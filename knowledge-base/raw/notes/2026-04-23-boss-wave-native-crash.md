---
date: 2026-04-23
task: investigate + remove boss waves after server crashed on first boss spawn
files:
  - TrooperInvasion/TrooperInvasion.cs
---

## Boss-wave native crash — `CreateByDesignerName("npc_trooper_boss") + Spawn()` is not safe

Symptom: server hard-crashed (native) the first time `SpawnBossTroopers`
fired — wave 10, first round-end boss wave. Reproducible.

### Root cause (high confidence)

The managed spawn path in `SpawnBossTroopers` was:

```csharp
var boss = CBaseEntity.CreateByDesignerName("npc_trooper_boss");
boss.TeamNum = EnemyTeam;
boss.Teleport(pos);
boss.Spawn();                // null CEntityKeyValues
```

`npc_trooper_boss` is a **lane-AI NPC**. The engine's normal spawn path
(`CEntitySpawner<CNPC_TrooperBoss>::Spawn`) feeds it `m_iLane`, squad
registration, and a navmesh region via a fully-populated CEntityKeyValues —
driven by the `info_trooper_spawn` / `info_super_trooper_spawn` map
entities and the `CCitadelTrooperSpawnGameSystem`. With null KV and only
`TeamNum` set via schema, the post-`Spawn` AI init dereferences a
null/uninitialized lane or squad pointer. C# `try/catch` can't see it.

Confirmed by scanning `server.dll`: `CNPC_TrooperBoss` has networked field
`m_iLane` declared on the class; the spawn game system is
`CCitadelTrooperSpawnGameSystem` — not a generic entity spawner.

### Why other managed spawns don't crash

`CPointWorldText.Create` and `ParticleSystem.Spawn` both use
`Spawn(ekv)` with a built-up CEntityKeyValues. They're point entities, not
AI-bound — no lane/squad dependency. There is no managed API wrapper for
trooper classes, and no published schema for CEntityKeyValues keys on
`CNPC_TrooperBoss` that would let us feed it lane/squad safely.

### Alternative that would likely work (not taken — user wanted boss waves
gone, not reworked)

The engine exposes a native cheat concommand
`citadel_spawn_trooper %f,%f,%f %s` with valid types
`default / boss / melee / medic / flying` — goes through the full engine
trooper spawn game system (same path as the regular wave burst). Usage
would mirror `FlexSlotUnlock.cs:29-31`:

```csharp
Server.ExecuteCommand("sv_cheats 1");
Server.ExecuteCommand($"citadel_spawn_trooper {x},{y},{z} boss");
Server.ExecuteCommand("sv_cheats 0");
```

Bracket with `sv_cheats 1/0` because the command is FCVAR_CHEAT. Use
InvariantCulture formatting for the coord list so a Wine locale doesn't
turn `123.45` into `123,45` and corrupt the comma-separated args.

I wrote + built this variant briefly before the user decided to scrap boss
waves entirely. If the user ever re-opens the feature, reach for
`citadel_spawn_trooper boss` instead of `CreateByDesignerName + Spawn()`.

### Action taken

Removed all boss-wave code from TrooperInvasion.cs:

- `SpawnBossTroopers`, `TriggerBossWave`, `IsBossWave`, `BossBonusGoldAt`
- `_pendingBossSpawn` timer handle + all Cancel/clear sites
- Boss constants: `BossWaveEveryN`, `BossesPerLane`,
  `BossBonusGoldBase`, `BossBonusGoldPerWave`, `BossSpawnDelaySeconds`
- Boss-kill bonus branch in `OnEntityKilled`
- `[BOSS WAVE]` suffix in `!wave` output
- Help line advertising boss waves
- `ti_boss_wave_started` + `ti_boss_killed` PostHog events

`npc_trooper_boss` remains in `_trooperDesigners` and in
`OnEntitySpawned` / `OnEntityKilled` filters — the engine still emits
boss troopers naturally via the "super trooper" promotion (see
`citadel_super_trooper_gold_mult`), so our tracking + HP-scaling + kill
attribution still needs to recognise them. Only our manual spawning is
gone.

### Gotcha for future reference

Raw note `2026-04-23-trooper-invasion-boss-waves.md` was written
**before** production test and optimistically described
`CreateByDesignerName + Spawn()` as a working pattern. That note is now
misleading on that specific point — the pattern crashes for lane-AI NPCs.
For point entities (particles, worldtext) the managed-spawn pattern is
still fine because those use `Spawn(ekv)` with explicit KV.
