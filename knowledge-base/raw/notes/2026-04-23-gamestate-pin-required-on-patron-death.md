---
date: 2026-04-23
task: Fix TrooperInvasion disconnecting all clients when a Patron dies
files:
  - TrooperInvasion/TrooperInvasion.cs
  - Deathmatch/Deathmatch.cs
---

The wiki's TrooperInvasion page claimed `HookResult.Stop` on
`gameover_msg` + `round_end` was sufficient to keep the mode in-progress
indefinitely "without touching the HUD clock or m_eGameState". In
production this is false: when `npc_barrack_boss` dies, the engine
flips `CCitadelGameRules.m_eGameState` to `PostGame` (0x8) at the
schema layer, which triggers the client-side "match over" flow
independent of the `gameover_msg` / `round_end` events. Observed
symptoms: all clients auto-disconnect the moment the Patron dies, and
the server then refuses new connections until a map reload.

## Two possible fixes

**A. Per-tick GameState pin** (what Deathmatch does at
`Deathmatch/Deathmatch.cs:149-152`). Read `m_eGameState` every tick;
if it has drifted from `GameInProgress`, write it back. Works but
spends a schema read/write every single tick in the lifetime of the
server.

**B. Pre-empt the lethal blow** (what TrooperInvasion now does).
Override `OnTakeDamage`; if the target is `npc_barrack_boss` and
`entity.Health - info.Damage <= 0`, zero out the damage, pin HP to 1,
and call `HandleVictory/HandleDefeat` ourselves. The Patron never
actually reaches 0 HP, so the engine never transitions out of
`GameInProgress`, so no schema pin is needed and there is no per-tick
cost. Event-only hook — fires only on actual damage events.

Once `_modeOver` is latched, further damage on the Patron is also
zeroed (not just the lethal hit), so stray trooper auto-attacks during
the post-mode cooldown can't re-trigger the handler or be visible as
"Patron took damage" after the defeat toast.

TrooperInvasion picks **B** over **A** because:
- Deathmatch already runs a per-tick timer for the match clock, so the
  GameState pin is free. TrooperInvasion has no such timer — adding one
  just for this would pay a full tick budget for a rare event.
- Pre-empt approach means `OnEntityKilled` no longer needs to watch
  for Patron deaths; the Patron literally never dies.

## Diagnostic hints for future agents

- Managed `try/catch` doesn't see the kick — it happens in the engine's
  own session teardown after schema sees `eGameState != GameInProgress`.
- Symptom appears exactly when the first Patron dies, which masked it
  during normal dev because early testing rarely triggered an actual
  Patron death under the horde scaling.
- `EGameState` enum values in
  `DeadworksManaged.Api/Enums/GameRulesEnums.cs`: `GameInProgress = 0x7`,
  `PostGame = 0x8`. Deathmatch checks for any non-`GameInProgress` and
  forces back.
