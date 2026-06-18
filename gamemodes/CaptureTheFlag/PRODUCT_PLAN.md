# CaptureTheFlag — Product Plan

> Implementation-ready design for a One-Flag Capture-the-Flag gamemode plugin
> for the Deadworks-managed Deadlock dedicated server.
> No C# in this document — only design, API references, and open questions.

## Overview

CaptureTheFlag (CTF) is a server-side gamemode in which a single **neutral
flag** spawns at the center of `dl_midtown`. Both teams (Amber = 2,
Sapphire = 3) fight to grab the flag, escort the carrier into the **enemy
base zone**, and hold it there for a hold-time to score a capture. First team
to a configured number of captures wins. The flag is **fully simulated in
plugin state** — there is no custom map entity. "Grabbing" the flag is a
proximity check against a tracked world position; "carrying" is a per-pawn
state flag; the flag's physical presence is communicated entirely through chat
messages (and, optionally, a billboard text entity — see Open Questions). The
Deadlock twist: the carrier gains **+50% move speed** but **cannot fire their
weapon** (primary `Attack`), forcing teams to escort rather than solo-run the
flag. The flag resets to center if the carrier dies, disconnects, or drops it
unclaimed for too long. Players respawn on a short delay so death is a tempo
cost, not an elimination.

This plan follows the patterns established by [[Deathmatch]] (NPC stripping,
clock pinning, spawn rituals, per-slot state dictionaries, stats emission) and
[[LockTimer]] (AABB zone containment via `Zone.Contains`).

---

## Game loop

```
OnStartupServer
   │  strip MOBA NPCs, pin HUD clock, compute flag center, load base zones
   ▼
Warmup ──(≥1 human connected & countdown elapsed)──► RoundActive
   │
   ▼
RoundActive  (flag in play; poll loop running)
   │  capture scored ──► announce ──► reset flag to Neutral ──┐
   │                                                          │
   │  (team capture count < CapturesToWin)  ◄─────────────────┘
   │
   └─(a team reaches CapturesToWin)──► MatchEnd
                                          │ announce winner, freeze scoring
                                          ▼
                                       Warmup (new match) after MatchEndDelay
```

- **Warmup** — entered on map load and after every match end. Flag sits
  Neutral at center but pickups are ignored; scores are zero. Leaves Warmup
  once at least one human is connected and a short `WarmupSeconds` countdown
  elapses. This mirrors Deathmatch's "session starts when first human joins"
  gating (`_humanCount`).
- **RoundActive** — the steady state. The poll timer (below) runs, pickups
  and captures are live, deaths drop the flag.
- **Capture scored** — increment the scoring team's count, broadcast, reset
  the flag to Neutral at center, and either continue or transition to
  MatchEnd.
- **MatchEnd** — a team hit `CapturesToWin`. Announce the winner via
  `CCitadelUserMsg_HudGameAnnouncement` (as Deathmatch does for rotations),
  freeze capture scoring, then after `MatchEndDelay` reset all state and
  re-enter Warmup. **Do not** let the engine show its own win screen — suppress
  `gameover_msg` and `round_end` and keep `m_eGameState` pinned to
  `GameInProgress`, exactly as `DeathmatchPlugin.TickMatchClock` /
  `OnGameoverMsg` / `OnRoundEnd` do (`Deathmatch.cs:267-283`).

---

## Configuration

A `CaptureTheFlagConfig` POCO bound with `[PluginConfig]` (see
[[plugin-config]]; reloadable via `dw_reloadconfig` → `OnConfigReloaded`).
All gameplay constants live here so they can be tuned without recompiling.

| Field | Default | Meaning |
|-------|---------|---------|
| `CapturesToWin` | `3` | Captures for a team to win the match |
| `CarrierSpeedMultiplier` | `1.5` | Move-speed factor applied to the carrier (+50%) |
| `CaptureHoldSeconds` | `10.0` | Continuous time the carrier must stay in the enemy base zone to score |
| `RespawnDelaySeconds` | `5.0` | Delay before a dead player respawns |
| `FlagResetSeconds` | `30.0` | Time a dropped, unclaimed flag waits on the ground before teleporting back to center |
| `PickupRadius` | `150.0` | World units; carrier-claim proximity radius around the flag position |
| `PollIntervalTicks` | `4` | How often the proximity/capture poll runs (see "Flag pickup") |
| `WarmupSeconds` | `15.0` | Countdown before the first round starts once a human is present |
| `MatchEndDelay` | `15.0` | Pause on the win screen before resetting to Warmup |

> Speed and hold-time defaults match the concept brief. `PickupRadius` and
> `PollIntervalTicks` are starting points to tune against observed feel —
> `150` units is roughly hero-melee range; widen if pickups feel unresponsive.

---

## State machine

The flag is a single tracked object with four states. Only one flag exists.

```
                 carrier dies / disconnects / drops
        ┌──────────────────────────────────────────────┐
        ▼                                                │
   ┌─────────┐  player in PickupRadius   ┌──────────┐    │
   │ Neutral │ ────────────────────────► │ Carried  │ ───┘
   │(@center)│                           │ (by X)   │
   └─────────┘ ◄──────────┐              └────┬─────┘
        ▲                 │ reset timer        │ carrier enters
        │                 │ expires            │ enemy base zone
        │            ┌─────────┐               ▼
        │            │ Dropped │          ┌────────────┐
        └────────────│(ground, │ ◄────────│ Capturing  │
   capture scored    │countdown)│ carrier  │(in zone,   │
   (→ Neutral)       └─────────┘ leaves    │ counting)  │
                          ▲      zone /     └─────┬──────┘
                          │      dies             │ hold reaches
              another player picks up             │ CaptureHoldSeconds
              from ground (→ Carried)             ▼
                                            score++, → Neutral
```

State data tracked in the plugin (single struct, not per-entity):

- `State`: `Neutral | Carried | Dropped | Capturing`
- `Position`: current world position of the flag (center, carrier pawn pos, or
  drop pos). For `Carried`/`Capturing` it tracks the carrier each poll tick.
- `CarrierSlot` / `CarrierEntityIndex`: who holds it (valid only in
  `Carried`/`Capturing`).
- `DropDeadlineCurTime`: `GlobalVars.CurTime + FlagResetSeconds`, set on entry
  to `Dropped`; reset countdown checked each poll.
- `CaptureDeadlineCurTime`: `GlobalVars.CurTime + CaptureHoldSeconds`, set on
  entry to `Capturing`; cleared if the carrier leaves the zone or dies.

**State notes**

- `Neutral → Carried`: any living player (either team) within `PickupRadius`
  of center claims it. On a tie within radius, pick the nearest.
- `Carried → Capturing`: carrier's position enters the **enemy** base zone
  (the zone belonging to the team the carrier is *attacking*, i.e. not the
  carrier's own team).
- `Capturing → Carried`: carrier leaves the zone before the hold completes —
  cancel `CaptureDeadlineCurTime` and announce "capture interrupted".
- `Carried/Capturing → Dropped`: carrier dies or otherwise loses the flag
  without scoring. Drop at carrier's last position (death position).
- `Carried/Capturing → Neutral` (disconnect): if the carrier disconnects,
  skip `Dropped` and reset straight to center (no body to drop at), as the
  brief specifies.
- `Dropped → Carried`: another living player enters `PickupRadius` of the
  drop position.
- `Dropped → Neutral`: `FlagResetSeconds` elapsed unclaimed → teleport flag
  back to center.
- `Capturing → Neutral`: hold complete → `score++`, broadcast, reset to
  center (and possibly MatchEnd).

---

## Flag pickup

Driven by a single `Timer.Every(Config.PollIntervalTicks.Ticks(), Poll)`
established in `OnStartupServer` (the same registration shape Deathmatch uses
for `ScaleAbilityCooldowns` / `TickMatchClock`, `Deathmatch.cs:127-128`; timer
semantics in [[timer-api]] — auto-cancelled on map change / unload).

Each `Poll` tick, while in `RoundActive`:

1. **If `Neutral` or `Dropped`** — iterate `Players.GetAllPawns()`, skip dead
   pawns (`!IsAlive`), compute horizontal distance from pawn position to the
   flag `Position`. If within `PickupRadius`, transition to `Carried`
   (nearest wins on multiple candidates). Use squared-distance comparison to
   avoid per-pawn `sqrt`.
2. **If `Carried` or `Capturing`** — resolve the carrier pawn by entity index
   (`CBaseEntity.FromIndex<CCitadelPlayerPawn>`). If gone/dead, treat as a
   drop (handled primarily by the death hook; this is a safety net). Update
   `Position` to the carrier pawn position. Then test the enemy base zone:
   in-zone drives `Carried → Capturing` / advances the capture countdown;
   out-of-zone drives `Capturing → Carried`.
3. **Dropped reset** — if `Dropped` and `GlobalVars.CurTime >=
   DropDeadlineCurTime`, reset to center (`Neutral`).

**Poll cadence.** `PollIntervalTicks = 4` (~every 4 ticks; Deadlock runs at
the Source-2 tick rate) is a good default: frequent enough that a sprinting
carrier can't skip through the ~thin pickup radius or tunnel through the base
zone between polls, cheap enough that scanning ≤ a handful of pawns is
negligible. Pickups/zone tests are O(players) per tick — with ≤ 12 players
this is trivial. See Open Questions on whether the carrier-effect refresh
(speed/no-fire) wants a finer cadence than the pickup scan.

---

## Flag carrier effects

Two effects apply to the carrier and must be **removed** the instant they stop
carrying (drop, capture, death, disconnect).

### Speed boost (+50%)

`CarrierSpeedMultiplier` applied to the carrier pawn via `pawn.AddModifier(name, kv)`
(`CBaseEntity.cs:220`) — applied once on pickup, removed on flag loss. The exact
modifier name and KV structure are left to the implementer to resolve against the
live modifier API on a running server. Fallback if no suitable modifier exists:
write a movement-speed schema field each poll tick (field name also needs confirming
on a live server). Remove the effect the instant the carrier drops/dies/disconnects.

### Cannot fire weapon

The carrier must be unable to fire their primary weapon. This is **confirmed
feasible** via `OnAbilityAttempt` (the return-less mask hook in
[[events-surface]]): each call, if the slot is the current carrier, block the
primary-fire button:

- `args.Block(InputButton.Attack)` — primary weapon-fire (`0x1`).
- `args.Block(InputButton.Attack2)` — alt-fire (`0x800`).

Block both. Abilities and items remain usable — keeping them live is what makes
escorted carries interesting. Blocked masks are OR'd across plugins each tick so
this needs no explicit cleanup; the engine rebuilds the mask every tick and the
block naturally stops the moment the player is no longer marked as carrier.

---

## Base zones

Each team defends a **base zone** — an AABB near that team's Patron/base on
`dl_midtown`. The carrier scores by holding the flag in the **enemy** team's base
zone. Reuse the [[LockTimer]] zone abstraction: `Zone` is an AABB record with
`Contains(Vector3 p, float margin = 20f)` (`LockTimer/Zones/Zone.cs`).

- Define two zones per map: `base_amber` (team 2's base) and `base_sapphire`
  (team 3's base).
- The **enemy zone for a carrier** = the zone of the team that is *not* the
  carrier's `TeamNum`. Amber carrier scores in `base_sapphire`; Sapphire carrier
  scores in `base_amber`.
- **Zones are built dynamically at `OnStartupServer`** — no hardcoded coordinates.
  Read the **Patron** (`npc_boss_tier3`) position per team (the canonical base
  anchor, confirmed alive in CTF) and build an AABB centered on it with a generous
  fixed radius. Also capture **Walker** (`npc_boss_tier2`) positions (Deathmatch
  pattern, `Deathmatch.cs:114-120`) as fallback anchors and for the flag center
  derivation. The zone auto-adapts and won't break on map updates.
- Patron and Walkers are **kept alive** — they are the natural base landmarks and
  their positions are the zone anchors. Only troopers are stripped (see NPC strip
  below).

---

## Capture scoring

1. Carrier enters the enemy base zone (`Zone.Contains(carrierPos)`): transition
   `Carried → Capturing`, set `CaptureDeadlineCurTime = CurTime +
   CaptureHoldSeconds`, broadcast "Amber is capturing! 10…".
2. While `Capturing`, each poll re-checks the carrier is still alive and still
   in the zone. If they leave or die → `Capturing → Carried`/`Dropped`, clear
   the deadline, broadcast "capture interrupted". Optionally broadcast a
   countdown at whole-second boundaries (e.g. 10/5/4/3/2/1) using a tracked
   "last announced second" to avoid spamming every poll tick.
   **Zone-boundary grace:** only interrupt the capture if the carrier has been
   outside the zone for at least 2 consecutive poll ticks. A single-tick gap
   (carrier on the boundary, oscillating due to movement/physics) must not
   reset the deadline.
3. When `CurTime >= CaptureDeadlineCurTime` with the carrier still in-zone:
   increment that team's capture count, broadcast "Amber scores! 2–1", reset
   the flag to center (`Neutral`), and check the win condition.

Captures are tracked as a simple `int[teamNum]` (or two ints). Reset to zero
on match start / Warmup entry.

---

## Flag drop on death

Hook the native `player_death` game event (typed `PlayerDeathEvent`, as
Deathmatch does, `Deathmatch.cs:498`). The event carries the victim position
(`VictimX/Y/Z`) — Deathmatch already uses these for respawn placement
(`Deathmatch.cs:502-503`).

On `player_death`:

- If the victim is the current carrier (`Carried` or `Capturing`):
  - Transition to `Dropped`.
  - Set flag `Position` to the death position `(VictimX, VictimY, VictimZ)`.
  - Set `DropDeadlineCurTime = CurTime + FlagResetSeconds`.
  - Remove the carrier effects (speed modifier; stop blocking `Attack`).
  - Clear `CarrierSlot`/`CarrierEntityIndex` and any active capture deadline.
  - Broadcast "Amber dropped the flag!".
- If the victim is not the carrier, no flag action.

Return `HookResult.Continue` — the death itself should proceed normally.

---

## Flag reset

The flag returns to center in three ways:

1. **Dropped timeout** — `Dropped` state + `CurTime >= DropDeadlineCurTime`
   (checked in `Poll`). Teleport to center, `Neutral`, broadcast "Flag
   returned to center."
2. **Carrier disconnect** — handled in `OnClientDisconnect`: if the
   disconnecting controller is the carrier, reset straight to `Neutral` at
   center (no `Dropped` phase — there's no body), remove effects, broadcast.
   Mirror Deathmatch's per-slot cleanup discipline (`Deathmatch.cs:1064-1102`):
   clear any carrier state keyed to that slot/entity.
3. **Capture scored** — reset to center as part of scoring (above).

"Center" = the map center. Deathmatch computes `_mapCenter` as the centroid of
all Walker spawn points (`Deathmatch.cs:354-360`); reuse the same derivation,
or use the midpoint between the two Patron positions. Avoid hardcoding.

---

## Respawn

- Set the respawn delay globally at startup: Deathmatch sets
  `citadel_player_spawn_time_max_respawn_time` (`Deathmatch.cs:110`). Note the
  engine still enforces a **minimum respawn floor** ([[deadlock-game]] match
  ConVars), so very short delays may not be honored exactly — verify `5s` lands
  where intended; the per-pawn `CCitadelPlayerPawn.RespawnTime` field is the
  authoritative knob if the ConVar proves too coarse.
- On `player_respawned` (typed `PlayerRespawnedEvent`, as Deathmatch hooks at
  `Deathmatch.cs:542`), apply any spawn ritual the mode wants (e.g. brief spawn
  protection like Deathmatch's `GrantSpawnProtection`, optional). Respawn
  **location** can stay engine-default (team base) — natural for CTF since you
  want to respawn defenders at their own base — or be customized later.
- The carrier never respawns *as* carrier: death already moved the flag to
  `Dropped`, so a respawned ex-carrier is a normal player.

---

## Win condition

- After each capture, if the scoring team's count `>= CapturesToWin`,
  transition to **MatchEnd**.
- Announce via `CCitadelUserMsg_HudGameAnnouncement` (title + description),
  the same net message Deathmatch uses for round results
  (`Deathmatch.cs:603-607`; [[netmessages-api]]) sent to
  `RecipientFilter.All`, plus a chat broadcast.
- Freeze scoring; keep the clock/`GameInProgress` pinned so the engine win
  screen never appears.
- After `MatchEndDelay`, reset captures, flag, and per-player state, then
  re-enter Warmup for the next match.

---

## Chat commands

Implemented with `[Command(...)]` attributes (see [[command-attribute]];
Deathmatch's `!help`/`!stuck` are the model, `Deathmatch.cs:845-870`). Chat
commands run before `OnChatMessage` ([[events-surface]]).

| Command | Behavior |
|---------|----------|
| `!flag` | Reports the flag's current state and rough location to the caller — e.g. "Flag: carried by <name> (Amber)", "Flag: dropped near Sapphire base, returns in 12s", or "Flag: neutral at center". |
| `!score` | Prints current capture score, e.g. "Amber 2 – 1 Sapphire (first to 3)". |
| `!stuck` (alias `!suicide`) | Kill self to respawn — copy Deathmatch's implementation verbatim (`Deathmatch.cs:852-870`): clear spawn-protection invuln bits, then `pawn.Hurt(999_999f)`. If the stuck player is the carrier, the resulting `player_death` drops the flag normally. |

Consider a `!help` listing these, as Deathmatch does. (`StuckCommand` also
exists as a standalone plugin — see Plugin composition; if composed in, the
mode may not need to reimplement `!stuck`.)

---

## HUD / feedback

All flag feedback is **chat-based** (`Chat.PrintToChatAll` / `Chat.PrintToChat`
per [[plugin-api-surface]]-style usage in Deathmatch), with optional big
announcements for captures/wins via `CCitadelUserMsg_HudGameAnnouncement`.

Broadcast events (all team-named, Amber/Sapphire):

| Event | Message (example) | Channel |
|-------|-------------------|---------|
| Flag picked up | "<name> (Amber) grabbed the flag!" | chat all |
| Flag dropped (death) | "Amber dropped the flag near Sapphire base!" | chat all |
| Flag returned (timeout) | "The flag returned to center." | chat all |
| Capture started | "Amber is capturing! Hold for 10s…" | chat all |
| Capture interrupted | "Capture stopped — carrier left the zone." | chat all |
| Capture countdown | "Capturing… 3 / 2 / 1" (whole-second gating) | chat all |
| Capture scored | "Amber scores! 2 – 1" | chat all + HUD announcement |
| Score update | rendered as part of capture-scored message | chat all |
| Match win | "Amber Team Wins the match!" | HUD announcement + chat all |

---

## Plugin composition (gamemodes.json)

Add a `capture-the-flag` entry to the repo-root `gamemodes.json` (current
entries: `normal`, `lock-timer`, `deathmatch`, `trooper-invasion`). Proposed
composition, modeled on the `deathmatch` line:

**NPC strip set:** strip `npc_trooper_boss` and `npc_trooper` (lane troopers and
their bosses) so the map isn't cluttered with MOBA creep AI. **Keep** `npc_boss_tier3`
(Patron), `npc_boss_tier2` (Walkers), `npc_boss_tier1` (Guardians), and
`npc_barrack_boss` (Watchers) — these are the base landmarks that define zones and
give the map its defensive geometry. Re-strip in `OnEntitySpawned` to catch late
trooper spawns, and disable the trooper spawner:
`ConVar.Find("citadel_npc_spawn_enabled")?.SetInt(0)`.

**Team assignment:** CaptureTheFlag enforces balance at join using
`controller.ChangeTeam(int)` (Deathmatch pattern, `Deathmatch.cs:114-120`) —
assign joiners to whichever playable team has fewer players. `TeamChangeBlock`
blocks client-side re-teaming; the plugin's server-side assignment bypasses it
cleanly. This neutralizes the engine auto-balancer.

```json
"capture-the-flag": ["StatusPoker", "CaptureTheFlag", "Hostname", "FlexSlotUnlock",
                     "TeamChangeBlock", "HealOnSpawn", "DisconnectCleanup",
                     "HeroSelect", "Feedback", "StuckCommand"]
```

Rationale per included plugin:

- **CaptureTheFlag** — this mode.
- **StatusPoker**, **Hostname**, **Feedback** — standard in every mode.
- **TeamChangeBlock** — blocks client concommands; plugin enforces server-side balance.
- **HealOnSpawn** — full HP on respawn fits the fast-respawn loop.
- **DisconnectCleanup** — entity removal on disconnect; mode still needs its own
  `OnClientDisconnect` for flag/carrier state cleanup.
- **FlexSlotUnlock** — full ability slots so heroes are kitted.
- **HeroSelect** — free hero pick; CTF is a positioning/teamwork mode where hero
  choice matters.
- **StuckCommand** — provides `!stuck` (CTF uses fast respawn so death is not
  terminal; the generic suicide variant is correct here).

The `.csproj` should follow the sibling plugin layout (one project per folder,
`DeadworksManaged.Api` reference); copy `Deathmatch/Deathmatch.csproj` as the
template.

---

## Open questions

1. **Carrier speed boost modifier name** — `pawn.AddModifier(name, kv)` is the
   right vehicle; the exact modifier name and KV keys need to be resolved on a
   live server. Fallback: write a movement-speed schema field each poll tick
   (field name also needs confirming). Implementer resolves.
2. **`Attack`/`Attack2` coverage** — confirm in-game that blocking both input
   bits disarms all heroes (some have unusual primary weapons). If a hero can
   still fire via a third input path, extend the block set.
3. **Patron lethality** — Patron and Walkers are kept alive as landmarks, but if
   lane troopers (now stripped) previously prevented the Patron from being
   attacked, confirm that a stripped-trooper map doesn't let one team kill the
   enemy Patron directly and trigger the engine's native win path. May need to
   suppress `gameover_msg`/`round_end` + pin `m_eGameState` as Deathmatch does
   even though we're not stripping Patrons.

Resolved: weapon block = `Attack | Attack2`. Speed boost = implementer resolves
modifier API. Base zones = built dynamically from Patron position at startup.
NPC strip = troopers only; Patron + Walkers kept. Team balance = plugin enforces
at join (Deathmatch pattern). Stalemate/time-limit = out of scope. Flag entity =
chat-only v1, no planned stretch goal.
