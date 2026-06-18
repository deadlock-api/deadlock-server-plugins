# ArenaFights — Product Plan

> Status: design / implementation-ready plan. No C# written yet.
> Target framework: Deadworks managed plugin API (`DeadworksManaged.Api`), same
> as [[../Deathmatch/Deathmatch.cs]] and [[../TrooperInvasion]].

## Overview

ArenaFights is a round-based, small-team elimination gamemode for `dl_midtown`.
Two teams of fixed size (2v2 or 3v3) fight inside a confined slice of the map.
There are **no respawns within a round** — the last team with a living player
wins the round, and the first team to win the majority of a best-of-3 takes the
match. Every round starts from a clean slate: all players are teleported to
their team's spawn line, given an identical fixed item loadout (no gold/economy),
and a single **item-box** spawns at arena center carrying one powerful timed buff
(Mannpower-style). The item-box is the focal point: grabbing it tilts the fight,
so both teams contest the center at round start. The rest of the map is stripped
of walkers/troopers/towers and the match clock is frozen so the engine never
ends the game on us.

## Game loop

```
          ┌─────────────────────────────────────────────────────┐
          ▼                                                     │
  Lobby ─► RoundStart ─► RoundActive ─► RoundEnd ──(more rounds)─┘
   ▲                                        │
   │                                        └──(a team reached roundsToWin)──► MatchEnd ─► Lobby
   └──────────────────────────────────────────────────────────────────────────────────┘
```

1. **Lobby** — waiting for enough players to fill both teams. Idle: no rounds
   tick, item-box not spawned. Players who join are assigned to a team and held.
2. **RoundStart** — short freeze/reset window (`resetSeconds`). Teleport every
   player to their team spawn line, full-heal, grant the fixed loadout, apply
   spawn protection, spawn the center item-box. Countdown announced via HUD.
3. **RoundActive** — fight. Deaths are permanent for the round (respawn
   suppressed). Confinement enforced every tick. Item-box pickup grants a buff.
4. **RoundEnd** — triggered when one team has zero living players (or the round
   timer expires → tiebreak rule). Announce round winner, increment that team's
   round score, pause `roundEndPauseSeconds`.
5. **MatchEnd** — when a team's round wins ≥ `roundsToWin`. Announce match
   winner, pause `matchEndPauseSeconds`, then reset scores and return to Lobby
   (or immediately re-enter RoundStart if both teams still full).

State is driven by a per-tick `Timer.Every(1.Ticks(), …)` tick function plus
event handlers, mirroring Deathmatch's `TickMatchClock` pattern.

## Configuration

A `[PluginConfig] ArenaFightsConfig` class (see [[plugin-config]]), validated
in-place (clamp, don't throw). Proposed fields:

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `TeamSize` | `int` | `3` | 2 or 3. Clamped to `[2,3]`. Drives lobby-ready threshold (`2 * TeamSize`). |
| `RoundsToWin` | `int` | `2` | Best-of-3 ⇒ 2. Best-of-5 ⇒ 3. |
| `ResetSeconds` | `float` | `5` | RoundStart freeze/countdown. |
| `RoundEndPauseSeconds` | `float` | `5` | Pause after a round is decided. |
| `MatchEndPauseSeconds` | `float` | `12` | Pause after match decided. |
| `RoundTimeLimitSeconds` | `float` | `120` | Safety cap; expiry → tiebreak (see Round end). `0` disables. |
| `SpawnProtectionSeconds` | `float` | `2` | Reuse Deathmatch's invuln-on-spawn pattern. |
| `Loadout` | `string[]` | `[]` | Internal item names granted each round start. Implementer resolves exact item names against the live `Items` enum. Empty = abilities only (maxed sigs). |
| `LoadoutEnhanced` | `bool` | `false` | Pass to `AddItem(name, enhanced)`. |
| `ItemBoxBuffDurationSeconds` | `float` | `20` | Buff lifetime once picked up. |
| `ItemBoxPickupRadius` | `float` | `120` | Proximity radius if touch-detection unavailable. |
| `ArenaBounds` | per-map AABB | (below) | min/max world corners; YAML-loadable like LockTimer. Survey in-game on first run. |
| `ArenaRadiusPerPlayer` | `float` | `800` | Base radius added per player in the match — zone grows with team size so 2v2 is tighter than 3v3. |
| `ConfinementDamageBase` | `float` | `20` | HP/s damage applied to a player the first second outside the zone. |
| `ConfinementDamageRampPerSecond` | `float` | `15` | Additional HP/s added for each additional second spent continuously outside. |

### Fixed loadout

Grant with `pawn.AddItem(name, enhanced: LoadoutEnhanced)` for each name in `Loadout`.
Signature abilities are always max-upgraded (`MaxUpgradeSignatureAbilities`) so fights
aren't gated on leveling regardless of loadout contents.

**Timing gotcha:** `AddItem` on a freshly-respawned pawn hits the same `GetMaxHealth = 0`
race that `HealOnSpawn` works around — use the same deferred-tick retry loop (up to ~20
ticks, re-resolving the pawn by `EntityIndex`) before granting items and healing.

Exact item names and the specific set to use are left to the implementer to resolve
against the live `Items` enum on a running server.

### Item-box buff

Each round a single **random timed buff** is chosen from the implementer-defined pool
and applied to the player who picks up the box. The buff type and its exact modifier
name/KV are determined by the implementer against the live modifier API. The buff is
**timed and self-clearing** via `Timer.Once(duration, removeFn)` and force-cleared on
death and on RoundEnd so it never leaks across rounds.

### Arena bounds

Reuse the LockTimer zone pattern: an AABB `{ min:[x,y,z], max:[x,y,z] }` in world
units, keyed by map name, loaded from a `arena.yaml` next to the plugin (see
`LockTimer/Zones/ZoneConfig.cs` + `Zone.Contains(point, margin)`). Pick one lane
section of `dl_midtown` as the box; the exact corners must be captured in-game
(open question — flythrough + `getpos`, or derive from a Walker pair's positions).
Team spawn lines are two small point sets near opposite ends of the box.

## State machine

Single enum `ArenaPhase { Lobby, RoundStart, RoundActive, RoundEnd, MatchEnd }`
plus a `_phaseUntil` (CurTime deadline) for the timed phases.

| From | Condition | To | Actions |
|------|-----------|----|---------| 
| Lobby | both teams have ≥ `TeamSize` ready players | RoundStart | reset scores, set `_phaseUntil = now + ResetSeconds` |
| RoundStart | `now ≥ _phaseUntil` | RoundActive | teleport+heal+loadout already applied at entry; lift freeze, enable fighting |
| RoundActive | a team has 0 living players | RoundEnd | award round to surviving team |
| RoundActive | `RoundTimeLimitSeconds` elapsed | RoundEnd | tiebreak (most living, then most total HP) |
| RoundEnd | `now ≥ _phaseUntil` AND winner < `RoundsToWin` | RoundStart | next round |
| RoundEnd | a team reached `RoundsToWin` | MatchEnd | announce match winner |
| MatchEnd | `now ≥ _phaseUntil` | Lobby (or RoundStart) | reset scores; back to lobby, or straight into a new match if still full |
| any | player count drops below `2*TeamSize` | Lobby | abort current match, reset |

Entry actions for RoundStart run **once** on transition (guard with a
`_roundInitialized` flag), not every tick. The per-tick ticker only checks
deadlines and win conditions and re-applies confinement + the frozen match
clock.

## Arena

**Clearing the rest of the map.** On `OnStartupServer`, strip all map combat
NPCs exactly like Deathmatch does — iterate `Entities.All`, `ent.Remove()` for
designer names in the removal set (`npc_boss_tier1/2/3`, `npc_barrack_boss`,
`npc_base_defense_sentry`, `npc_trooper_boss`), and re-strip in
`OnEntitySpawned` to catch late spawns. Disable NPC spawning with
`ConVar.Find("citadel_npc_spawn_enabled")?.SetInt(0)`. (Unlike TrooperInvasion,
ArenaFights does **not** self-spawn Guardians.)

**Keeping the match alive.** Reuse Deathmatch's clock pinning: every tick, pin
`m_flGameStartTime` / `m_fLevelStartTime` / `m_flRoundStartTime` together, and
force `m_eGameState` back to `GameInProgress` if it ever advances, so the engine
never shows a "team won" screen. Suppress `gameover_msg` and `round_end` game
events with `HookResult.Stop` (Deathmatch does both).

**Confining players to the zone.** The arena is a **circle** (not a fixed AABB)
centered on the arena mid-point. Its radius scales with the number of players
currently in the match: `radius = ArenaRadiusPerPlayer * totalPlayers`, so a 2v2
uses a tighter zone than a 3v3. Use `ArenaBounds` (AABB) only as the outer hard
boundary for the map section; the circular zone sits inside it.

Each tick during RoundActive, for every living pawn outside the circle:
- Track `_outsideSeconds[slot]` — how many continuous seconds that player has been
  outside. Reset to 0 the instant they re-enter.
- Apply tick damage: `damage = ConfinementDamageBase + ConfinementDamageRampPerSecond * _outsideSeconds[slot]`
  via `pawn.Hurt(damage)` (non-lethal clamp: never deal more than `pawn.Health - 1`
  so the zone alone can't kill — only prolonged exposure combined with enemy pressure
  finishes the player). This ensures leaving the zone is a sustained pressure, not an
  instant kill or a free repositioning tool.
- Print a **one-time** warning to the player on first breach: "Return to the arena!"
  via `Chat.PrintToChat`. Do not spam per-tick.

The `_outsideSeconds` counter must be cleared on RoundEnd/RoundStart so accumulated
exposure doesn't carry across rounds.

## Team assignment

Teams are Amber (`2`) and Sapphire (`3`). On `OnClientFullConnect`:
- Count current players per team. Assign the joiner to whichever playable team
  has fewer players (tie → random), capped at `TeamSize` per team. Use
  `controller.ChangeTeam(team)` (server-initiated, bypasses the picker — see
  [[deadlock-game]]).
- If both teams are already full, place the joiner as spectator (team `0`) /
  queue them for the next match.
- Assign a hero. Either let players keep their pick, or — to avoid an empty
  lobby stalling — auto-`SelectHero` a least-used available hero the way
  Deathmatch does (filter by `AvailableInGame`). Open question whether
  ArenaFights wants free hero pick (compose `HeroSelect`) or auto-assign.
- Lock teams once a match starts: block mid-match team changes by composing
  `TeamChangeBlock`, and reset assignments only between matches.

**Mid-match joins.** If a player connects while a match is Active (a round is in
progress), they are placed as **spectator** (`MakeObserver`) until the current
match ends. They are added to a team and made eligible for the next match only.
This prevents mid-round team-size changes breaking living-count logic.

`!ready` (below) gates the Lobby→RoundStart transition so players control when
the first round begins.

## Round start

On transition into RoundStart, run the per-round reset once:

1. For every player on a playable team:
   - Clear any dead state (see No-respawn) and ensure the pawn is alive — if the
     player died last round, force a respawn now (RoundStart is the *only* place
     respawns are allowed). This may require temporarily restoring a normal
     `RespawnTime` and triggering respawn, then re-suppressing.
   - Teleport to a team spawn point (`pawn.Teleport(position)`), spread across the
     team's spawn line so teammates don't stack.
   - Full heal (use the native heal path noted in [[deadlock-game]]: poll
     `GetMaxHealth` for up to ~20 ticks post-respawn since it returns 0 for a
     few ticks, then `Heal`).
   - Clear stale buffs/modifiers from the previous round.
   - Grant the fixed loadout: `foreach (name in Loadout) pawn.AddItem(name, enhanced)`.
   - `MaxUpgradeSignatureAbilities(pawn)` so abilities are usable.
   - Apply spawn protection (`SpawnProtectionSeconds`) via the
     Deathmatch invuln pattern (`ModifierProp.SetModifierState(Invulnerable, …)`
     + `OnTakeDamage` zeroing as belt-and-suspenders).
2. Spawn the center item-box (below).
3. Announce the round: HUD `CCitadelUserMsg_HudGameAnnouncement` with round
   number and score; chat countdown.

## Item-box

**Entity / mechanism.** Spawn a server-side prop at arena center to represent
the box, using the `CreateByDesignerName` + `CEntityKeyValues` + `Spawn` pattern
TrooperInvasion uses for Guardians. Candidate entity classes (open question —
confirm one exists and is networkable on this build):
- a generic physics/model prop (`prop_physics` / `prop_dynamic`) with a custom
  model, used purely as a visual + position anchor; OR
- a trigger volume entity for native touch detection.

Layer a visual on top with `ParticleSystem` (`CParticleSystem.Create(path)
.AtPosition(center).StartActive(true).Spawn()`) and/or `CBaseEntity.SetScale`
so the box reads clearly. Optionally a looping `SoundEvent` beacon.

**Pickup detection.** Two options, pick by what the entity supports:
1. **Touch hooks** — if the box is (or carries) a trigger, use
   `OnEntityStartTouch(EntityTouchEvent)` and match `e.Other` to a living player
   pawn. Cleanest if available.
2. **Proximity poll** (fallback, always works) — each RoundActive tick, if the
   box is unclaimed, find the nearest living pawn within `ItemBoxPickupRadius` of
   the box position; first one in range claims it.

On pickup:
- Mark the box claimed, remove/hide the box entity + particle, play a grab sound.
- Pick the round's `BuffSpec` (chosen at RoundStart, revealed on pickup) and
  apply it to the grabbing pawn for `ItemBoxBuffDurationSeconds` via
  `pawn.AddModifier(name, kv3)` (build magnitude with `KeyValues3.SetFloat`).
- Announce "`<player>` grabbed `<buff>`!" to all (HUD + chat) so the enemy team
  knows to focus them.
- Schedule removal: `Timer.Once(duration, () => removeModifier(pawn))`. Also
  remove on that pawn's death and on RoundEnd.

Only **one** box per round, single pickup. If the holder dies, the buff is lost
(not dropped) in v1 — dropping/re-pickup is a possible later enhancement (open
question).

## No-respawn (within a round)

Deadlock auto-respawns dead players. To suppress that for the duration of a
round:

- **Primary:** set `CCitadelPlayerPawn.RespawnTime` (read/write float, v0.4.8) to
  a very large value when a player dies during RoundActive, so the engine never
  brings them back mid-round. Restore a normal value only at RoundStart when we
  deliberately respawn everyone.
- **Belt-and-suspenders:** also intercept `player_respawned` — if we get an
  unexpected respawn during RoundActive (engine ignored RespawnTime), immediately
  put the pawn back into a dead/observer state. Composing the Observer API
  (`MakeObserver`, v0.4.7) lets eliminated players spectate the rest of the round
  cleanly instead of sitting at a respawn screen.
- Track living players per team in a `Dictionary`/`HashSet` updated on
  `player_death`. RoundActive win check reads these counts. A disconnect also
  decrements the living count (handle in `OnClientDisconnect`).

> Open question: confirm `RespawnTime` alone reliably blocks the engine respawn
> on this build; if not, lean on the `player_respawned` re-kill / MakeObserver
> path as the real mechanism. `citadel_player_spawn_time_max_respawn_time` caps
> *max* time but the engine enforces a *minimum* floor (per [[deadlock-game]]),
> so the convar alone is not sufficient.

## Round end

- **Last-team-standing:** RoundActive tick checks living counts. When exactly one
  playable team has > 0 living and the other has 0, that team wins the round.
  If both hit 0 on the same tick (mutual kill), the round is a **draw** — replay
  it: no score change, return to RoundStart immediately.
- **Timer expiry tiebreak:** if `RoundTimeLimitSeconds` elapses with both teams
  alive, decide by (1) most living players, then (2) highest summed HP, then (3)
  draw → replay.
- On decision: transition to RoundEnd, increment the winning team's round score,
  HUD-announce `"<Team> wins round N — Amber X : Y Sapphire"`, set
  `_phaseUntil = now + RoundEndPauseSeconds`. Clear all buffs and the item-box.
  Eliminated players stay spectating until the pause ends.

## Match end

- After incrementing the round score, if a team reached `RoundsToWin`, go to
  MatchEnd instead of RoundStart.
- HUD `CCitadelUserMsg_HudGameAnnouncement`: `"<Team> wins the match! (X–Y)"`,
  plus chat. Optional per-player summary (kills/round wins) emitted to stats if a
  stats client is wired (Deathmatch/TrooperInvasion both have one; ArenaFights
  can reuse the same `StatsClient` shape — optional for v1).
- Pause `MatchEndPauseSeconds`, then reset round scores and living state and
  return to Lobby; auto-start a fresh match if both teams are still full and
  ready.

## Chat commands

Implemented with the `[Command]` attribute (see [[command-attribute]]):

| Command | Phase | Behavior |
|---------|-------|----------|
| `!ready` | Lobby | Marks the caller ready. When all players on both full teams are ready, start the match (Lobby→RoundStart). Re-issue toggles. Echo ready count. |
| `!score` | any | Print current match score, round number, and rounds-to-win to the caller. |
| `!stuck` | RoundActive | Caller is wedged on geometry. Since dying ends their round, **do not** kill them — instead teleport them to a safe in-bounds point near their current position (or their team spawn). This deliberately differs from Deathmatch/`StuckCommand`, where `!stuck` suicides to respawn. Rate-limit to prevent abuse (e.g. once per N seconds). |
| `!help` | any | List the above. |

Because `!stuck` here must **not** kill (no respawns), ArenaFights provides its
own variant and should **not** compose the shared `StuckCommand` plugin
(its suicide behavior would be fatal mid-round). Same reasoning Deathmatch uses
for keeping its own `!stuck` variant (see wiki log 2026-05-29).

## HUD / feedback

- **Round/score banner** at each RoundStart and RoundEnd via
  `CCitadelUserMsg_HudGameAnnouncement` (Title + Description), `RecipientFilter.All`.
- **Countdown** during RoundStart/ResetSeconds via chat (or repeated HUD).
- **Death announcements** in chat on `player_death` ("`<victim>` was eliminated —
  Amber 2v3 Sapphire alive"), so everyone tracks how many remain.
- **Item-box events**: "An item-box appeared at center!" on spawn; "`<player>`
  grabbed `<buff>`!" on pickup; both HUD + chat.
- **Match result** banner at MatchEnd.
- Per-player status (your team, alive/dead, score) available via `!score`.

## Plugin composition (gamemodes.json)

Add an `arena-fights` entry alongside the existing modes. Proposed set:

```json
"arena-fights": [
  "StatusPoker",
  "ArenaFights",
  "Hostname",
  "FlexSlotUnlock",
  "TeamChangeBlock",
  "DisconnectCleanup",
  "Feedback"
]
```

Rationale (mirrors `deathmatch`):
- `StatusPoker`, `Hostname`, `Feedback` — standard server furniture.
- `FlexSlotUnlock` — so the fixed loadout can fill flex item slots.
- `TeamChangeBlock` — lock teams during a match.
- `DisconnectCleanup` — clear leaver slots so living-count logic stays correct.
- **Not** `StuckCommand` — ArenaFights ships its own non-lethal `!stuck`.
- `HeroSelect` / `HeroSelectOnNextSpawn` — include only if players pick heroes;
  omit if ArenaFights auto-assigns (open question).

The plugin folder is `ArenaFights/` (PascalCase, one folder per plugin) with an
`ArenaFights.csproj` referencing `DeadworksManaged.Api`; net-message use pulls in
the `Google.Protobuf` PackageReference required for Docker CI (see
[[netmessages-api]]).

## Open questions

1. **Item-box entity API.** What concrete server-spawnable entity should the box
   be on this build — `prop_physics`/`prop_dynamic` for a visual anchor, or a
   trigger volume for native touch? Does `OnEntityStartTouch` fire for it, or do
   we fall back to proximity polling? What model + particle path render a clear
   "powerup" object? Confirm `CreateByDesignerName` works for the chosen class
   the way it does for `npc_boss_tier1`.
2. **Suppressing auto-respawn.** Does setting `CCitadelPlayerPawn.RespawnTime`
   huge reliably prevent the engine from respawning a player mid-round, given the
   engine's minimum respawn floor? If not, is the `player_respawned` re-kill /
   `MakeObserver` path the canonical mechanism?
3. **Arena AABB / zone center.** Survey `dl_midtown` in-game on first run with a
   `!pos` admin command to capture the arena center and the outer AABB corners.
   Until surveyed, derive the center from Walker lane midpoints (Deathmatch
   pattern) as a placeholder.
4. **Hero pick: free choice vs auto-assign.** Whether ArenaFights composes
   `HeroSelect` (free pick) or auto-assigns (Deathmatch-style least-present hero)
   is left to the implementer. Include `HeroSelect` in the composition only if
   free pick is chosen.

Resolved: draw on simultaneous wipe → replay round. `HealOnSpawn` dropped
(ArenaFights heals itself at RoundStart). Mid-match joins → spectate until
next match. Loadout details and buff modifier names → implementer resolves
against live server.
