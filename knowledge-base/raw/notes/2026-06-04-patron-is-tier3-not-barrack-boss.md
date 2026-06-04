---
date: 2026-06-04
task: Fix TrooperInvasion declaring Defeat the moment the first base boss (Watcher) dies
files:
  - TrooperInvasion/TrooperInvasion.cs
  - TrooperInvasion/TrooperInvasion.EndMode.cs
related-notes:
  - 2026-05-04-dl-midtown-no-tier1-guardians.md
  - 2026-04-28-trooper-invasion-friendly-guardian-false-defeat.md
  - 2026-04-23-gamestate-pin-required-on-patron-death.md
---

`npc_barrack_boss` is NOT the Patron. It is the **Watcher** — there are 6 per
team (the "base bosses" the user sees fall). The actual Patron is
**`npc_boss_tier3`** — 1 per team.

Authoritative source: the game files at
`/home/manuel/.local/share/Steam/steamapps/common/Deadlock` (mounted in dev via
`GAMEFILES_MOUNT`), decompiled vdata at
`deadlock-assets-api/vdata/npc_units.vdata`. The `npc_boss_tier3` block
(`_class = "npc_boss_tier3"`) has: `m_sModelName = "models/npc/patron_amber/patron_amber.vmdl"`,
`m_PatronKilledSound`, `m_PatronTransformStartSound = "Patron.Phase1.Transform.Start"`,
`m_nPhase2Health`, `m_flPostShrineTransition`, Amber/Sapphire (mother) Patron
particles, and the Shrine→Patron phase machinery. It is unambiguously the phased
Patron win-objective. The bot DPS table (`m_VSWatcher`, `m_VSWalker`, `m_VSPatron`,
`m_VSShrine`, `m_VSGuardian`) also lists Watcher and Patron as distinct objective types.

Corrected objective map for `dl_midtown` (per team):
- `npc_boss_tier2` ×3 = Walker (lane towers)
- `npc_barrack_boss` ×6 = Watcher ("base bosses")
- `npc_boss_tier3` ×1 = **Patron** (Shrine phase → Patron phase2, 12000+12000 HP)
- `destroyable_building` ×2 = t3 generators

**The bug:** `PatronDesigner = "npc_barrack_boss"`. `OnTakeDamage` intercepts the
killing blow on `PatronDesigner`, pins HP, and calls `EndMode`. So the first
Watcher (base boss) to be destroyed by enemy troopers was read as "the Patron
took a lethal blow" → `EndMode(defeat)` while the real Patron stood untouched.

**Fix:**
- `PatronDesigner = "npc_boss_tier3"`.
- `IsGuardianDesigner` no longer includes `npc_boss_tier3` (that's the Patron
  now); it is now `npc_boss_tier2 || npc_barrack_boss` — the sub-objectives
  (Walker + Watcher) whose death can fire the scripted weaken hit on the Patron.

This means **the entire 2026-04-28 "friendly-guardian false-defeat" weaken-window
analysis was built on the misidentification** (it called `npc_barrack_boss` the
Patron). The window machinery is kept defensively, retargeted at the real Patron,
but its necessity for `npc_boss_tier3` is now UNVERIFIED — needs in-game testing.

Open questions to verify live (could not test from this session):
- Does the real Patron (`npc_boss_tier3`) death also flip `m_eGameState`→PostGame
  and kick clients (the original reason for the OnTakeDamage HP-pin)? Assumed yes.
- Phase1(Shrine)→Phase2(Patron) transform: the HP-pin-to-1 on the first lethal
  blow may freeze the boss in phase1 / end the mode at phase1 depletion rather
  than at true death. Acceptable for PvE, but confirm it isn't premature.
- Whether any sub-objective death actually fires a scripted lethal hit on tier3
  (the premise of the weaken window). If not, the window can later be deleted.
