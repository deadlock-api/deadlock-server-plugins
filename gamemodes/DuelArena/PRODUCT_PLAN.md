# DuelArena — Product Plan

> Status: design draft. No C# written yet. References Deadworks managed API
> calls where known; flags unknowns as open questions at the bottom.

## Overview

DuelArena is a server-side gamemode for Deadlock (`dl_midtown`) that runs
several simultaneous 1v1 duels across distinct zones of the map. Players are
paired into best-of-3 duels; the winner of a match "promotes" to a
higher-ranked arena and the loser "demotes", producing an informal ELO-like
ladder without any persistent rating system — your rank is just your live win
count this session. Each round opens with a randomly chosen **twist modifier**
(e.g. +50% cooldown reduction, melee-only, random hero per respawn) that
applies to every active arena for that round, keeping the format fresh. There
is no economy: every duel uses a fixed loadout, respawns happen only between
rounds, and death immediately ends the current round.

This plan follows the structural patterns already proven in
[`Deathmatch/Deathmatch.cs`](../Deathmatch/Deathmatch.cs): strip map NPCs at
startup, pin the HUD match clock, suppress `round_end`/`gameover_msg`,
teleport-on-spawn, grant a spawn ritual (max signature abilities + spawn
protection), and announce state via `CCitadelUserMsg_HudGameAnnouncement`.

---

## Game loop

```
                ┌──────────────────────────────────────────────┐
                │                  WAITING                      │
                │  (< 2 players, or between sessions)           │
                └───────────────────┬──────────────────────────┘
                                    │  ≥ 2 players connected
                                    ▼
        ┌────────────────────  MATCH ACTIVE  ───────────────────┐
        │                                                       │
        │   Matchmaking: pair players → assign each pair to     │
        │   an arena (ranked slots 1..N)                        │
        │                                                       │
        │   For each arena, run a best-of-3:                    │
        │      ┌──────────────────────────────────────────┐    │
        │      │  ROUND START                              │    │
        │      │   - teleport both duelists into arena     │    │
        │      │   - grant fixed loadout + spawn ritual    │    │
        │      │   - roll & announce twist modifier        │    │
        │      │   - brief countdown, lift spawn protect   │    │
        │      ├──────────────────────────────────────────┤    │
        │      │  ROUND ACTIVE                             │    │
        │      │   - players fight; no respawn on death    │    │
        │      ├──────────────────────────────────────────┤    │
        │      │  ROUND END (on first death)               │    │
        │      │   - award round to survivor               │    │
        │      │   - announce score (e.g. "1-0")           │    │
        │      │   - pause, then loop to next round        │    │
        │      └──────────────────────────────────────────┘    │
        │   …repeat until one duelist reaches 2 round wins      │
        │                                                       │
        └───────────────────────┬───────────────────────────────┘
                                │ arena match decided
                                ▼
                ┌──────────────────────────────────────────────┐
                │             ROTATING (per arena)              │
                │  announce match winner; promote/demote;       │
                │  re-pair immediately → back to SESSION ACTIVE │
                └──────────────────────────────────────────────┘
```

Arenas rotate **independently**: when an arena's best-of-3 is decided, that
pair immediately re-pairs and starts a new match without waiting for other
arenas. A global tick drives round transitions per arena, mirroring
Deathmatch's `Timer.Every(1.Ticks(), TickMatchClock)` + elapsed-time rotation
approach (`Deathmatch.cs:234-269`).

The global state machine is:
- **Waiting** — fewer than 2 connected players.
- **Session Active** — at least one arena is running a live duel.
- Back to **Waiting** when the last arena goes idle (< 2 players remain).

---

## Configuration

`DuelArenaConfig` (a `[PluginConfig]` class, hot-reloadable via
`OnConfigReloaded`, following [[plugin-config]]):

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `RoundsToWin` | int | 2 | Round wins needed to take a match (2 ⇒ best-of-3). |
| `MaxPlayers` | int | 12 | Hard cap on duelists. 12 ⇒ up to 6 pairs / arenas. |
| `ArenaCount` | int | 3 | Number of simultaneous arenas (one per usable lane section). |
| `RoundTimeLimitSeconds` | float | 90 | If no death, round ends in a draw / sudden-death (see open Qs). |
| `RoundStartCountdownSeconds` | float | 4 | Spawn-protection + "fight!" countdown before damage is live. |
| `RoundEndPauseSeconds` | float | 5 | Pause after a death before next round teleport. |
| `MatchEndPauseSeconds` | float | 8 | Pause after a match before rotation. |
| `TwistEnabled` | bool | true | Master switch for twist modifiers. |
| `TwistProbability` | float | 0.75 | Per-round chance a twist is rolled at all (else "vanilla" round). |
| `TwistWeights` | map<string,float> | all 1.0 | Per-modifier relative weight for the weighted pick. |
| `FixedLoadoutHeroPool` | list<Heroes> | empty ⇒ all `AvailableInGame` | Heroes selectable for duels. |
| `AllowHeroChoice` | bool | false | If true, players keep their chosen hero between matches; if false, random per match. |
| `ArenaBoundaryRadius` | float | 1800 | Distance from arena center beyond which a duelist is teleported back to their spawn point. |

Probability model: each round, with probability `TwistProbability` we draw one
modifier from the enabled set using `TwistWeights` (weighted random, same idea
as Deathmatch's "least-present hero" weighted pick at
`Deathmatch.cs:426-434`). Otherwise the round is vanilla.

---

## State machine

### Global state

```
Waiting ──(≥2 players)──► Session Active ──(< 2 players remain)──► Waiting
```

- **Waiting** — fewer than 2 connected duelists. Connected players sit in a
  holding area (arena 1 center or a spectator perch) until a second arrives.
- **Session Active** — at least one arena has a live duel. Arenas rotate
  independently: when a match is decided, that arena re-pairs and starts a new
  match immediately.

### Per-arena state

```
Idle ──(pair assigned)──► RoundActive ──(death / timeout)──► RoundEnd
  ▲                            ▲                                 │
  │                            └──────(next round, score < win)──┘
  └──────(match decided: a duelist hit RoundsToWin)─────────────┘
```

- **Idle** — no pair assigned (bye arena, or fewer pairs than arenas).
- **RoundActive** — two duelists alive and fighting.
- **RoundEnd** — a death (or timeout) fired; score updated; short pause; then
  either loop to a new RoundActive or, if `RoundsToWin` reached, fall to Idle
  and report the match result to the global machine.

State lives in a small `Arena` record per slot: `int RankSlot`, two duelist
entity indices, `int[] roundWins`, `ArenaState state`, current twist, round
start time.

---

## Arena layout

`dl_midtown` is the only map. We reuse Deathmatch's landmark trick: the four
**Walker** towers (`npc_boss_tier2`, one per team per lane) are fixed, readable
positions captured at `OnStartupServer` and `OnEntitySpawned`
(`Deathmatch.cs:113-124, 362-373`). Lane color IDs are `1` Yellow, `3` Green,
`4` Blue, `6` Purple (see [[deadlock-game]]).

**Suggested 3 arenas — one per usable lane section** (skip Blue=4, as
Deathmatch does in its `_laneCycle = {1,3,6}`):

| Arena | Lane | Anchor derivation | Notes |
|-------|------|-------------------|-------|
| Arena A (rank 1, "top") | Yellow (1) | midpoint between Amber & Sapphire Yellow Walkers | Highest-rank duel. |
| Arena B (rank 2, "mid") | Green (3) | midpoint between Amber & Sapphire Green Walkers | |
| Arena C (rank 3, "bottom") | Purple (6) | midpoint between Amber & Sapphire Purple Walkers | Lowest-rank duel. |

Each arena's **center** = midpoint of that lane's two opposing Walker
positions; the two **spawn points** for the duel are offset from center along
the lane axis (toward each Walker) by a fixed radius (e.g. ±900 units) so
duelists start facing each other. This derives coordinates from live entity
data rather than hardcoding map coordinates — robust to map updates, same
philosophy as Deathmatch's `PickSpawnPoint`.

> Coordinate ranges are intentionally **not hardcoded**. They are computed at
> startup from captured Walker positions. If a lane's Walkers aren't captured
> (schema race), fall back to the bearing-sorted bucketing Deathmatch already
> implements (`Deathmatch.cs:298-331`).

Arena **boundary**: when a duelist strays more than `ArenaBoundaryRadius` from
the arena center, teleport them back to their spawn point. Checked once per tick
per alive duelist (simple per-tick distance check + `Teleport`).

If `ArenaCount` > 3, additional arenas would need sub-sections of the same
lanes or the Blue lane; flagged as future work.

---

## Matchmaking

**Initial pairing (session start / first MatchActive):**

- Collect all connected duelists (team-assigned, not spectators).
- Pair them. Two viable strategies:
  - **Random** — shuffle, pair adjacent. Simplest; good when no rank data.
  - **Rank-seeded** — if we want a warm start, seed by Deadlock rank-predict
    (the API Deathmatch already queries:
    `https://api.deadlock-api.com/v1/players/{accountId}/rank-predict`,
    `Deathmatch.cs:895-926`) and pair nearest-rank. Optional; default to random
    for v1 since intra-session win-count rank takes over after match 1.
- Assign pairs to arenas. For the first match, arena assignment order is
  arbitrary (random) since no win history exists yet.

**Odd player out / byes:** with an odd number of duelists, exactly one player
has no pair. They stay at their arena position and are first in line to be
paired next rotation. No `MakeObserver` call. Track a `_byeQueue` so the same
player isn't repeatedly benched.

**Rotation re-pairing (after MatchEnd):** see Rank system + Rotation below.

---

## Rank system

Non-persistent, session-scoped. Each duelist has an `int Wins` counter (matches
won, not rounds). On `MatchEnd`:

- Match winner: `Wins++`.
- Players are conceptually sorted by `Wins` descending (ties broken by current
  arena rank, then by round-win differential, then arbitrarily).
- Arena rank slots (1 = top) are filled from this sorted order: highest win
  counts occupy the top arena.

This is a "winner stays / climbs" ladder: you don't carry a number players see
as a rating; your rank is your position in the sorted standings. `!rank`
surfaces it (below). The counter resets when the session empties (mirror
Deathmatch's `_dmRoundNum`/session reset on last disconnect,
`Deathmatch.cs:1104-1113`).

---

## Round start

For each arena entering RoundActive:

1. **Teleport both duelists** to their two spawn points (`pawn.Teleport(position: spawn)`,
   as Deathmatch does on respawn, `Deathmatch.cs:554`). Face them toward arena
   center (set eye angles / `Teleport` with angle if supported — open question
   on angle param).
2. **Grant fixed loadout** — apply the spawn ritual: max signature abilities
   (`MaxUpgradeSignatureAbilities`, `Deathmatch.cs:784-792`), full heal on
   spawn (HealOnSpawn plugin already in the stack does this; see composition),
   and any fixed items via `pawn.AddItem(name, enhanced: false)`
   ([[plugin-api-surface]]). No gold economy: we do **not** grant currency
   (Deathmatch grants 999999 gold for free buying; DuelArena deliberately does
   not, to keep loadouts fixed). Flex slots can be force-unlocked via the
   FlexSlotUnlock plugin if the fixed loadout needs flex items.
3. **Roll & announce the twist modifier** (see next section) — single
   `CCitadelUserMsg_HudGameAnnouncement` to both duelists (and spectators of
   that arena).
4. **Spawn protection + countdown** — set `Invulnerable` + `BulletInvulnerable`
   modifier states (`GrantSpawnProtection`, `Deathmatch.cs:649-672`) for
   `RoundStartCountdownSeconds`, then lift. Damage only counts after the
   countdown. This prevents instant trades on teleport-in.

---

## Twist modifiers

One twist is active per round, applied to **all** arenas uniformly (simpler to
announce and reason about than per-arena twists). Each modifier is a small
apply/revert pair invoked at round start / round end. Listed with exact
mechanic and the API it leans on:

| # | Name | Mechanic | API / approach |
|---|------|----------|----------------|
| 1 | **Quickdraw (+50% CDR)** | Halve ability cooldowns | Reuse Deathmatch's `ScaleAbilityCooldowns` shift-the-window technique (`Deathmatch.cs:803-836`); run a per-tick timer only while this twist is active. |
| 2 | **Infinite Stamina** | Stamina never depletes | Per-tick `pawn.SetStamina(max)` ([[plugin-api-surface]] v0.4.6), or `EModifierState.UnlimitedAirJumps`/`UnlimitedAirDashes`. |
| 3 | **Hero Roulette** | Each duelist gets a random hero at round start; rerolled every round | `pawn.SwapOrReset(Heroes.X, onReady)` (v0.4.7, [[plugin-api-surface]]). Must `Precache.AddHero` for any hero in the pool in `OnPrecacheResources`. See revert note below. |

`TwistWeights` in config has entries for these three twists only. Additional
twists (Melee Only, Glass Cannon, Low Gravity, Big Head/Tiny, Sudden Death
Speed) are deferred to v2 pending API verification.[^twists-v2]

[^twists-v2]: v2 candidates require confirming: `OnAbilityAttempt` gun-fire
isolation (Melee Only); damage-amp vs. max-HP edits (Glass Cannon); `sv_gravity`
applicability and movement-speed modifier name (Low Gravity, Sudden Death Speed);
`SetScale` hitbox behaviour (Big Head/Tiny).

Revert discipline: every twist must cleanly revert at round end (cooldown timer
cancelled, hero reset if Roulette, etc.) so the next round starts clean. Track
the active twist in arena/global state and revert before rolling the next.

**Hero Roulette revert:** if a pawn dies before the `onReady` callback fires
(during `SwapOrReset`), `OnceHeroInitialized` is auto-cleaned per v0.4.7 notes —
no manual guard needed. On RoundEnd, if a Roulette swap is in-flight, cancel it
by not applying the `onReady` ritual: guard with a round-id check inside the
callback so a stale callback from a previous round silently no-ops.

---

## Round end

- **Detection:** `[GameEventHandler("player_death")]` (like Deathmatch's
  `OnPlayerDeath`, `Deathmatch.cs:498-540`). When a duelist in a RoundActive
  arena dies, that arena's round is over and the **other** duelist wins the
  round. Identify the arena by the victim's entity index → arena lookup.
- **Timeout:** if `RoundTimeLimitSeconds` elapses with both alive, the duelist
  with the lower HP fraction (current HP / max HP) loses. If equal within a
  small epsilon → draw; replay that round (same rule as ArenaFights
  simultaneous-wipe: same round number, new round start ritual).
- **Disconnect:** if a duelist disconnects during RoundActive, their opponent
  wins the round (and match) immediately; that arena re-pairs with remaining
  players.
- On round resolution:
  - Increment winner's `roundWins` for that arena.
  - Announce score with `CCitadelUserMsg_HudGameAnnouncement`
    (`TitleLocstring` = "Round won!", `DescriptionLocstring` = "Alice 1 – 0 Bob").
  - Suppress the engine's native respawn for the dead player during the pause
    (see No-respawn section).
  - Wait `RoundEndPauseSeconds`, then either start the next round (teleport +
    ritual + new twist) or, if `roundWins` hit `RoundsToWin`, transition the
    arena to match-decided.

We must suppress `round_end` and `gameover_msg` game events exactly as
Deathmatch does (`Deathmatch.cs:271-283`) so a single player death never trips
the engine's real round/game-over flow.

---

## Match end

- When an arena's duelist reaches `RoundsToWin` round wins, that arena's match
  is decided. Announce the match winner for that arena.
- When **all** arenas are decided (and any timed-out arenas resolved), the
  global machine moves to MatchEnd: a summary announcement (per-arena winners),
  then `MatchEndPauseSeconds`, then Rotating.
- Bump per-match win counters here (`Wins++` for each arena's match winner) so
  the rank sort in Rotating sees fresh numbers.

---

## Rotation

After MatchEnd, recompute ranks and re-pair:

1. **Promote / demote by arena:** each arena's match winner moves up one arena
   rank (toward rank 1); the loser moves down one (toward rank N). Rank-1 winner
   stays at rank 1 (can't promote past the top); rank-N loser stays at rank N.
   This is the local "king of the hill" movement.
2. **Re-pair within adjacent ranks:** the canonical scheme is "winner of arena
   k meets loser of arena k−1" so winners climb into tougher opponents and
   losers fall to easier ones — a natural ladder. Concretely, after collecting
   {winners, losers}, sort everyone by `Wins` (Rank system) and pair the sorted
   list adjacently (1st vs 2nd, 3rd vs 4th, …), assigning pairs to arenas top-down.
3. **Byes:** if player count is odd, the lowest-ranked unpaired player waits one
   rotation (tracked in `_byeQueue`; prioritized next time). A bye does not
   change their `Wins`.
4. New pairs → arenas → re-enter MatchActive.

Edge cases:
- A duelist disconnects mid-match → their opponent wins by default; rotation
  proceeds with remaining players (re-pair may produce a new bye).
- Exactly 2 players → single arena (arena 1), repeated best-of-3s, no real
  promotion (rank is just cumulative wins). `!score` still tracks the series.

---

## No-respawn within a round

Deadlock auto-respawns dead players after a respawn timer. We must keep the
loser dead until the round-end pause completes (or move them to spectator).
Options, best-first:

1. **`CCitadelPlayerPawn.RespawnTime`** (v0.4.8, [[plugin-api-surface]]) —
   read/write `m_flRespawnTime`. Push it far into the future on death so the
   engine doesn't auto-respawn; then on next-round start we teleport rather than
   rely on engine respawn. **Preferred** if writing it actually suspends respawn.
2. **`controller.MakeObserver()`** (v0.4.7 observer API, [[observer-api]]) — on
   death, convert the loser to a spectator pawn so there's no hero pawn to
   respawn. They watch the (already-over) arena / their next match. On
   next-round start, `SelectHero` / re-spawn them into the arena. Heavier but
   unambiguous — there's no pawn to auto-respawn.
3. **Re-kill loop** — if a respawn slips through, intercept `player_respawned`
   and immediately re-suppress. Fragile; fallback only.

Note Deathmatch instead *embraces* fast respawn
(`citadel_player_spawn_time_max_respawn_time 2`, `Deathmatch.cs:110`); DuelArena
wants the opposite, so this is genuinely new behavior to validate. Flagged in
Open Questions.

---

## Chat commands

Using the `[Command]` attribute ([[command-attribute]]), same pattern as
Deathmatch's `CmdHelp`/`CmdStuck` (`Deathmatch.cs:845-870`):

| Command | Effect |
|---------|--------|
| `!rank` | Print caller's current standing: `Wins`, arena rank slot, "next up" status. |
| `!score` | Print the caller's current arena best-of-3 score (e.g. "You 1 – 0 Bob, round 2"). |
| `!stuck` / `!suicide` | Concedes the current round (counts as a death → round loss). DuelArena owns this command: clear spawn-protection bits, then `pawn.Hurt(999999)`. |
| `!help` | List commands. |

---

## HUD / feedback

All via `CCitadelUserMsg_HudGameAnnouncement` (`TitleLocstring` +
`DescriptionLocstring`) sent with `RecipientFilter.All` or a per-arena
recipient filter where possible ([[netmessages-api]]):

- **Round start / twist announcement:** Title `"Round N"`, Description
  `"Twist: Quickdraw (+50% CDR)"` (or `"No twist this round"`). Sent to both
  duelists (+ that arena's spectators).
- **Kill / round-win announcement:** Title `"Round won!"`, Description
  `"Alice eliminated Bob — Alice 1 – 0"`.
- **Match score:** running series shown on each round-start and round-end
  announcement (`1-0`, `2-0`, etc.).
- **Match end:** Title `"Match: Alice defeats Bob 2–1"`.
- **Rotation:** Title `"Rotation"`, Description `"Alice promoted to Top Arena;
  Bob to Mid"` — mirrors Deathmatch's rotation HUD (`Deathmatch.cs:603-608`).
- **Chat fallbacks** via `Chat.PrintToChat` / `Chat.PrintToChatAll` for
  per-player detail (`!rank`, `!score`).

Spectator-of-your-match feedback (so a benched/bye player sees the live duel)
depends on the observer-targeting approach in Open Questions.

---

## Plugin composition (`gamemodes.json`)

Add a `duel-arena` entry alongside the existing modes. Following the
`deathmatch` composition (which bundles map cleanup, flex unlock, team-change
blocking, heal-on-spawn, disconnect cleanup, hero-select, feedback):

```json
"duel-arena": [
  "StatusPoker",
  "DuelArena",
  "Hostname",
  "FlexSlotUnlock",
  "TeamChangeBlock",
  "HealOnSpawn",
  "DisconnectCleanup",
  "HeroSelectOnNextSpawn",
  "Feedback"
]
```

Rationale per dependency:
- **StatusPoker / Hostname / Feedback** — standard infra (server browser,
  hostname, feedback channel), present in every mode.
- **FlexSlotUnlock** — if the fixed loadout uses flex items; harmless otherwise.
- **TeamChangeBlock** — duelists are placed on Amber (2) / Sapphire (3) by the
  plugin; block players from re-picking teams (note: server-side `ChangeTeam`
  bypasses the concommand path anyway, see [[deadlock-game]]).
- **HealOnSpawn** — full heal at round start without DuelArena doing it itself.
- **DisconnectCleanup** — clears pawns/controllers of leavers so rotation logic
  doesn't trip over ghost slots.
- **HeroSelectOnNextSpawn / HeroSelect** — only if `AllowHeroChoice`; otherwise
  DuelArena assigns heroes and this can be dropped.
- **`!stuck`** is owned by DuelArena itself (see Chat commands).

---

## Open questions

1. **Suppressing auto-respawn within a round.** Which mechanism actually holds a
   player dead: writing `CCitadelPlayerPawn.RespawnTime` far into the future, or
   `MakeObserver()` on death + re-spawn on next round? Needs live testing — no
   existing plugin suppresses respawn (Deathmatch does the opposite). This is the
   single biggest unknown.
2. **Per-arena recipient filters.** Can `CCitadelUserMsg_HudGameAnnouncement`
   target just the two duelists + their spectators, or only `RecipientFilter.All`?
   Affects whether twist/score announcements spam everyone or stay arena-scoped.
3. **`OnPawnHeroInitialized` double-fire guard for Hero Roulette.** Confirm the
   round-id guard pattern (check round id inside the `onReady` callback and
   no-op if stale) is sufficient to prevent a late-firing callback from a
   previous round from applying its swap to the new round.

**Resolved:** arena rotation is independent (not lockstep); `!duel [player]`
dropped from v1; round timeout resolves by lowest HP fraction (draw → replay);
v1 twist set is Quickdraw, Infinite Stamina, Hero Roulette only (others deferred
to v2); arena boundary is a leash teleport at `ArenaBoundaryRadius`; bye player
stays in place (no `MakeObserver`); `!stuck` is owned by DuelArena; mid-round
disconnect awards the round and match to the opponent immediately.
