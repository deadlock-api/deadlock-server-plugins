---
date: 2026-06-16
task: fix TrooperInvasion patron kill intercepting the native game-over
files: [TrooperInvasion/TrooperInvasion.EndMode.cs]
---

`HookResult.Continue` in `OnTakeDamage` passes the damage through to the engine even if you zero `args.Info.Damage` first — the engine still processes the entity kill and flips `m_eGameState → PostGame`, kicking every client. The events-surface wiki table documents this: "Stop blocks damage applied". To actually swallow a lethal blow you must return `HookResult.Stop`. All absorb paths in `OnTakeDamage` (modeOver guard, weaken absorb, non-real-kill, debounce, final-phase kill) were changed from `Continue` to `Stop`. The `args.Info.Damage = 0f` write is now redundant for Stop paths and was removed; `args.Entity.Health` pins are kept since they ensure correct health state after Stop.

Safety added to `OnGameoverMsg`: if `!_modeOver` when the event fires (meaning `OnTakeDamage` failed to intercept), sets `_modeOver = true` and calls `DoChangeLevel("safety-reset")` immediately (no 30-second countdown, since players are likely already kicked by the native PostGame transition).
