---
date: 2026-04-28
task: Fix TrooperInvasion declaring Defeat when friendly Base Guardian falls (Patron still alive)
files:
  - TrooperInvasion/TrooperInvasion.cs
related-notes:
  - 2026-04-23-trooper-invasion-false-victory-on-base-guardian.md
---

The 2026-04-23 false-victory fix (`OnTakeDamage` `realKill` gate keyed on
`Attacker.As<CCitadelPlayerPawn>() ?? IsTrooperDesigner(attacker.DesignerName)`)
patched the player-killed-enemy-Base-Guardian case but left the symmetric
defeat path broken. That note's closing claim — "trooper killing friendly
Patron keeps working because troopers pass `IsTrooperDesigner`" — is exactly
the bug: when a trooper kills the friendly **Base Guardian** (`npc_boss_tier3`,
team 2), the engine fires the same scripted "weaken Patron" damage event on
the friendly **Patron** (`npc_barrack_boss`, team 2) but propagates the
trooper as the `Attacker`. `IsTrooperDesigner(attacker)` → true → `realKill`
→ `EndMode(victory: 2 != 2 = false)` → DEFEAT, with the Patron still standing.

Attacker-identity alone cannot distinguish the scripted weaken event from a
real lethal hit. The only reliable signal is the **time-window from the
guardian's `entity_killed`** — when a `npc_boss_tier3` or `npc_boss_tier2`
on team T dies, the engine fires the scripted weaken damage on team-T's
Patron within a single tick. Track `_humanPatronWeakenAt` /
`_enemyPatronWeakenAt` (set in `OnEntityKilled` via `IsGuardianDesigner`),
gate to a ~2s window in `OnTakeDamage`, and additionally require
`Damage > MaxHealth * 0.5f` so legitimate small chip damage during the
window still passes through normally.

When the absorb fires, restore `Health = MaxHealth` (rather than pinning to
1 like the lethal-absorb branch does) so the Patron isn't left with a 1-HP
pin that makes every subsequent damage event lethal-branch-trigger-able the
moment the window expires — which would re-introduce the false defeat with
~2s delay.

Non-obvious bits:
- Order of events: `entity_killed` for the guardian fires **before** the
  scripted `OnTakeDamage` on the Patron, so the timestamp is reliably set
  when the OnTakeDamage gate runs. (Confirmed by the gate working — would
  have been a 0% hit rate otherwise.)
- The 2s window is generous; the engine fires the scripted hit
  effectively the same tick. Keeping it at 2s leaves headroom for any
  Walker → Base-Guardian chained scripts.
- `MaxHealth * 0.5f` magnitude gate matters: without it, ALL damage during
  the window would be absorbed-and-restored, freezing the Patron at full
  HP for 2s after every tier2/tier3 death and breaking real player kill
  attempts that happen to coincide.
- The original false-victory path (player kills enemy guardian → scripted
  event on enemy Patron with null/world Attacker) is now caught by the
  new gate **as well as** the old `realKill` check; both layers stay,
  defense in depth.
