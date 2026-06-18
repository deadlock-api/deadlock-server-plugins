# PayloadRace — Product Plan

> Status: **draft / pre-implementation**. This document is implementation-ready.
> One load-bearing unknown remains (Walker AI control mechanism — see
> [Open questions](#open-questions)); spike it first before building the advance/pause
> core. All other design decisions are resolved.

## Overview

PayloadRace is a symmetric "Walker Escort" gamemode for `dl_midtown`. Both teams
simultaneously push their own lane **Walker** (`npc_boss_tier2`, the Tier-2 lane
tower NPC) toward the enemy base. There is no cart entity: the Walker itself is the
payload. Each team must keep at least one ally inside an **advance zone** (a sphere
centered on their Walker) to keep it moving forward; if the zone is empty the Walker
pauses, and if an enemy also stands in the zone the push is **contested** and stalls.
First team to drive its Walker into the enemy base (Patron / base coordinates) wins.
Economy, souls, items, and respawns stay vanilla — this is a macro-strategy mode about
positioning and mid-game power spikes, not a pure aim arena. It is the inverse of
[TrooperInvasion's](../TrooperInvasion/TrooperInvasion.cs) PvE horde loop: PvP, two
symmetric teams, one objective per side.

## Game loop

```
Warmup  ──(enough players / timer)──▶  Active  ──(a Walker reaches enemy base | time limit)──▶  GameOver
  ▲                                                                                                  │
  └──────────────────────────────────  changelevel reset  ◀──────────────────────────────────────┘
```

### Warmup
- Entered on `OnStartupServer`. Walkers identified and frozen at their lane start.
- Players join, pick heroes, buy starting items. No Walker advances.
- Transition to **Active** when both teams have ≥ N players (config `MinPlayersToStart`,
  default 1 per team for testing) **or** a `WarmupMaxSeconds` fallback elapses.
- Mirror TrooperInvasion's auto-arm-on-join idiom: arm from the first
  `OnClientFullConnect` once the start condition is met (idempotent).

### Active
- Both Walkers are live. A poll timer (`Timer.Every`) evaluates each Walker's advance
  zone every tick-bucket and drives its per-Walker state machine (below).
- Progress is tracked and broadcast periodically to chat / HUD.
- A hard `MatchTimeLimitSeconds` fallback prevents infinite stalemates — at expiry the
  Walker that has progressed furthest wins (or draw if tied within epsilon).

### GameOver
- Triggered when a Walker reaches the enemy base (see [Win condition](#win-condition))
  or the time limit resolves.
- Suppress the engine's native gameover/round-end so we control presentation
  (TrooperInvasion / Deathmatch both `HookResult.Stop` on `gameover_msg` + `round_end`).
- Announce winner via `CCitadelUserMsg_HudGameAnnouncement`, freeze both Walkers,
  run a post-mode cooldown, then `changelevel <currentmap>` for a clean full reset
  (the only reliable engine-wide reset — see TrooperInvasion's "Post-mode world reset").

## Configuration

All tunables live on a `PayloadRaceConfig` `[PluginConfig]` object so they can be
hot-reloaded via `dw_reloadconfig` / `OnConfigReloaded`.

| Key | Default | Meaning |
|---|---|---|
| `AdvanceZoneRadius` | `1200f` | Radius (game units) of the sphere around a Walker that determines its state. Tune against hero engagement ranges. |
| `WalkerHealth` | `5× vanilla` | Walkers should survive focus-fire long enough for the race to matter; default to 5× vanilla HP. See [Walker death](#walker-death). |
| `AdvanceSpeed` | `engine default` | Forward speed of an advancing Walker. Only applicable if the spike selects the speed-field option; ignored for teleport-stepping (magnitude encoded in step vector). |
| `AdvanceSpeedBoost` | `1.0f` | Multiplier applied when the zone is held by allies and uncontested. Set `>1` for a "stacked = faster" incentive; `1.0` keeps it flat. |
| `PauseLagSeconds` | `1.5f` | Grace period after the zone empties before the Walker actually pauses (prevents flicker when allies briefly leave). |
| `ResumeLagSeconds` | `0.5f` | Grace before a paused/contested Walker resumes advancing once the zone is re-secured. |
| `ContestRequiresEnemyCount` | `1` | How many enemies in the zone make it "contested". |
| `MatchTimeLimitSeconds` | `1800f` | Fallback cap; furthest-progressed Walker wins at expiry. |
| `WarmupMaxSeconds` | `60f` | Warmup auto-start fallback. |
| `MinPlayersToStart` | `1` | Min players **per team** to leave warmup. |
| `ProgressBroadcastSeconds` | `20f` | Cadence of the periodic chat progress line. |
| `ContestAlertCooldownSeconds` | `8f` | Min spacing between "contested!" alerts per team. |

ConVars set in `OnStartupServer` (vanilla-economy posture, mirroring TrooperInvasion):
`citadel_player_spawn_time_max_respawn_time` left at vanilla, `citadel_allow_purchasing_anywhere`
left vanilla (we want shop-tethered macro play; revisit if testing says otherwise),
flex-slot unlock optional. Soul/gold income untouched.

## State machine (per Walker)

```
        ┌─────────────────────────────────────────────────────────┐
        │                                                         │
   ┌────▼─────┐  ally in zone, no enemy   ┌───────────┐           │
   │ Advancing │◀────────────────────────│  Paused   │           │
   └────┬─────┘                           └─────▲─────┘           │
        │  zone empties (after PauseLag)        │ no ally         │
        │───────────────────────────────────────┘                 │
        │                                                          │
        │  enemy enters zone (ally present)   ┌───────────┐        │
        │────────────────────────────────────▶ Contested │        │
        │◀────────────────────────────────────└───────────┘        │
        │  enemy leaves (after ResumeLag)                          │
        │                                                          │
        │  Walker HP → 0                                           │
        ▼                                                          │
   ┌──────────┐                                                    │
   │   Dead   │── instant loss (opponent wins) ──────────────────────┘
   └──────────┘
        │ reaches enemy base
        ▼
   ┌──────────┐
   │   Won    │  (terminal — triggers GameOver)
   └──────────┘
```

State definitions, evaluated each poll tick from the advance-zone scan:

- **Advancing** — ≥1 ally in zone, 0 enemies (or < `ContestRequiresEnemyCount`). Walker
  moves toward enemy base. Apply `AdvanceSpeedBoost` if held.
- **Paused** — 0 allies in zone (after `PauseLagSeconds` debounce). Walker holds position.
- **Contested** — ≥1 ally **and** ≥ `ContestRequiresEnemyCount` enemies in zone. Walker
  holds position; fire a throttled "Walker contested!" alert.
- **Dead** — Walker HP reached 0. Instant loss for this team; opponent wins. See [Walker death](#walker-death).
- **Won** — Walker reached the enemy base. Terminal; raises GameOver.

Implementation: a small `enum WalkerState` + a per-team struct holding
`{ CBaseEntity walker, WalkerState state, float lastAllyPresentTime, float lastEnemyPresentTime,
float progress01, float lastContestAlertTime }`. Debounce transitions using the
`lastAllyPresentTime`/`lastEnemyPresentTime` timestamps against `PauseLag`/`ResumeLag`,
exactly like Deathmatch's spawn-protection timestamp pattern.

## Walker management

Walkers (`npc_boss_tier2`) **already exist** on `dl_midtown` — they are map-placed
(confirmed: TrooperInvasion leaves them intact as gameplay content; Deathmatch removes
them). Each carries `TeamNum` (2 = Amber, 3 = Sapphire) and `m_eLaneColor`
(1=Yellow, 4=Blue, 6=Purple). There are 3 Walkers per team.

### Identifying each team's Walker
- At `OnStartupServer`, iterate `Entities.All`, collect every entity with
  `DesignerName == "npc_boss_tier2"`, bucket by `(TeamNum, lane)` — reuse Deathmatch's
  `TryReadLaneColor` + bearing-fallback logic (the lane schema read can race spawn and
  return 0; fall back to bearing around team centroid).
- Also handle late spawns in `OnEntitySpawned` (Deathmatch does this — the Walker may
  not be present at the first startup sweep).
- **Single-lane v1:** Yellow lane (lane 1, `m_eLaneColor == 1`) is the active race
  lane — one Walker per team. Matching DuelArena's "rank 1 arena" precedent. The other
  two Walkers per team are skipped in the movement loop (not removed; just ignored).
  Three-lane mode is deferred to v2.

### Controlling Walker movement

This is the highest-risk part of the implementation. The implementer **must spike
this first on a live server** before building the advance/pause core. Use the
following decision tree in order:

1. **Schema freeze/disable flag (preferred).** Look for a per-entity "frozen",
   "AI-disabled", or "movement-locked" schema flag on `npc_boss_tier2`. If one exists,
   toggle it: clear the flag for Advancing, set it for Paused/Contested. This is the
   cleanest path — no fighting the engine's navigation.
2. **Per-entity movement-speed field.** If there is a writable speed scalar on the
   entity, set it to 0 for Paused/Contested and to `AdvanceSpeed × boost` for
   Advancing. Only use if (1) does not exist.
3. **Teleport-stepping (accepted v1 fallback).** Drive the Walker manually: each poll
   tick when Advancing, call `walker.Teleport(currentPos + stepVector)` where
   `stepVector` points along the lane path toward the enemy base at magnitude
   `AdvanceSpeed × dt × boost`. Skip the teleport when Paused/Contested. To prevent
   the native lane AI from fighting the teleport, neutralize it via whatever schema
   write keeps it from re-running navigation (set passive / disable nav — prefer schema
   writes over anything that triggers AI init, given the boss-wave `Spawn()` AV history
   in TrooperInvasion). Note: a straight-line step may clip geometry; use intermediate
   waypoints if needed.

The spike determines which option is viable. Document the result before wiring up the
state machine.

### Lane path / waypoints
For teleport-stepping (and to measure progress), we need the lane's route from each
team's Walker start to the enemy base. Options:
- Derive from the two opposing same-lane Walker positions plus the enemy Patron/base
  position — a polyline through known lane landmarks. TrooperInvasion's Guardian
  self-spawn work already extracted authored lane node origins from the `dl_midtown`
  entity lump (`guard_boss_name` zipline-node origins) — that data is a candidate
  source of lane waypoints.
- Simplest v1: straight segment from own-Walker-start → enemy-base-coordinate, accepting
  that the Walker may path-clip; refine with intermediate waypoints once the basic loop
  works.

## Advance zone

A sphere of radius `AdvanceZoneRadius` centered on the **live** Walker position
(recompute center each poll — the Walker moves). Per poll tick, for the active team's
Walker:

```
allies   = count of alive players where TeamNum == walker.TeamNum   && dist(player, walker) <= R
enemies  = count of alive players where TeamNum != walker.TeamNum   && dist(player, walker) <= R
```

Iterate players via `Players.GetAllPawns()` (Deathmatch uses this), filter `IsAlive`,
compare `Vector3.Distance(pawn.Position, walkerPos)` against radius (squared-distance
compare for cheapness). Derive the next state from `(allies, enemies)` per the state
machine, with `PauseLag`/`ResumeLag` debouncing.

Polling cadence: `Timer.Every(1.Ticks(), …)` is overkill; a coarser
`Timer.Every(250.Milliseconds(), …)` (or every N ticks) is plenty for zone occupancy
and cheaper. Movement stepping, if teleport-based, should run on the same or a slightly
finer timer so motion is smooth.

> Alternative to manual distance polling: trigger volumes via
> `OnEntityStartTouch`/`OnEntityEndTouch` if we can spawn a sphere trigger parented to
> the Walker. More event-driven but requires creating/moving a trigger entity that
> tracks a moving Walker — likely more trouble than the cheap distance poll. **Poll is
> the recommended v1 approach.**

## Walker progress tracking

Progress is a normalized `progress01 ∈ [0, 1]`. v1 uses a straight-line projection:

```
unit  = normalize(enemyBaseCoord - ownWalkerStart)
progress01 = clamp( dot(currentPos - ownWalkerStart, unit) / totalDist, 0, 1 )
```

where `totalDist = length(enemyBaseCoord - ownWalkerStart)`. Projecting onto the
start→base axis means lateral drift does not inflate progress. v2 can accumulate
arc-length along a lane polyline if straight-line clips geometry badly.

Capture `ownWalkerStart` and `enemyBaseCoord` once at warmup. `enemyBaseCoord` = the
enemy Patron (`npc_boss_tier3`) position, or a fixed map coordinate near it. Store both
per team. The periodic broadcast and `!walker` command both read `progress01`.

## Win condition

A Walker reaching the enemy base ends the match for that team. Detection options:

1. **Proximity (recommended, robust):** when `progress01 >= 1.0` — i.e. the Walker's
   projected distance reaches the enemy base coordinate within a small `ArriveEpsilon`
   (e.g. 300 units) — declare that team the winner. Pure geometry, no dependency on
   damage events. Works with teleport-stepping naturally (we control the position).
2. **Patron damage/death (alternative):** if the design wants the Walker to actually
   destroy the enemy Patron, watch `OnTakeDamage` / `entity_killed` on `npc_boss_tier3`
   with the Walker as attacker. This couples to the engine's NPC combat and the Patron's
   two-phase death (see TrooperInvasion's two-phase Patron handling) — more fragile.
   **Defer.** v1 uses proximity arrival, not Patron destruction.

On arrival: set that Walker's state to **Won**, raise GameOver, suppress native events,
announce, cooldown, changelevel.

## Walker death

If a Walker's HP reaches 0 (enemy team focuses and kills it), the team whose Walker
died loses immediately — their opponent wins. Detect via `OnTakeDamage` final lethal
blow on the team's `npc_boss_tier2` (mirror TrooperInvasion's lethal-blow interception
pattern) or by polling `walker.IsAlive` in the same poll loop.

Walker respawn with penalty is **out of scope for v1** — self-spawning `npc_boss_tier2`
is unverified and lane-AI spawns have crashed before (TrooperInvasion boss-wave AV).

**Set `WalkerHealth` to 5× vanilla** (the config default) so the Walker can't be
trivially bursted. The race — not a Walker gank — should decide the match.

Because the Walker is contestable by enemies standing in its zone, expect Contested
state and Walker-damage to naturally overlap.

## Economy

Vanilla. Normal soul income (last-hits, denies, kills), items purchasable as usual,
flex slots optionally force-unlocked (schema-write pattern from Deathmatch/TrooperInvasion)
if we want full builds. No starter-gold injection, no cooldown scaling, no ability
max-upgrade. The mode's depth comes from team coordination and mid-game power, so the
real economy must stay intact. This matches TrooperInvasion's "removed the god-mode
shortcuts" posture.

## Respawn

Standard. No respawn-point override (unlike Deathmatch, which retargets spawns per lane).
Players respawn at their own base via engine default and walk/zipline to the active lane.
Optionally keep `HealOnSpawn` in the composition for forgiveness. Leave
`citadel_player_spawn_time_max_respawn_time` at vanilla so death has weight and a wiped
team genuinely loses zone control for the respawn window — that lull is the core
push-vs-defend rhythm.

## HUD / feedback

- **Periodic progress line** (chat, every `ProgressBroadcastSeconds`):
  `[PR] Amber Walker: 67%  —  Sapphire Walker: 45%`
  Use team color names (Amber/Sapphire) from Deathmatch's `TeamName`.
- **State-change toasts** (HUD via `CCitadelUserMsg_HudGameAnnouncement`, used sparingly
  to avoid spam):
  - On entering Contested: `"<Team> Walker Contested!"` (throttled by
    `ContestAlertCooldownSeconds`).
  - On entering Paused for the first time in a while: optional softer chat note
    `[PR] Your Walker is stalled — get in the zone!` to the owning team only.
- **GameOver toast:** `"<Team> Wins!"` + description with final percentages.
- Keep per-state spam in **chat**, reserve **HUD toasts** for contest/win — same split
  TrooperInvasion uses (round events = HUD, per-wave = chat).
- The HUD match clock can be left to the engine (TrooperInvasion proved suppressing
  `gameover_msg`/`round_end` alone keeps the match live without manually pinning the
  clock). If we want the clock to show remaining time vs `MatchTimeLimitSeconds`, adopt
  Deathmatch's 5-field clock-anchor write; otherwise leave it vanilla.

## Chat commands

Use the v0.4.5 `[Command]` attribute (TrooperInvasion/LockTimer style), not the legacy
`[ChatCommand]`.

| Command | Effect |
|---|---|
| `!walker` | Print both teams' current Walker progress %, state, and approx distance to enemy base, to the caller. |
| `!stuck` / `!suicide` | Kill self to respawn. **Provide via the shared `StuckCommand` plugin** in the composition (don't reimplement) — TrooperInvasion does exactly this. |
| `!help` | List PayloadRace commands. |

`!walker` reads the per-team progress structs; no mutation. Mirror Deathmatch's
`Chat.PrintToChat(caller.Slot, …)` for caller-only output.

## Plugin composition (`gamemodes.json`)

Add a new entry. Start from TrooperInvasion's PvP-adjacent set but **without**
`TeamChangeBlock` (we need two real teams) and **without** `HeroSelect` forcing (players
pick freely):

```jsonc
"payload-race": [
  "StatusPoker",
  "PayloadRace",
  "Hostname",
  "FlexSlotUnlock",      // optional — only if we want full builds
  "HealOnSpawn",
  "DisconnectCleanup",
  "Feedback",
  "StuckCommand"
]
```

Notes:
- **No `TeamChangeBlock`** — PayloadRace is symmetric two-team; we want the engine's
  team assignment / balancer, or a light custom balance in `OnClientFullConnect`
  (Deathmatch has a rank-based balancer we could borrow later; v1 can lean on engine).
- `DisconnectCleanup` handles per-slot entity cleanup (the repo's shared pattern).
- Docker service on a new free port (TrooperInvasion = 27018; pick the next, e.g.
  **27019**), plus the two CI `paths-filter` stanzas
  (`.github/workflows/build-plugins.yml`, `docker-gamemodes.yml`), mirroring how
  TrooperInvasion was registered.
- `PayloadRace.csproj` follows the triple-mode reference pattern
  (DeadlockDir / ProjectReference / Docker fallback) used by TrooperInvasion/StatusPoker.

## Suggested file layout (mirrors TrooperInvasion's split)

- `PayloadRace.cs` — config, fields, constants, lifecycle (`OnLoad`/`OnStartupServer`/`OnUnload`).
- `PayloadRace.Walkers.cs` — Walker identification, movement control, lane path.
- `PayloadRace.Zone.cs` — advance-zone poll, state machine evaluation, progress tracking.
- `PayloadRace.EndMode.cs` — win/death/timeout resolution, gameover suppression, changelevel reset.
- `PayloadRace.Players.cs` — join/leave, warmup arming, session bookkeeping.
- `PayloadRace.Commands.cs` — `!walker`, `!help`.

## Open questions

All design decisions from the original Q1–Q9 are resolved:

- **Q1/Q2 — Walker AI control:** spike (1) schema freeze flag → (2) speed field → (3)
  teleport-stepping in that order on a live server. See
  [Controlling Walker movement](#controlling-walker-movement) for the full decision tree.
- **Q3 — Natural movement:** assumed stationary on `-insecure` (same dependency gap as
  Guardians — objective-init never runs). All forward motion is plugin-driven. Confirm
  on first live run.
- **Q4 — Progress metric:** straight-line dot-product projection, closed.
- **Q5 — What stops a Walker naturally:** irrelevant; we assume stationary and drive all
  motion ourselves.
- **Q6 — Walker respawn:** out of scope v1; instant loss on Walker death.
- **Q7 — Walker death UX:** instant loss; `WalkerHealth = 5× vanilla` to prevent trivial
  ganks.
- **Q8 — Single vs. three-lane:** single-lane v1, Yellow lane (lane 1).
- **Q9 — Win condition:** proximity arrival (`progress01 >= 1.0`), no Patron destruction
  coupling.

**One remaining open item — Walker AI control mechanism (Q1/Q2):** the implementer must
spike the three options on a live server before wiring up the advance/pause state
machine. Everything else is decided and does not block parallel work on zone polling,
progress tracking, warmup, or GameOver.
