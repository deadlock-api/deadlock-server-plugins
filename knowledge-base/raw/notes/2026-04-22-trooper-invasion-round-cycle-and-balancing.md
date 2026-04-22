---
date: 2026-04-22
task: Round cycling + join/leave-robust balancing for TrooperInvasion
files: [TrooperInvasion/TrooperInvasion.cs]
---

Iterated TrooperInvasion from "runs forever, _waveNum climbs unbounded" to a
round-based arcade loop robust to arbitrary join/leave.

**Round structure.** `RoundLength = 20` waves per round, then 2-second
post-burst delay, `IntermissionSeconds = 30` announcement window, then
auto-rearm. At intermission: `DisarmWaves` resets `_waveNum=0`, cancels
pending timers, culls all troopers; `_roundNum++`; `_starterGoldSeeded`
cleared so anyone still on server re-seeds 2500 gold at their next spawn.
Bounded wave counter means bounded catch-up gold, bounded bounty, and
natural "round X" narrative beats. Player items/AP/accumulated gold
persist across rounds — only the horde counter resets.

**Timer tracking.** All scheduler timers tracked via `IHandle` fields
(`_pendingWaveTimer`, `_pendingBurstEnd`). `DisarmWaves` cancels both.
Without this, rapid disarm/rearm cycles stacked grace timers →
multiple back-to-back waves. Also added internal `if (!_wavesActive) return;`
guards inside every timer lambda as belt-and-suspenders.

**Player-scaled tuning knobs** (all recomputed per wave, so join/leave mid-round
adjusts the next wave automatically):
- Wave interval: linear 20s (1p) → 5s (32p)
- Burst seconds: 0.75s (1p) → 6s (32p) × onboarding ramp (0.35/0.55/0.8/1.0 for waves 1/2/3/4+)
- Trooper cap: 80 (1p) → 600 (32p) — tracked via `_aliveEnemyTroopers` HashSet synced in `OnEntitySpawned`/`OnEntityDeleted`, zero per-tick scans
- Active-lane bitmask: `(1 << lanes) - 1` where `lanes = Clamp(humans/2, 1, 4)` — "at least 2 players per active lane" rule
- Starter-gold catch-up: `2500 + max(0, _waveNum - 1) * 500`

**Event-driven empty-server cleanup.** Originally polled every 5s; now
handled purely in `OnClientDisconnect` when `HumanPlayerCount` excluding
the leaver hits 0 → `DisarmWaves` (spawn off + cull + timer cancel +
`_waveNum = 0`) plus `_roundNum = 1`, `_modeOver = false`,
`_starterGoldSeeded.Clear()`. No polling cost.

**Strict enemy team filter.** `OnEntitySpawned` now checks
`TeamNum == EnemyTeam (3)` explicitly instead of `!= HumanTeam`. Neutral/
team-0/team-1 troopers (if any) are ignored rather than tracked as
enemies. Friendly (team-2) troopers still culled via deferred-Remove.

**Map-change handling.** `OnStartupServer` resets all state fields
(`_wavesActive`, `_modeOver`, `_waveNum`, `_roundNum`, both HashSets,
cancels pending timers). Timers that should not leak across maps
(`Timer.Every`) registered via `.CancelOnMapChange()`.

**Match-clock code deleted entirely.** Earlier iterations wrote `m_flGameStartTime`
+ 4 companion fields every tick to force `EGameState.GameInProgress` and
anchor the HUD clock. Turns out the engine extrapolates forward from the
anchor fields indefinitely, so per-tick rewrite is redundant. And even a
once-per-round anchor is overkill: the engine's native HUD clock and
state machine run correctly on their own as long as `OnGameoverMsg` and
`OnRoundEnd` still return `HookResult.Stop` (which they do, to prevent
match-end). Result: deleted 6 schema accessors and the `Timer.Every(1.Ticks)`
ticker. Plugin still holds the game in-progress indefinitely via the
gameover hooks alone.

**UnlockFlexSlots timing.** Moved from `OnStartupServer`'s
`Timer.Once(1.Seconds())` to `OnClientFullConnect`'s
`Timer.Once(1.Seconds()).CancelOnMapChange()`. Reason: `CCitadelTeam`
entities don't exist on an empty map, so a boot-time call no-ops. Running
it per-player-join (idempotent — guards on current bit state) ensures
the team schema is populated by the time we touch it.
