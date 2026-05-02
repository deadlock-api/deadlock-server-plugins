---
date: 2026-05-02
task: Fix TrooperInvasion world state not resetting after Defeat/Victory
files: [TrooperInvasion/TrooperInvasion.cs]
related-commits: [5f4d527]
---

The `BeginPostModeCooldown` reset only touched plugin-side bookkeeping (`_roundNum`,
`_modeOver`, `_aliveEnemyTroopers`, etc.). Engine-side world state carried over into the
next round: Patron HP was pinned to 0 by the `OnTakeDamage` absorb-and-hold (never
un-pinned after EndMode), dead Base Guardians / Walkers / Guardians remained destroyed
on the map, and the trooper spawn machinery reflected the end-of-round state rather than
a fresh map start.

`citadel_match_end`, `mp_restartgame`, and `citadel_street_brawl_reset` don't apply on
`dl_midtown` in standard mode (they either crash, no-op, or reload the game rules without
restarting the map). The only reliable engine-level reset is `changelevel`.

Fix: replace the cooldown body with the `AutoRestartPlugin` pattern — a
`Timer.Sequence(...).CancelOnMapChange()` countdown with chat warnings at T-30, T-10,
T-5 seconds, then `Server.ExecuteCommand("changelevel " + Server.MapName)`. After the
level reload, `OnStartupServer` re-runs and is now the single source of truth for all
plugin re-init (previously some init was duplicated in `BeginPostModeCooldown`).

Key consequence: all per-round state (including `_starterGoldSeeded`, `_waveNum`, etc.)
is reset naturally because the plugin ALC reloads with the map. No need for explicit
field clearing — the `OnStartupServer` path already does it.

Footgun avoided: `CancelOnMapChange()` on the countdown timer means if the server
operator manually issues `changelevel` before the 30s countdown completes, the pending
timer is cancelled cleanly and no double-changelevel occurs.
