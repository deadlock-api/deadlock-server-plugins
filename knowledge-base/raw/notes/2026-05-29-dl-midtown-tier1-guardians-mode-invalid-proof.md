---
date: 2026-05-29
task: Diagnose (again) why "guardians" don't spawn in TrooperInvasion; runtime diag added
files:
  - TrooperInvasion/TrooperInvasion.Diag.cs
  - TrooperInvasion/TrooperInvasion.cs
  - knowledge-base/raw/notes/2026-05-04-dl-midtown-no-tier1-guardians.md
---

Added a one-shot startup diagnostic (`TrooperInvasion.Diag.cs`, sampled at
+5/15/30/60/120s) that logs `GameRules` state + a boss/objective entity census +
the engine's own `citadel_bot_list_objectives_ent`. Live run on `dl_midtown`
(game files mounted from the user's local Steam install, single human, no GC)
gives a **definitive** answer and corrects the 2026-05-04 note's reasoning.

## What the engine's own objective list reports (24 objectives, 0 tier1)

`citadel_bot_list_objectives_ent` output:
- **Team 2 (Amber):** 3× `npc_boss_tier2` (rebels_t2_boss purple/yellow/blue),
  6× `npc_barrack_boss` (rebels_watcher blue/purple/yellow ×2), 1× `npc_boss_tier3`,
  2× `destroyable_building` (amber_t3_generator purple/yellow)
- **Team 3 (Sapphire):** 3× `npc_boss_tier2` (combine_t2_boss yellow/purple/blue),
  6× `npc_barrack_boss` (combine_watcher), 1× `npc_boss_tier3`, 2× t3 generators
- **Zero `npc_boss_tier1`.** Entity census agrees: tier2=6, tier3=2,
  barrack_boss=12, plus tier2/tier3 boss *abilities* present (bosses alive & armed).

So there is exactly **one lane tower per lane per team = the tier2 Walker**. No
front Guardian phase exists in the map's authored objective set. The tier3 Base
bosses (2) ARE present — a user reporting "only walkers and base boss" is in fact
seeing the complete set; the only thing absent is tier1, which isn't there to spawn.

## The decisive new fact: mode=Invalid, yet everything else spawned

Runtime `GameRules`: `state=GameInProgress` already at +5s
(`stateStart` clock≈2.0), `gameMode=Invalid`, `matchMode=Invalid`, matchId=0.

Two things this proves:
1. **"Match-start / game-start event never fires" is REFUTED.** The engine
   reaches `GameInProgress` on its own within ~2s of map load, no GC, no pregame
   help, no plugin nudge. (My pregame-countdown hypothesis was wrong.)
2. **Mode does NOT gate objective spawning.** Under `gameMode=Invalid /
   matchMode=Invalid`, the tier2 Walkers, tier3 Base bosses, Patron/watcher
   buildings, and t3 generators ALL spawned correctly. If Invalid-mode suppressed
   boss spawns, none of them would exist. Since every other tier spawns fine under
   Invalid, tier1's absence cannot be mode-gating — it is **map-authored
   absence**. dl_midtown (3-lane Midtown) ships without a tier1 Guardian phase.

## Correction to 2026-05-04 note

That note reached the right CONCLUSION ("dl_midtown has no tier1") but via a wrong
premise: it claimed `gameMode/matchMode` were "already Normal/Unranked by default,
no plugin work required." **They are actually `Invalid`** on a plugin-hosted server
with no GC. The conclusion survives because of the stronger argument above
(all-other-tiers-spawn-under-Invalid), not because the modes were correct.

## Implication for the user's request

There is no spawn bug to fix: the front Guardians the user expects are not part of
this map build's objective layout. Options if front Guardians are genuinely wanted:
verify the mounted game-files version actually contains them in live play (the
earlier "vanilla matchmaking shows guardians" claim may be terminology — Walkers
called "Guardians" — or a different map/patch), or manual spawn (blocked: AI-NPC
managed spawn crashes natively, see boss-wave note). Setting game/match mode would
NOT help — proven above.
