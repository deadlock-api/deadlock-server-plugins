---
date: 2026-04-23
task: TrooperInvasion wave-skip "N troopers still alive" with no troopers on the map
files: [TrooperInvasion/TrooperInvasion.cs]
---

`OnEntityDeleted` does not fire for every trooper removal path the engine
takes. Symptom in TrooperInvasion: `_aliveEnemyTroopers` set keeps growing
across waves (e.g. "116 troopers still alive" while map has zero), `RunWave`
hits its cap-check at `TrooperInvasion.cs:489` and prints
`"Wave skipped — N troopers still alive (cap M)"` indefinitely.

Suspected leak sources (unverified individually, only the symptom is
confirmed): super-trooper promotion (`citadel_super_trooper_gold_mult`
upgrades `npc_trooper` → `npc_trooper_boss` — entity may swap rather than
delete-then-spawn), engine end-of-lane despawn after reaching Patron,
map/round transition cleanup. Whatever the path, our hook misses it.

Fix landed: a `ReconcileAliveTroopers()` sweep that calls
`_aliveEnemyTroopers.RemoveWhere(idx => CBaseEntity.FromIndex(idx) is null
|| !IsTrooperDesigner(ent.DesignerName) || ent.TeamNum != EnemyTeam)`,
invoked in `RunWave` right before the cap check. Self-heals every wave —
prevents starvation regardless of which removal path the engine takes.

Pattern generalises: any plugin that maintains a parallel `HashSet<int>`
of tracked entities should reconcile against `CBaseEntity.FromIndex` at
decision points, not trust `OnEntityDeleted` exclusively.
