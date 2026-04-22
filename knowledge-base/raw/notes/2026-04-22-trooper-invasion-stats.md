---
date: 2026-04-22
task: PostHog stats pipe for TrooperInvasion (difficulty calibration)
files:
  - TrooperInvasion/Stats/StatsClient.cs
  - TrooperInvasion/TrooperInvasion.cs
  - docker-compose.yml
  - .env.example
---

## Why stats at all

Concrete use case: **"is TrooperInvasion too easy or too hard?"** —
balance-tuning, not engagement. Session-level outcome records are the
answer shape (highest wave, round outcome, duration, player count,
deaths per wave), sliceable by cohort.

## Why inline, not a shared `Stats/` plugin

The repo **already prefers local duplication over cross-plugin static
calls**. Evidence: `Deathmatch.cs:698` has a verbatim copy of
`HeroSelect.FuzzyMatchHero` with the comment "Duplicated from
HeroSelectPlugin.FuzzyMatchHero — keeping a local copy avoids a ...".
Each plugin DLL loads into its own ALC; cross-ALC static calls are
either not supported or deliberately avoided.

So: the Stats client lives inside `TrooperInvasion/Stats/` as an
`internal static class StatsClient`. When Deathmatch / LockTimer get
the same treatment later, duplicate the client (matching the existing
convention) OR invest in making it cross-ALC-safe at that point. Not
now.

## Why PostHog Cloud

Backend decision matrix:
- **Extend api.deadlock-api.com** — user controls it but doesn't want
  to hand-roll the analytics UI.
- **PostHog self-hosted** — ops burden (another Docker stack).
- **PostHog Cloud** — chosen. Zero ops. Free tier (1M events/month)
  covers this easily; TI emits ~6 events per wave at most plus one
  outcome per session.
- **ClickHouse + Grafana** — overkill for the current scale.

## SteamID hashing

Raw `CBasePlayerController.PlayerSteamId` (ulong) is **never** sent
externally. `StatsClient.HashSteamId` does HMAC-SHA256(salt,
le_bytes(steamid)) → 64-char lowercase hex. Salt is in
`DEADWORKS_ENV_STATS_SALT`.

Stability contract: **rotating the salt breaks all cross-session
player grouping.** Generate once with `openssl rand -hex 32`, check
into the deploy secrets store, never touch it.

## Event taxonomy

All event names prefixed `ti_` so future gamemodes (e.g. `dm_` for
Deathmatch) don't clash in the shared PostHog project.

| event | distinct_id | fires in | key props |
|-------|-------------|----------|-----------|
| `ti_round_started` | server_id | `ArmWaves` | round, players |
| `ti_wave_started` | server_id | `RunWave` after `_waveNum++` | wave, round, players, deaths_prev_wave, alive_enemy_troopers, active_lanes, gold_reward |
| `ti_session_outcome` | server_id | `HandleVictory` / `HandleDefeat` / last-disconnect-when-not-over | outcome (victory\|defeat\|abandoned), highest_wave, highest_round, total_waves, duration_s, peak_players, avg_players |
| `ti_player_joined` | hash(steamid) | `OnClientFullConnect` | current_wave, current_round, hero_id |
| `ti_player_left` | hash(steamid) | `OnClientFullDisconnect` | session_duration_s, wave, round, was_mid_round, players_remaining |
| `ti_player_died` | hash(steamid) | `OnEntityKilled` (non-patron human pawn) | wave, round, hero_id |

## Session semantics

A "session" spans from first-player-joins-empty-server to either
victory, defeat, or last-player-leaves. `_sessionStartUtc = null`
means no active session — it gates `EmitSessionOutcome` so
HandleDefeat → last-disconnect cascade doesn't double-fire the
outcome event.

`ResetSessionStats()` called from `OnStartupServer` (map change) and
from the last-disconnect branch of `OnClientDisconnect`.

## Cross-wave deaths-per-wave counter

`_deathsThisWave` increments on any human-player kill (non-patron). At
the next wave start, the count rolls into `_deathsPrevWave` and the
new wave's `deaths_prev_wave` property, then resets. Wave 1's value is
always 0 by design.

## Fire-and-forget discipline

Copied from `LockTimer/Records/MetricsClient.cs`:
- Singleton `HttpClient` with 10s timeout.
- `Capture` returns immediately after `_ = SendAsync(...)`.
- `SendAsync` has a blanket try/catch that `Console.WriteLine`s the
  failure and drops it. No retries, no backoff, no queue. If the
  transport dies the game server keeps running.
- `Capture` is a silent no-op when `POSTHOG_KEY` or `STATS_SALT` is
  unset — `StatsClient.Enabled` guards every call site where we'd
  otherwise construct the props dict unnecessarily (hot paths like
  player death).

## Difficulty-curve dashboard (PostHog side, not code)

- X: `peak_players` bucketed (1-2, 3-4, 5-8, 9-16, 17-32).
- Y: P50 and P90 of `highest_wave` from `ti_session_outcome`, split
  by `outcome`.
- Read:
  - P50 wave == `RoundLength` → too easy for that bucket.
  - P50 wave < 3 with `outcome=defeat` → too hard.
  - `abandoned` dominating → boredom / frustration signal.
