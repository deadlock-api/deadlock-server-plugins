---
date: 2026-04-22
task: Fix TrooperInvasion 4-player wedge; correct lane-bitmask assumptions
files: [TrooperInvasion/TrooperInvasion.cs, Deathmatch/Deathmatch.cs]
---

**Deadlock currently has only 3 lanes, not 4.** The valid `citadel_active_lane`
lane IDs are `{1=Yellow, 4=Blue, 6=Purple}`. Green (3) is no longer a lane.

This reconciles several confusions in the existing wiki:

- `Deathmatch.cs:51` has `_laneCycle = { 1, 3, 6 }` with the comment "Yellow,
  Green, Purple — skip Blue (4)". That's stale — `3` is no longer valid, and
  the comment's lane naming is outdated.
- `knowledge-base/raw/notes/2026-04-22-citadel-active-lane-bitmask.md` asserts
  `citadel_active_lane` is a bitmask with bits 0–3 = 4 lanes, and that
  `(1 << N) - 1` is a valid "N lanes" mask. Both claims are wrong on current
  Deadlock.

### Why `(1 << N) - 1` wedged at 4 players

TrooperInvasion used `(1 << activeLanes) - 1` to compute the lane mask,
recomputed every `RunWave` from `ComputeActiveLanes(humans) = Clamp(humans/2, 1, 4)`.

| Humans | activeLanes | `(1<<N)-1` |
|---|---|---|
| 1–3 | 1 | **1** (Yellow — valid) |
| 4–5 | 2 | **3** (Green — no longer a lane) |
| 6–7 | 3 | 7 (Yellow+Blue+Purple — probably accepted as bitmask) |
| 8+ | 4 | 15 |

At 4 players the mask became `3`, which references a lane that no longer
exists in the engine. The spawn pipeline silently no-opped — scheduler
kept running, `Chat.PrintToChatAll` still printed "next in 19s", but zero
troopers emerged. The symptom only appeared at exactly 4 players because
that was the first transition away from the value `1` the mask had been
pinned at during 1–3-player play.

### Fix

Replace bitmask math with explicit OR of valid lane markers `{1, 4, 6}`:

```csharp
private static readonly int[] _laneMarkers = { 1, 4, 6 };
private static int LaneBitmask(int activeLanes) { … OR first `activeLanes` markers … }
```

And clamp `ComputeActiveLanes` to `[1, 3]` (only 3 lanes exist).

Progression:

| Humans | activeLanes | Mask | Lanes enabled |
|---|---|---|---|
| 1–3 | 1 | `1` | Yellow |
| 4–5 | 2 | `1\|4 = 5` | Yellow + Blue |
| 6+ | 3 | `1\|4\|6 = 7` | Yellow + Blue + Purple |

### Open questions / unverified

- Whether `5` and `7` are accepted as combined-bitmask values or whether the
  engine requires individual lane IDs. TagPlugin uses `255` (all-on) which
  is treated as bitmask, so combined values probably work. Needs live test.
- Whether `(1 << N) - 1 = 15` would wedge at 8 players in the previous code
  for the same "references nonexistent lane" reason. Moot now.
- Whether the `_laneCycle = { 1, 3, 6 }` in `Deathmatch.cs:51` actually
  spawns anyone into the value-3 lane or if Deathmatch has been silently
  broken on 1/3 rotations since Deadlock dropped to 3 lanes. Separate
  investigation.

Fix committed as `5382526`.
