# KingOfTheHill — Product Plan

> Status: planning document. No C# yet. API calls referenced here are drawn
> from the existing `Deathmatch`, `LockTimer`, and `TrooperInvasion` plugins
> and the wiki pages `[[plugin-api-surface]]`, `[[events-surface]]`,
> `[[deadlock-game]]`. Anything not yet confirmed against the runtime is
> collected under **Open questions** at the bottom — treat those as blocking
> spikes before the matching feature is built.

## Overview

KingOfTheHill (KOTH) is a single-objective territory-control gamemode for
`dl_midtown`. One contested **control point** sits on the map. While exactly
one team occupies it, that team *holds* the point and is rewarded every second
with both an abstract **score** and real Deadlock **item gold** (souls) split
among the holders — keeping the familiar economy loop alive. The first team to
reach the score limit wins. To prevent a single team from turtling one spot,
the point **relocates** every few minutes to a different lane mid-point,
announced in chat. Lane Walkers and other map NPCs keep their normal behaviour
and are **not** objectives; KOTH only cares about who stands in the active
zone. The mode never lets the engine declare its own winner — like Deathmatch,
it pins `m_eGameState` to `GameInProgress` and suppresses `gameover_msg` /
`round_end`, driving its own win condition and reset.

## Game loop (server start → game end)

1. **Map load** (`OnStartupServer`): disable native NPC-driven win
   objectives if desired (or leave Walkers alone — they are not objectives;
   see Plugin composition). Capture Walker positions to derive lane
   mid-points (same capture pattern as `Deathmatch.cs:114-120`). Compute the
   candidate point-location list. Enter **Warmup**.
2. **Warmup**: wait until `_humanCount >= MinPlayersToStart` (gated by
   `OnClientFullConnect` / `OnClientDisconnect`, mirroring Deathmatch's
   `_humanCount` bookkeeping). Show a "waiting for players" announcement.
   Reset both team scores to 0.
3. **Activate first point**: pick the starting location (map center), spawn
   the visual marker, announce "Point is LIVE at <location>", transition to
   **PointActive**.
4. **PointActive tick** (every `ScoreTickSeconds`, default 1s): scan players,
   classify the point as `Neutral` / `Held(team)` / `Contested`. If `Held`,
   grant score + gold to that team's occupants and emit nothing else. Re-emit
   the visual marker color to reflect state (optional).
5. **Feedback cadence** (every `FeedbackSeconds`, default 10s): chat line with
   current holder + score (`"Amber holds the point — 240/500"`).
6. **Rotation timer** (every `RotationSeconds`, default 210s = 3.5min): pick
   the next location (round-robin over the candidate list, excluding the
   current one), enter **PointMoving**, announce the upcoming spot, despawn the
   old marker, spawn the new one, return to **PointActive**.
7. **Win check** (inside the score tick): if either team's score `>= ScoreLimit`,
   transition to **GameOver**.
8. **GameOver**: announce winner via `CCitadelUserMsg_HudGameAnnouncement`,
   freeze scoring, hold for `PostGameSeconds`, then reset all state and return
   to **Warmup** for the next match (no map reload required, matching the
   Deathmatch "match never ends" model).

## Configuration

All tunables live on a `KingOfTheHillConfig` class bound with `[PluginConfig]`
(see `[[plugin-config]]`, hot-reloadable via `dw_reloadconfig` →
`OnConfigReloaded`). Defaults below.

| Key | Default | Meaning |
|-----|---------|---------|
| `ScoreLimit` | `500` | Score (point-seconds) a team needs to win |
| `ScorePerSecond` | `1` | Score added per tick while a team holds |
| `GoldPerSecondPerPlayer` | `40` | Souls granted per holding player per tick |
| `GoldGrantMode` | `"per_player"` | `per_player` (each holder gets the full rate) or `split` (rate divided among holders) |
| `PointRadius` | `350.0` | Sphere radius (world units) for occupancy test |
| `PointHeightTolerance` | `200.0` | Max vertical distance from point center to count (prevents bridge/below-map false positives) |
| `ScoreTickSeconds` | `1.0` | Scoring + win-check cadence |
| `RotationSeconds` | `210.0` | Time the point stays at one location |
| `FeedbackSeconds` | `10.0` | Chat status cadence |
| `MinPlayersToStart` | `2` | Humans needed to leave Warmup |
| `PostGameSeconds` | `15.0` | GameOver hold before reset |
| `ContestedScoring` | `false` | If `true`, the team with more bodies scores while contested; default `false` = no scoring when contested |
| `PointLocations` | *(derived)* | Optional explicit list of world positions; if empty, derive from Walker/lane mid-points (see Point locations) |
| `RotationOrder` | `"round_robin"` | `round_robin` or `random` (no immediate repeat) |
| `ShowMarker` | `true` | Spawn the visual zone marker entity |

Config validation **clamps in place** rather than throwing (repo idiom):
`PointRadius` floored at `50`, `ScorePerSecond >= 0`, `RotationSeconds >= 30`,
etc.

## State machine

```
            players >= MinPlayersToStart
  Warmup ─────────────────────────────────► PointActive
    ▲                                          │  │  ▲
    │ reset (after PostGameSeconds)            │  │  │ rotation done
    │                                          │  │  │
 GameOver ◄──── score >= ScoreLimit ───────────┘  └──┴── PointMoving
                                              (RotationSeconds elapsed)
```

- **Warmup** — no scoring, no marker (or a dim "pending" marker). Waiting for
  `MinPlayersToStart`. Falling below the threshold mid-match (everyone leaves)
  drops back here and resets scores; mirrors Deathmatch's empty-server gating.
- **PointActive** — the working state. Carries a sub-state recomputed every
  score tick from current occupancy:
  - `Neutral` — zero players in zone. No scoring.
  - `Held(team)` — players from exactly one team in zone. That team scores +
    gets gold.
  - `Contested` — players from both teams in zone. No scoring unless
    `ContestedScoring = true`.
- **PointMoving** — brief transition (single tick is fine): despawn old
  marker, choose + spawn new location, announce. Could optionally include a
  few-second "point relocating" grace where no one scores; default is
  instantaneous swap.
- **GameOver** — winner announced, scoring frozen, `PostGameSeconds` countdown
  to reset.

State lives in plain fields on the plugin (`_phase`, `_activeLocationIdx`,
`_scores[team]`, `_pointMoveAtTick`, `_pointState`), not entity-attached.

## Control point — occupancy detection

The point is a **sphere** centered on the active location (a `Vector3`),
radius `PointRadius`, with a vertical clamp `PointHeightTolerance`. This is the
simplest robust test and matches how Deathmatch already does proximity math
(`Vector3.Distance`, `Vector3.Dot`). We deliberately do **not** rely on engine
trigger volumes / `OnEntityStartTouch` for scoring (see Open questions) — a
polled spherical check on the per-tick player scan is deterministic and needs
no map entity.

Per score tick:

```
occupants(team) = count of alive hero pawns p where
    horizontalDist(p.Position, center) <= PointRadius
    AND abs(p.Position.Z - center.Z) <= PointHeightTolerance
```

Iterate `Players.GetAll()` → `controller.GetHeroPawn()?.As<CCitadelPlayerPawn>()`,
skip dead/null (`pawn.IsAlive`) and pawns not yet past the LockTimer 5s
`_slotReadyAt` guard (`LockTimerPlugin.cs:121-123`) to avoid granting gold to
half-initialized pawns post-respawn. Bucket by `pawn.TeamNum` (2 = Amber,
3 = Sapphire per `[[deadlock-game]]`). Then:

- `team2 > 0 && team3 > 0` → `Contested`
- `team2 > 0` xor `team3 > 0` → `Held(thatTeam)`
- both `0` → `Neutral`

The horizontal-distance variant (XY only, Z handled by the tolerance band)
avoids a tall cylinder picking up players on overpasses directly above the
point. LockTimer's `Zone.Contains` (AABB + margin, `Zones/Zone.cs:17`) is the
box alternative if a future location is better described by a box than a
sphere; the config could carry a per-location shape, but sphere-only is the
v1 scope.

## Point locations (dl_midtown)

The point cycles through a small set of world positions. Two ways to source
them, in priority order:

1. **Explicit config** — `PointLocations` as a list of `[x, y, z]` corners
   filled in once we have surveyed real coordinates (use the LockTimer `!pos`
   pattern — a `!point`/`!here` admin command that prints
   `pawn.Position` — to capture them live in-game; LockTimer does exactly this
   at `LockTimerPlugin.cs:236-246`).
2. **Derived from Walkers** — at startup, capture `npc_boss_tier2` (Walker)
   positions per team/lane just like Deathmatch (`Deathmatch.cs:114-120`,
   `m_eLaneColor` with bearing fallback). A lane's **mid-point** ≈ the midpoint
   between the two teams' Walkers on that lane. The **map center** ≈ centroid
   of all Walkers (`RecomputeMapCenter`, `Deathmatch.cs:354-360`).

Candidate set for v1 (5 locations, classic KOTH rotation):

- **Center** — map centroid. The opening point.
- **Yellow lane mid** (lane 1)
- **Green lane mid** (lane 3)
- **Purple lane mid** (lane 6)
- *(Blue lane 4 optional — Deathmatch skips Blue in its lane cycle;
  follow suit unless playtests want it.)*

Real coordinates are **unknown until surveyed**. All point locations are
derived dynamically from Walker midpoints at startup (same Deathmatch pattern,
`Deathmatch.cs:114-120`) — no hardcoded coordinates. During the first
playtest, an `!point here` admin command prints `pawn.Position` to console
(LockTimer `!pos` analogue), allowing better positions to be optionally baked
into `PointLocations` config afterwards.

## Point rotation

A timer drives relocation. Two viable implementations, matching existing
plugins:

- **`Timer.Every(...)` + elapsed check** like Deathmatch's `TickMatchClock`
  (`Deathmatch.cs:234-269`): one master tick compares `GlobalVars.CurTime`
  against a stored `_pointActivatedAt`, fires `RotatePoint()` when
  `elapsed >= RotationSeconds`. Preferred because we already run a per-tick /
  per-second loop for scoring.
- Or a dedicated `Timer.Once(RotationSeconds.Seconds(), RotatePoint)`
  re-armed each rotation.

`RotatePoint()`:

1. Pick next index (`round_robin`: `(idx+1) % count`; `random`: any other
   index).
2. Despawn old marker, spawn new marker at the new location.
3. Announce. **Chat is the v1 cue** (`Chat.PrintToChatAll`), plus a
   `CCitadelUserMsg_HudGameAnnouncement` banner like Deathmatch's rotation
   (`Deathmatch.cs:603-607`): title `"Point Relocated"`, description
   `"Now contested: Green lane mid"`.
4. Reset `_pointActivatedAt`. Scores carry over (rotation does not reset
   score).

**Audio cue (optional, stretch):** the `Sounds` / `SoundEvent` builder exists
(`[[plugin-api-surface]]`) — a horn on relocation would be nice-to-have but is
not required for v1.

## Scoring

On each score tick where state is `Held(team)`:

- **Score**: `_scores[team] += ScorePerSecond`.
- **Gold**: for each holding pawn of that team, grant souls via
  `pawn.ModifyCurrency(ECurrencyType.EGold, amount, ECurrencySource.ECheats,
  silent: true, forceGain: true)`
  (signature confirmed at
  `deadworks/managed/DeadworksManaged.Api/Entities/CCitadelPlayerPawn.cs:76`).
  `ModifyCurrency` **adds** (unlike `SetCurrency` which sets absolute, used by
  Deathmatch/TrooperInvasion for the 999_999 god-economy). `ECurrencySource.ECheats`
  is the most permissive source and avoids any cap logic. `forceGain: true`
  as an additional bypass — verify in a live test that it actually suppresses
  per-tick cap enforcement. `silent: true` suppresses the per-tick +gold popup
  (60 popups/min would be spammy); the accumulated gold is surfaced instead in
  the periodic chat feedback line (e.g. `"Amber holds the point — 240/500, +40 gold/s"`).
  - `GoldGrantMode = per_player`: each holder gets `GoldPerSecondPerPlayer`.
  - `GoldGrantMode = split`: each holder gets
    `GoldPerSecondPerPlayer * <baseline holders> / <actual holders>` (or simply
    `TotalGoldPerSecond / holders`) so stacking the point doesn't multiply
    economy.

**Display**: score is shown via the periodic chat feedback (below) and the
`HudGameAnnouncement` banner on milestones / win. A persistent on-screen HUD
widget is out of scope for v1; the running counter lives in chat + announcements.

## Win condition

Checked inside the score tick after incrementing: if
`_scores[holdingTeam] >= ScoreLimit`, that team wins immediately. Announce with
`CCitadelUserMsg_HudGameAnnouncement` (title `"Amber Team Wins!"`, description
`"Final: Amber 500 — Sapphire 437"`) + `Chat.PrintToChatAll`, then enter
**GameOver**. Because the engine's own match end is suppressed
(`OnGameoverMsg` / `OnRoundEnd` return `HookResult.Stop`, and `m_eGameState` is
pinned to `GameInProgress` — Deathmatch pattern at `Deathmatch.cs:267-283`),
the win is entirely plugin-driven.

Tie/edge: scores are integers incremented one team at a time, so an exact
simultaneous reach is impossible — the team that crosses the threshold on its
tick wins.

## End-of-game / reset

In **GameOver**, after `PostGameSeconds`:

- Zero `_scores`.
- Reset `_activeLocationIdx` to center, `_pointActivatedAt`.
- Despawn marker; re-enter **Warmup** (or jump straight to first point if
  enough players remain).
- Optionally emit a stats event (`koth_game_outcome`) following the
  Deathmatch/TrooperInvasion `StatsClient.Capture` pattern (winner, duration,
  peak players, rotations) — nice for parity but not required for v1.

No map reload: the match loops in place, consistent with how Deathmatch and
TrooperInvasion run indefinitely.

## Chat commands

Registered with the unified `[Command("name")]` attribute (see
`[[command-attribute]]`). The `!` prefix is required at registration — e.g.
`[Command("!score")]` — consistent with repo convention.

| Command | Behaviour |
|---------|-----------|
| `!score` | Print current scores + holder: `"Amber 240 — Sapphire 180 (Amber holds)"` |
| `!point` | Print where the point is + time to next rotation: `"Point: Green lane mid. Relocates in 1:12."` Optionally pings distance/direction from caller using `pawn.Position`. |
| `!stuck` / `!suicide` | Kill self to respawn — reuse the existing `StuckCommand` plugin (already in other gamemode profiles) rather than re-implementing; if KOTH wants its own, copy Deathmatch's `CmdStuck` (`Deathmatch.cs:852-870`). |
| `!koth` *(optional)* | Help / list commands, like Deathmatch `!help`. |
| `!point here` *(admin/dev, optional)* | Print `pawn.Position` to console for surveying point locations — LockTimer `!pos` analogue, used only during coordinate capture. |

## HUD / feedback cadence

- **Every `FeedbackSeconds` (10s)** while in PointActive: one chat line
  reflecting current sub-state:
  - `Held`: `"[KOTH] Amber holds the point — 240/500, +40 gold/s"`
  - `Contested`: `"[KOTH] Point CONTESTED — Amber 240 / Sapphire 180"`
  - `Neutral`: `"[KOTH] Point is NEUTRAL — capture it! Amber 240 / Sapphire 180"`
- **On rotation**: banner + chat (see Point rotation).
- **On milestones** (optional): banner at e.g. 50%/80% of `ScoreLimit`.
- **On win**: banner + chat.
- **Visual marker**: attempt a `CParticleSystem` at the point position first
  — this is the preferred approach for a sphere/pillar capture-point visual.
  If particles don't render reliably on a live server, fall back to LockTimer's
  `env_beam` box renderer (`Zones/ZoneRenderer.cs`) sized to `PointRadius`.
  Marker **color reflects state**: neutral = white, held = holder team color
  (Amber/Sapphire), contested = yellow. Re-render on state change only (not
  every tick) to avoid entity churn — LockTimer renders once and leaves it
  (`LockTimerPlugin.cs:168-181`).

Chat is the authoritative feedback channel for v1; a richer always-on HUD
widget is out of scope for v1.

## Plugin composition (gamemodes.json)

Add a `king-of-the-hill` profile. Following the existing profiles' shape:

```json
"king-of-the-hill": [
  "StatusPoker",
  "KingOfTheHill",
  "Hostname",
  "FlexSlotUnlock",
  "TeamChangeBlock",
  "HealOnSpawn",
  "DisconnectCleanup",
  "HeroSelect",
  "Feedback",
  "StuckCommand"
]
```

Rationale (mirrors the `deathmatch` / `trooper-invasion` profiles):

- **StatusPoker** — present in every profile; server status.
- **Hostname**, **Feedback** — standard across all profiles.
- **FlexSlotUnlock** — players get full builds (KOTH is a fight-fest economy
  mode); same reason Deathmatch includes it.
- **TeamChangeBlock** — keep teams stable so scoring stays fair.
- **HealOnSpawn** + **DisconnectCleanup** — quality-of-life parity with
  Deathmatch/TrooperInvasion.
- **HeroSelect** (or **HeroSelectOnNextSpawn**) — let players pick heroes;
  choose the variant TrooperInvasion uses unless playtest says otherwise.
- **StuckCommand** — provides `!stuck` so KOTH doesn't reimplement it.

KOTH **keeps** Walkers/Guardians (they are not objectives) and suppresses the
win path via `gameover_msg` + `round_end` hooks returning `HookResult.Stop`,
and `m_eGameState` pinned to `GameInProgress` (Deathmatch pattern,
`Deathmatch.cs:267-283`). Additionally, lethal damage to both Patrons is
intercepted via `OnTakeDamage` — any hit that would reduce a Patron to 0 HP
returns `HookResult.Stop`, making Patrons effectively unkillable by lane AI.
This prevents lane creep from accidentally triggering a match end.

## Open questions

All ten original questions have been resolved. Decisions are recorded inline
in the relevant sections above.

**Summary of resolutions:**

| # | Topic | Decision |
|---|-------|----------|
| Q1 | `ECurrencySource` | `ECheats`; verify `forceGain: true` bypasses cap in live test |
| Q2 | Gold popup spam | `silent: true` per-tick; show accumulated gold in chat feedback line |
| Q3 | Zone trigger vs polling | Polled spherical checks; no trigger volumes |
| Q4 | Visible point marker | `CParticleSystem` first; fall back to `env_beam` box sized to `PointRadius` |
| Q5 | Real point coordinates | Derived from Walker midpoints at startup; `!point here` survey command for first playtest |
| Q6 | Lane creep ending the match | Suppress `gameover_msg` + `round_end` + pin `m_eGameState`; intercept lethal Patron damage via `OnTakeDamage` → `HookResult.Stop` |
| Q7 | Command prefix | `!` required at registration (`[Command("!score")]`) |
| Q8 | Persistent on-screen HUD | Out of scope for v1; chat + announcement banners are sufficient |
| Q9 | Contested behaviour | `ContestedScoring = false` (classic KOTH); config flag keeps variant open |
| Q10 | Gold for dead/respawning holders | Skip `!pawn.IsAlive`; apply 5s `_slotReadyAt` guard pattern |

**Remaining open item:**

- **`CParticleSystem` path for the capture-point marker** — confirm it renders
  correctly on a live server. If not, fall back to `env_beam` (see HUD /
  feedback cadence, Visual marker). No other open questions remain.
