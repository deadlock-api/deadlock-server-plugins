---
date: 2026-04-28
task: TrooperInvasion wave-skip false positive ("X troopers still alive" with no troopers visible) and the resulting stuck-round "hang"
files: [TrooperInvasion/TrooperInvasion.cs]
---

`TrooperInvasion._aliveEnemyTroopers` was originally a `HashSet<int>`
keyed by raw entity index. Two leak paths kept the set growing across
waves until it permanently exceeded `ComputeTrooperCap(humans)`, after
which `RunWave` printed `[TI] Wave skipped — N troopers still alive
(cap M)` every interval and never spawned anything again — which
players experienced as the gamemode being frozen.

1. **Dying-but-not-yet-deleted entities pass the reconciler.**
   `OnEntityDeleted` fires after a tick or more of `LifeState.Dying`/`Dead`.
   During that window `IsTrooperDesigner(ent.DesignerName)` and
   `ent.TeamNum == EnemyTeam` are still true on the corpse, so
   `ReconcileAliveTroopers` keeps the entry. A dozen of these per wave
   compound until the cap is permanently saturated.

2. **Index reuse hides identity.** `CBaseEntity.FromIndex(idx)` returns
   whatever entity currently occupies slot `idx`, regardless of serial.
   If the original trooper was destroyed and the slot was recycled
   (even by an unrelated entity), the reconciler can't distinguish
   "still our trooper" from "different entity at the same slot."

Fix in `TrooperInvasion.cs` — track `uint EntityHandle` (packed
serial+index) instead of raw `int` index, and add `!ent.IsAlive` to
the reconciler's skip predicate:

```csharp
private readonly HashSet<uint> _aliveEnemyTroopers = new();

// OnEntitySpawned: _aliveEnemyTroopers.Add(ent.EntityHandle);
// OnEntityDeleted: _aliveEnemyTroopers.Remove(args.Entity.EntityHandle);

private void ReconcileAliveTroopers() {
    _aliveEnemyTroopers.RemoveWhere(handle => {
        var ent = CBaseEntity.FromHandle(handle);
        return ent == null
            || !IsTrooperDesigner(ent.DesignerName)
            || ent.TeamNum != EnemyTeam
            || !ent.IsAlive;
    });
}
```

Why `FromHandle` is correct: per
`deadworks/managed/DeadworksManaged.Api/Entities/CBaseEntity.cs:94-98`
it returns `null` when the serial baked into the handle no longer
matches the engine's current entity at that slot. Stale handles
report "gone" automatically — no reuse-aliasing.

`CullAllTroopers` and the deferred-`Remove` path in `OnEntitySpawned`
were also switched to handles for the same reason: a 1-tick deferred
`CBaseEntity.FromIndex(idx)?.Remove()` could otherwise hit a recycled
slot.

**Generalisation:** any plugin tracking enemy/trooper/NPC liveness
should key on `EntityHandle` and gate on `LifeState`. The pattern
`HashSet<int>` of raw indices is a footgun under horde-mode churn.
