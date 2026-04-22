---
date: 2026-04-22
task: Diagnose native access violation during trooper spawn cascade
files: [TrooperInvasion/TrooperInvasion.cs]
---

Calling `args.Entity.Remove()` **synchronously** inside
`OnEntitySpawned(EntitySpawnedEvent args)` during a heavy spawn cascade
produced a native access violation (`deadworks_*_accessviolation.mdmp`) with
no managed stack, a few seconds after the wave started. The pattern matches
`TagPlugin`'s direct `Remove()` usage and is documented as "safe to modify"
in [[events-surface]] — so why did it crash here?

Two compounding factors:

1. `citadel_trooper_max_per_lane = 2048` (we raised it from vanilla 25 when
   chasing "hundreds of troopers" — see
   `2026-04-22-trooper-squad-size-cap.md`). That lets dozens of troopers
   spawn **in a single frame** when `spawn_interval_early = 1s` fires.
2. Our `OnEntitySpawned` culls every team-2 trooper via `Remove()`, so the
   spawn cascade and the delete cascade happen in the same frame, both
   iterating engine-internal spawn lists.

The engine's spawn iterator doesn't tolerate mid-iteration deletion at that
scale. Fix:
- Dropped `max_per_lane` back to 256 (still 10× vanilla; enough for the
  wave-4+ ≈ 128 trooper design target).
- Changed `OnEntitySpawned` to capture `EntityIndex` and defer the actual
  `Remove()` one tick via `Timer.Once(1.Ticks(), ...)`. The closure
  re-resolves the entity from the index; if it's already gone (shouldn't
  happen at one tick, but defensive) the closure no-ops.

Canonical pattern for "cull friendly NPCs at spawn":

```csharp
public override void OnEntitySpawned(EntitySpawnedEvent args) {
    var ent = args.Entity;
    if (/* filter */ false) return;
    int idx = ent.EntityIndex;
    Timer.Once(1.Ticks(), () => CBaseEntity.FromIndex(idx)?.Remove());
}
```

TagPlugin's direct-Remove pattern works for *low-volume* designer-based
filtering (a single per-map cull of ~10 boss NPCs). For *per-spawn* filters
during a horde mode, defer.
