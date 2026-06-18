# GunGame (Hero Rotation) — Product Plan

> Status: planning. No code yet. This document is implementation-ready but
> deliberately flags every unverified engine/API assumption as an open question
> at the bottom. Read those before writing the plugin.

## Overview

GunGame is a free-for-all gamemode where every player races through a fixed
rotation of ~12 heroes. Everyone starts on the same hero. Each time a player
scores a configured number of kills (default 2), they "level up" and the
plugin swaps them to the next hero in the rotation — so progression is measured
in *heroes climbed*, not gold or items. The first player to clear the entire
rotation and then land a **melee kill** on the final level wins the match. A
Deadlock-flavored twist on CS2 Arms Race: instead of cycling weapons we cycle
whole hero kits, and being killed by the current leader demotes the killer's
victim... no — demotes the *leader* one level (the "knife demotion" mechanic,
adapted below). Soul/economy gain is suppressed throughout so the only axis of
progress is the hero ladder.

This plugin owns the full match lifecycle on `dl_midtown` the same way
[`Deathmatch`](../Deathmatch/Deathmatch.cs) does: it strips map NPCs/objectives,
pins the HUD clock, suppresses `gameover_msg`/`round_end`, and drives its own
state machine.

## Game loop

Three top-level phases, tracked by a single `GunGamePhase` enum on the plugin:

```
Warmup  ──(enough players OR warmup timer expires)──▶  Active  ──(someone wins)──▶  GameOver
   ▲                                                                                    │
   └──────────────────────────(post-game cooldown timer)───────────────────────────────┘
```

### Warmup
- Entered on `OnStartupServer` and after each `GameOver` cooldown.
- Players can join, spawn, and shoot, but **kills do not count** toward levels
  (so an early-bird doesn't run away with the game before opponents arrive).
- Everyone is forced onto the level-0 starting hero.
- Ends when `_humanCount >= Config.MinPlayersToStart` **or** the
  `Config.WarmupSeconds` timer expires with ≥1 player present. (If it expires
  with 0 players, stay in Warmup.)
- Transition broadcasts a `HudGameAnnouncement` "Gun Game — GO!".

### Active
- Kills count. The per-player state machine (below) advances levels.
- Instant respawn on death, keeping the player's current level/hero.
- Periodic leaderboard ticks (top 3) via a `Timer.Every`.
- Continues until a player satisfies the win condition.

### GameOver
- Triggered by the win condition. Broadcast winner, freeze scoring, optionally
  freeze players (open question — see below).
- Hold for `Config.PostGameSeconds`, then reset all per-player state back to
  level 0, re-pin the clock, and return to Warmup.
- Mirror Deathmatch's defensive `gameover_msg`/`round_end` suppression so the
  engine's own end-screen never sticks; GunGame decides when the match ends,
  not the engine.

## State machine (per player)

Keyed by controller `EntityIndex` (stable for the session; cleared in
`OnClientDisconnect`), matching the Deathmatch `_killsThisRound` pattern.

```
class PlayerProgress
{
    int Level;            // 0 .. RotationLength (== 12 means "on final melee level 13")
    int KillsThisLevel;   // 0 .. Config.KillsPerLevel-1, resets on level change
    bool Finished;        // set true the instant they win; guards double-win
}
```

- `Level` indexes `Config.HeroRotation`. `Level == RotationLength` is the
  special **final melee level** (conceptually "level 13" for a 12-hero
  rotation) — the player is on the *last* hero of the rotation and must now get
  a melee kill. The final level always reuses the last entry in `HeroRotation`
  (`MeleeHero` defaults to `null` and is ignored); no separate "knife hero" is
  needed.
- A player's displayed "rank" in chat is `Level + 1` of `RotationLength + 1`.

### Advancement rule
On a counted kill by player P:
1. `KillsThisLevel++`.
2. If `KillsThisLevel >= Config.KillsPerLevel`:
   - `KillsThisLevel = 0`, `Level++`.
   - If `Level < RotationLength`: swap P to `HeroRotation[Level]` (see Hero
     swapping). Announce advancement.
   - If `Level == RotationLength`: P has entered the final melee level. Swap to
     the last hero in `HeroRotation`. Block primary fire and abilities via
     `AbilityAttemptEvent` (see "Final round"). Announce "Final level — get a
     melee kill to win!".

### Demotion rule
See "Demotion mechanic" below — it decrements `Level` and re-swaps the hero.

## Configuration

`GunGameConfig` via `[PluginConfig]` (JSONC auto-created, hot-reloadable like
[`plugin-config`]). Proposed fields and defaults:

| Field | Default | Meaning |
|-------|---------|---------|
| `HeroRotation` | 12-hero list below (codenames) | Ordered ladder. Level *i* uses `HeroRotation[i]`. |
| `KillsPerLevel` | `2` | Counted kills needed to advance one level. |
| `DemotionEnabled` | `true` | Whether killing the current leader demotes them. |
| `MinPlayersToStart` | `2` | Warmup→Active threshold. |
| `WarmupSeconds` | `30` | Max warmup wait. |
| `PostGameSeconds` | `15` | GameOver hold before reset. |
| `LeaderboardIntervalSeconds` | `20` | Top-3 chat cadence. |
| `FinalLevelRequiresMelee` | `true` | If false, the final level is won by any kill. |
| `MeleeHero` | `null` | Unused by default — final level always reuses the last entry in `HeroRotation`. Field reserved for future override; set to `null`. |
| `SuppressEconomy` | `true` | Zero out soul/gold gain. |

### Default 12-hero rotation (codenames — see [[deadlock-game]] Heroes note)

The `Heroes` enum uses **codenames**, not display names. Proposed default,
ordered loosely from straightforward gunplay → ability-heavy → melee-friendly
finish. Every entry must pass `AvailableInGame` at load time (filter + log +
drop any that fail, exactly like Deathmatch does when picking heroes):

| Level | Codename | Display name |
|------:|----------|--------------|
| 0  | `Inferno`  | Infernus |
| 1  | `Gigawatt` | Seven |
| 2  | `Hornet`   | Vindicta |
| 3  | `Ghost`    | Lady Geist |
| 4  | `Atlas`    | Abrams |
| 5  | `Wraith`   | Wraith |
| 6  | `Orion`    | Grey Talon |
| 7  | `Forge`    | McGinnis |
| 8  | `Chrono`   | Paradox |
| 9  | `Kelvin`   | Kelvin |
| 10 | `Haze`     | Haze |
| 11 | `Krill`    | Mo & Krill (melee-leaning finisher) |

> The exact codename→hero mapping must be verified against the live `Heroes`
> enum at implementation time; only `Inferno`, `Hornet`, `Orion`, `Krill` are
> confirmed in the wiki. Treat the rest as placeholders. Whatever the final
> list, validate each through `GetHeroData()?.AvailableInGame == true` and log
> the resolved rotation at load, dropping unavailable heroes so a bad config
> can't brick the mode.

## Hero swapping

Use the v0.4.7 hero-swap helpers (see [[deadworks-0.4.7-release]]):

- All hero swaps (level-up and demotion) are deferred to `player_respawned` —
  never called on a dead pawn. The `onReady` callback captured inside
  `SwapOrReset` is guarded with the player's level and round-id at call time;
  if the player has since died and respawned again before the callback fires,
  discard it.
- Swap on level change (called from `player_respawned`):
  ```
  pawn.SwapOrReset(nextHero, onReady: () => OnHeroReady(pawn, level, roundId));
  ```
  `SwapOrReset` internally calls `SelectHero` (different hero) or `ResetHero`
  (same hero), and `onReady` fires once abilities/modifiers are repopulated —
  replacing the old `Timer.Once(1s)` guesswork.
- In `OnHeroReady`, apply the per-spawn ritual: zero currency, grant brief spawn
  protection. Do **not** call `MaxUpgradeSignatureAbilities` on level-change
  swaps — stock hero kits only, so each hero feels authentic to its progression.
  (If maxing abilities on the very first spawn / level-0 assignment is desired
  for consistency with other modes, that is the only exception.)
- **Double-fire guard:** `OnPawnHeroInitialized` fires for *every* init path
  (initial spawn, `SelectHero`, `ResetHero`, `resethero` cmd). If we both
  subscribe to `OnPawnHeroInitialized` and use `OnceHeroInitialized` callbacks,
  we will double-run setup. Decision: prefer the per-swap `onReady`/
  `OnceHeroInitialized` continuation and do **not** also handle
  `OnPawnHeroInitialized` for level-up setup. Use `OnClientFullConnect` only
  for the initial level-0 assignment.

### Precache
Per [[deadworks-0.4.6-release]], the host **no longer auto-precaches** every
`AvailableInGame` hero. Because GunGame swaps players into many heroes at
runtime, the implementer **must** override `OnPrecacheResources` and call
`Precache.AddHero(...)` for every hero in `HeroRotation`. This is a hard
requirement, not optional — missing a precache causes a hitch or failure on the
first swap to that hero. (`MeleeHero` is always `null`/the last rotation entry,
so no separate precache call is needed for it.)

## Economy

- Override `OnModifyCurrency` and return `HookResult.Stop` as the primary
  suppress for soul/gold gains when `Config.SuppressEconomy` is true, so kills
  don't build an economy. (Confirm the event exposes amount/type as listed in
  [[events-surface]].)
- On each spawn/hero-ready, hard-set currency to `0` as belt-and-suspenders —
  ensures no residual balance survives across swaps. Deathmatch sets `999_999`
  for its "buy anything" feel; GunGame wants the opposite.
- Do **not** enable `citadel_allow_purchasing_anywhere`. Leave default item
  restrictions in place. Implementer should verify `HookResult.Stop` on
  `OnModifyCurrency` has no side effects on health/leveling before shipping.

## Demotion mechanic

When `DemotionEnabled` and a player V is killed by attacker A in Active phase:
- Identify the **current leader(s)** = player(s) with the highest `Level`.
- **If the victim V is the (or a) leader, V drops one level on any death** —
  kill method does not matter. `V.Level = max(0, V.Level - 1)`, reset
  `V.KillsThisLevel = 0`, and swap V to `HeroRotation[V.Level]` on their
  respawn (not mid-life). Announce the demotion to all.
- This rewards focusing the front-runner and keeps the pack competitive.
- Guard: never demote below 0; never demote a `Finished` player (match is over
  by then anyway). Self-kills/suicides (`!stuck`, attacker == victim) do **not**
  demote.

## Final round (melee)

At `Level == RotationLength` the player is on the final hero and must score a
**melee kill** to win (when `FinalLevelRequiresMelee`).

**Enforcement: block inputs (option 2).** On reaching the final level, block
primary fire and all abilities via `AbilityAttemptEvent`:
- `BlockAllAbilities()`
- block `InputButton.Attack`
- block `InputButton.Attack2`

Any kill the player then lands is implicitly melee — no damage-type detection
needed. This sidesteps the unsolved "what marks a melee kill" problem entirely.

If playtesting reveals that melee kills are still impossible with abilities and
gun blocked (e.g. engine quirk), revisit and fall back to detecting melee via
`CTakeDamageInfo` flags. Implementer should spike `AbilityAttemptEvent`
button-blocking against the live build to confirm it applies to primary fire
before shipping the final level.

## Kill tracking

Hook the native game event, same as Deathmatch:

```
[GameEventHandler("player_death")]
public HookResult OnPlayerDeath(PlayerDeathEvent args) { ... }
```

A kill counts toward a level only if, mirroring Deathmatch's `scored` check:
- attacker controller and victim pawn both exist,
- attacker != victim (no suicide credit),
- both are human players.

FFA layout: all players on team 2 (Amber) with `mp_friendlyfire 1`, same
decision as FreeForAll. Kill gating relies on attacker ≠ victim, not on
differing teams. (ConVar name for friendly fire needs confirming — shared
blocker with FFA.)

From the event we read attacker/victim controllers and update `PlayerProgress`.
On the final level, no damage-type inspection is needed — the kill is
implicitly melee because inputs are blocked (see "Final round").

## Respawn

- Instant respawn, keeping the same level/hero.
- Cap respawn time low like Deathmatch:
  `citadel_player_spawn_time_max_respawn_time` ≈ `2` (the engine enforces a
  minimum floor regardless). Optionally force-respawn via the `respawn`
  concommand path if the floor is too high.
- On `player_respawned`: re-apply spawn protection and, if a demotion/level
  change was queued while dead, perform the `SwapOrReset` now. Hero changes are
  always deferred to respawn — never applied to a dead pawn.
- Spawn placement: GunGame is FFA; start with engine default spawns for v1, add
  Walker-based line-of-fire-aware selection later if needed.

## Win condition

First player to:
1. Climb every level (`Level` reaches the final melee level), **and**
2. Land a melee kill on that final level (or any kill if
   `FinalLevelRequiresMelee == false`).

On the winning kill: the first `OnPlayerDeath` handler to process the winning
kill sets `Finished = true` and transitions to GameOver. The `Finished` guard
prevents any double-win if two killing blows land on the same tick. Event
dispatch ordering is assumed stable — first-handler-wins is accepted. Broadcast
the winner via `HudGameAnnouncement` and start the post-game cooldown. Ignore
any further kills until reset.

## Leaderboard

A `Timer.Every(Config.LeaderboardIntervalSeconds.Seconds(), ...)` during Active:
- Sort players by `Level` desc, then `KillsThisLevel` desc.
- Print top 3 to all via `Chat.PrintToChatAll`:
  `"[GG] 1. <name> — <heroDisplayName> (Lv N/12)"` etc.
- Also surface on the periodic `HudGameAnnouncement` if a HUD slot is desired.

## Chat commands

Use the unified `[Command]` attribute (see [[command-attribute]]):

| Command | Behavior |
|---------|----------|
| `!level` | Whisper the caller their current level, hero, and kills-to-next. |
| `!top`   | Whisper the caller the current top-3 leaderboard on demand. |
| `!stuck` | Suicide-to-respawn. GunGame owns this — implement locally (same reasoning as Deathmatch: needs spawn-protection clear before the lethal hit). `StuckCommand` must **not** appear in the `gamemodes.json` composition. Guard in `OnPlayerDeath`: attacker == victim → skip demotion check and skip kill credit. |

## HUD / feedback

- **On advancement:** to the advancing player, `Chat.PrintToChat` +
  optional centered `HudGameAnnouncement` "Level N — <hero>!". To everyone, a
  short "<name> reached level N" when crossing notable thresholds (e.g. new
  overall leader).
- **On demotion:** broadcast "<name> was knocked down to level N!" to all, and
  whisper the demoted player.
- **On final level:** announce the player is "one melee kill from victory".
- **On win:** full-screen `HudGameAnnouncement` "<name> wins Gun Game!".
- Console `Console.WriteLine("[GG] ...")` logging throughout, matching the
  `[DM]`/`[TI]` house style for server-side debugging.

## Plugin composition (gamemodes.json)

Add a `gun-game` entry alongside the others. Suggested set, modeled on the
`deathmatch` line (FFA, self-driven lifecycle, no team-vs-team objectives):

```json
"gun-game": ["StatusPoker", "GunGame", "Hostname", "FlexSlotUnlock", "TeamChangeBlock", "HealOnSpawn", "DisconnectCleanup", "Feedback"]
```

Notes:
- `GunGame` itself owns scoring, hero swaps, respawn placement, economy
  suppression, and HUD.
- `StuckCommand` is intentionally **omitted** — GunGame implements `!stuck`
  locally (same reasoning as Deathmatch: needs spawn-protection clear before
  the lethal hit; the generic plugin would double-register and be absorbed).
- `TeamChangeBlock` keeps players from re-teaming out of the FFA layout.
- `HealOnSpawn` / `FlexSlotUnlock` mirror Deathmatch; revisit whether GunGame
  wants flex slots at all given there are no item builds (likely drop
  `FlexSlotUnlock`).

The `GunGame.csproj` should mirror `TrooperInvasion.csproj` /
`Deathmatch.csproj` (same triple-mode build, `DeadworksManaged.Api`
reference) — see [[plugin-build-pipeline]].

## Open questions

All nine original questions have been resolved (decisions applied above).
Remaining open items for implementer:

1. **Confirm friendly-fire ConVar name.** The exact ConVar to enable same-team
   damage in Deadlock (equivalent of `mp_friendlyfire 1`) is unconfirmed —
   shared blocker with the FreeForAll plugin. Must be verified against the live
   build before kill-tracking will work.
2. **Confirm `AbilityAttemptEvent` blocks `InputButton.Attack`.** The final-level
   enforcement (option 2) assumes `AbilityAttemptEvent` can block primary fire,
   not only abilities/items. Implementer should spike this against the live build.
   If primary fire cannot be blocked this way, fall back to detecting melee via
   `CTakeDamageInfo` flags on the lethal hit.
