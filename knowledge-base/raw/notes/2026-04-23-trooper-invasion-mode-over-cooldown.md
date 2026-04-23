---
date: 2026-04-23
task: fix TrooperInvasion `_modeOver` dead-state trap after victory/defeat
files:
  - TrooperInvasion/TrooperInvasion.cs
---

`_modeOver = true` is set in `HandleVictory` / `HandleDefeat`
(`TrooperInvasion.cs:508-524`) but nothing inside the wave loop ever
unlatches it. The only place `_modeOver = false` previously lived was
the *last-human-disconnect* branch of `OnClientDisconnect` at
`TrooperInvasion.cs:755-766`. Consequence: a server that just won
wave 30 (or got its Patron destroyed) stays permanently stuck for every
player who remains connected — and crucially, new joiners during that
latched window land in a silent server (`ArmWaves` no-ops on
`_modeOver`), giving the public impression that "the server is broken".
The only way out was for every human to leave.

Fix: added a `BeginPostModeCooldown(outcome)` helper invoked from both
`HandleVictory` and `HandleDefeat`. It immediately calls `DisarmWaves`
(spawn off, cull, cancel wave/burst timers, `_waveNum = 0`) to guarantee
no residual waves fire during the cooldown, then schedules a single
`PostModeCooldownSeconds = 30f` timer on `_pendingWaveTimer`. When that
fires it clears `_modeOver`, resets `_roundNum = 1`, clears
`_starterGoldSeeded`, and wipes session-stats accumulators in-line
(peak/sample counters + per-wave death counters) — deliberately NOT
calling `ResetSessionStats` because that clears `_playerJoinTimes`,
which would zero out `session_duration_s` for players who stayed
connected through the victory/defeat. Their connection never dropped,
so their session duration should keep counting from their original
join. `_sessionStartUtc` is already null at this point (cleared by
`EmitSessionOutcome` during the outcome emission), so
`ArmWaves` → `EnsureSessionStarted` will open a fresh session for the
next ti_session_outcome event.

Ordering in `HandleVictory`/`HandleDefeat`:

1. `_modeOver = true`
2. `citadel_trooper_spawn_enabled 0` (redundant with DisarmWaves but
   harmless; closes the spawn window immediately rather than waiting
   for the DisarmWaves Server.ExecuteCommand)
3. `AnnounceHud("VICTORY!", "…Fresh round in 30s")`
4. `EmitSessionOutcome("victory")` — clears `_sessionStartUtc` as a
   single-shot, so subsequent calls no-op.
5. `BeginPostModeCooldown("victory")` — DisarmWaves then 30s timer.

`_modeOver` intentionally STAYS true through the cooldown so that
`OnClientFullConnect` → `ArmWaves` calls from anyone joining during the
cooldown no-op. They get the fresh round automatically when the timer
fires.

Interaction with the existing disconnect-reset path: if the last human
leaves during the cooldown, `OnClientDisconnect` calls `DisarmWaves`
which cancels `_pendingWaveTimer`. That pre-empts the cooldown timer,
and the disconnect path does its own full reset
(`_modeOver = false; _roundNum = 1; ResetSessionStats`). Next joiner
starts entirely fresh. No double-fire.

The AnnounceHud toast at victory/defeat is one-shot; the engine holds
it on-screen long enough for players to read. The follow-up
`AnnounceHud("ROUND 1", …)` from `ArmWaves` serves as the visible
"we're back" signal when the cooldown expires.
