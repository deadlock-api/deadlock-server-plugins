---
date: 2026-05-02
task: Fix TrooperInvasion wave-skip-forever caused by dead-trooper lingering in alive set
files: [TrooperInvasion/TrooperInvasion.cs]
related-commits: [c359ee5, b7ea812, c2813ae]
---

Dying enemy troopers linger as live entities for a tick or more before `OnEntityDeleted`
fires. The reconciler (which runs at the start of each `RunWave` to prevent over-spawning)
counted these dying-but-not-yet-deleted troopers as "alive", keeping `_aliveEnemyTroopers`
artificially above the cap and causing the wave to be skipped with "too many troopers alive"
— permanently, because the count never came back down below cap.

**First approach (tried and reverted):** `c2813ae` switched `_aliveEnemyTroopers` from
`HashSet<int>` (indexed by raw EntityIndex) to `HashSet<uint>` keyed by `EntityHandle`
(packed serial+index). The idea: `CBaseEntity.FromHandle` returns null on serial mismatch,
so stale handles auto-report "gone", and the reconciler also explicitly dropped `!IsAlive`
entries. This worked but was more complex and introduced its own edge cases around the
1-tick deferred-Remove paths. **Reverted** by `b7ea812`.

**Final approach (`c359ee5`):** Keep the `HashSet<int>` (EntityIndex) approach but add
a dead-check to the reconciler. When reconciling the alive set before spawn, call
`CBaseEntity.FromIndex(idx)?.IsAlive ?? false` — if false (entity deleted or dead),
remove from the set. This is simpler and catches the dying-corpse window without the
handle-tracking overhead.

Key insight: the reconciler runs once per wave trigger (not per tick), so the extra
`FromIndex` lookups are not a performance concern. The IsAlive check covers both the
"entity deleted before OnEntityDeleted fired" edge case and the "entity exists but is
in the dying animation" state.
