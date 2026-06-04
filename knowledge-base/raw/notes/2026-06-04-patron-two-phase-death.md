---
date: 2026-06-04
task: Make TrooperInvasion end only on the Patron's SECOND (final) death, not the first
files:
  - TrooperInvasion/TrooperInvasion.cs
  - TrooperInvasion/TrooperInvasion.EndMode.cs
related-notes:
  - 2026-06-04-patron-is-tier3-not-barrack-boss.md
---

The Patron (`npc_boss_tier3`) is a two-phase boss. First real death drops the
Shrine form; the engine makes it invulnerable through a ~15-20s dying/transform
sequence (`m_DyingModifier` carries INVULNERABLE/UNKILLABLE/UNTARGETABLE) and
revives it as the mobile Patron (phase 2). Only the second death is the true end.
vdata evidence in `npc_units.vdata` `npc_boss_tier3`: `m_nMaxHealth=12000` AND
`m_nPhase2Health=12000`, plus `m_flPhase1DyingBegin/Drop/Wait/TransformUp`,
`m_flPostShrineTransition`, and a `dying` modifier subclass.

Previous code pinned HP to 1 and called EndMode on the FIRST lethal blow, so the
mode ended at phase-1 depletion. Fix: in `OnTakeDamage`, count real-kill lethal
blows per team (`_humanPatronDowns`/`_enemyPatronDowns`):
- Non-real (scripted/world) lethal hit → pin to 1, swallow (unchanged guard).
- 1st real death (`downs < PatronPhases=2`) → let the lethal damage THROUGH so the
  engine kills the Shrine and runs its transform. No pin, no EndMode.
- 2nd real death → swallow + pin + EndMode (prevents the engine PostGame kick).
- Debounce `PatronTransformDebounceSeconds=5.0`: a single death lands several lethal
  damage events in one tick; 5s safely coalesces them without risking phase 2, which
  is unreachable that fast (boss invulnerable the whole transform).

Counters reset in `OnStartupServer` (post-changelevel) and the last-player-disconnect
path in `TrooperInvasion.Players.cs`.

UNVERIFIED — needs in-game testing:
- Assumes letting phase-1 reach 0 HP does NOT trigger the engine match-end kick (the
  transform handles it). The user's report that the boss "comes back after ~20s"
  supports this, but the pass-through path is new and untested.
- The exact transform duration vs the 5s debounce — if a team somehow bursts phase 2
  within 5s of phase 1 death, the final death would be debounced and missed. Believed
  impossible due to transform invulnerability, but worth confirming under heavy DPS.
- Whether the same entity index persists across the transform (we key by TEAM, so it
  doesn't matter, but noted for anyone who later tries entity-keyed tracking).
