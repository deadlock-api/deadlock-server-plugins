---
date: 2026-04-22
task: Determine upper bound of citadel_trooper_squad_size
files: [TrooperInvasion/TrooperInvasion.cs]
---

`citadel_trooper_squad_size` has an engine-enforced hard cap of **8 members
per squad**. Setting it higher (we tried 75+) is *silently accepted* by the
convar itself but the spawn system then spews
`Error!! Squad trooper_squad_<lane>_<idx>_<N> is too big!!! Replacing last
member` on every pulse, and the squad still materialises with only 8 troopers.

The practical consequence for horde modes: total wave volume is
`squad_size × spawn_interval_pulses × lanes` — so to scale from small waves to
hundreds of troopers you must scale **pulse count** (either burst duration or
spawn interval), not squad size.

TrooperInvasion's tuning after discovering this:
- `MaxSquadSize = 8` (engine max, constant)
- `citadel_trooper_spawn_interval_early/late/very_late = 1s` (set in
  `OnStartupServer`) — one pulse per second per lane
- `BurstSeconds = 4s` constant (wave-1..3 ramp from 1.5s → 3.5s for onboarding)
- `WaveIntervalSeconds` scales 20s (1p) → 5s (32p) linearly
- Result: wave-4+ ≈ 8 × 4 × 4 = 128 troopers per wave, split across team-2 and
  team-3 at spawn time, with team-2 culled via `OnEntitySpawned` (see
  `2026-04-22-onentityspawned-remove-deferral.md`).

Squad size is the wrong knob for "bigger waves". Always scale via pulses.
