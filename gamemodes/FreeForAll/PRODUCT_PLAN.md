# FreeForAll — Product Plan

> Status: planning document. No code yet. This plan is written against the
> Deadworks managed API as documented in `knowledge-base/wiki/` and as used by
> the existing `Deathmatch/` and `TrooperInvasion/` plugins. One remaining open
> item (friendly-fire ConVar name) is flagged in **Open Questions** at the bottom.

## Overview

FreeForAll (FFA) is a no-teams deathmatch gamemode for `dl_midtown`. Every
player fights every other player; the first to reach a configurable kill target
wins the match. Respawns are near-instant, ability cooldowns are globally halved
to keep the pace high, and there is **no soul/item economy** — every player
starts with a fixed loadout and item builds are removed as a differentiator so
that ability mastery and positioning decide fights. Ziplines stay open for
aggressive repositioning. Structurally the plugin reuses the proven Deathmatch
machinery (permanent round loop, gameover suppression, walker-anchored spawns,
spawn protection, cooldown scaling) but rips out team scoring and replaces it
with per-player kill tracking and a single-winner end condition.

The one hard constraint that shapes every other decision: **the Deadlock engine
only models two teams (2 = Amber, 3 = Sapphire).** There is no native "no team"
mode. FFA therefore has to *simulate* free-for-all on top of a two-team engine.
**Decision (Q1):** put everyone on team 2 (Amber) and set `mp_friendlyfire 1`
(or the Deadlock-equivalent ConVar — see Open Questions) at startup so same-team
damage lands. Kill counting already requires `attacker != victim` so self-damage
is excluded. Same-team visuals (green outlines, friendly reticle) may look odd —
accepted trade-off for v1; implementer notes what actually renders.

## Game loop

1. **Server start / map load** (`OnStartupServer`)
   - Set ConVars: disable NPC/trooper spawns, allow purchasing anywhere off,
     short max respawn time, allow duplicate heroes.
   - Strip all map NPCs (Guardians, Walkers-as-objectives, Patron, sentries,
     troopers) so the map is pure PvP. **Keep Walker positions** captured first
     as spawn anchors (same as Deathmatch), then remove the entities.
   - Reset all match state to `Warmup`.
   - Arm the two per-tick timers: cooldown scaler and HUD-clock/gameover pinner.
2. **Warmup** — server is empty or below `MinPlayersToStart`. No score, no win
   checks. Players who join can move and fight, but the scoreboard is frozen at
   0 and a HUD banner says "Waiting for players (`N`/`MinPlayersToStart`)".
3. **Match start** — when `humanCount >= MinPlayersToStart`, transition to
   `Active`: clear all kill counts, announce "FIRST TO `KillTarget` KILLS —
   GO!", record `matchStartUtc`.
4. **Active loop**
   - Player joins → assigned a spawn, given the spawn ritual (full HP, spawn
     protection, max signature abilities, fixed loadout), added to the
     scoreboard at 0 kills.
   - Player kills another player → attacker's kill count increments, kill feed
     + optional chat line, win condition checked.
   - Player dies → near-instant respawn at an anti-camp-scored spawn point.
   - Every tick: cooldowns halved, HUD clock pinned, gameover suppressed.
5. **Win reached** — a player hits `KillTarget` (tiebreak resolved if multiple
   crossed in the same frame) → transition to `GameOver`.
6. **GameOver** — announce winner + final top-N scoreboard, freeze scoring for
   `PostGameSeconds`, then reset all counts and either return to `Warmup` (if
   below min players) or start a fresh `Active` match. Players are **not**
   kicked; the server rolls straight into the next match (mirrors Deathmatch's
   "permanent loop" philosophy — the match never truly ends, it just cycles).

## Configuration

All tunables live on an `FreeForAllConfig` class decorated with
`[PluginConfig]` (JSONC auto-created and hot-reloadable via `OnConfigReloaded`,
per [[plugin-config]]). The host contract requires the config class to exist
even if empty (see Deathmatch note), so this is also where future knobs land.

| Key | Type | Default | Meaning |
|-----|------|---------|---------|
| `KillTarget` | int | `30` | Kills to win the match. Tuning note: consider scaling with player count in a later pass (e.g. `max(20, 5 × playerCount)`) — not a v1 code change. |
| `MinPlayersToStart` | int | `2` | Players required to leave Warmup |
| `RespawnSeconds` | float | `2.0` | Time dead before auto-respawn |
| `CooldownScale` | float | `0.5` | Multiplier on remaining ability cooldown (0.5 = −50%) |
| `SpawnProtectionSeconds` | float | `3.0` | Invulnerability window after (re)spawn |
| `PostGameSeconds` | float | `15.0` | Scoreboard display before next match |
| `StartingGold` | int | `0` | Gold granted on spawn (0 + purchase lock = no economy) |
| `LockLoadout` | bool | `true` | Suppress item purchases / soul gain |
| `FixedItems` | string[] | `[]` | Optional fixed item names granted on spawn (empty = abilities only) |
| `AnnounceKillsInChat` | bool | `true` | Print "X killed Y" lines |
| `EnableZiplines` | bool | `true` | Leave ziplines active (documented intent; ziplines are on by default) |

Constants that are **not** expected to change per-deployment (designer names of
NPCs to strip, walker designer, spawn-protection modifier states) stay as code
constants, matching Deathmatch's split between `Config` and `private const`.

## State machine

Three phases tracked by a single `enum MatchPhase { Warmup, Active, GameOver }`
field plus a few timestamps. No per-round rotation (FFA has no lane cycle).

```
        humanCount >= MinPlayersToStart
Warmup ───────────────────────────────► Active
  ▲                                        │
  │ humanCount < MinPlayersToStart         │ someone reaches KillTarget
  │ (after PostGame)                       ▼
  └────────────────────────────────── GameOver
         PostGameSeconds elapsed,
         then reset → Warmup or Active
```

- **Warmup**: scoring disabled, win checks skipped, players may still fight.
- **Active**: scoring enabled, win condition checked on every kill.
- **GameOver**: scoring frozen, winner banner shown, `gameOverUntil` timestamp
  set; the per-tick pinner watches for `CurTime >= gameOverUntil` and calls
  `ResetMatch()`.

Drive the GameOver→reset transition from the **per-tick callback**, not
`Timer.Once`. Deathmatch learned that `Timer.Every`/`Timer.Once` may not fire on
an idle/empty server, while a schema-write tick callback always runs (see
[[deathmatch]] "HUD match clock anchor"). Use the same tick for the
PostGame countdown.

## Spawning

Reuse Deathmatch's walker-anchored spawn system wholesale — it already solves
"good PvP spawn points on `dl_midtown`" and anti-spawn-camping:

- **Capture**: in `OnStartupServer` and `OnEntitySpawned`, record every
  `npc_boss_tier2` (Walker) position before removing combat NPCs. Walkers carry
  `m_eLaneColor` (`CNPC_TrooperBoss::m_eLaneColor`); for FFA we don't care about
  lane assignment, just the full pool of positions. Bucket them as a flat list
  (we can collapse Deathmatch's per-team/lane buckets into one global pool since
  there are no meaningful teams).
- **Pick**: on `player_respawned`, score candidate spawn points and pick from
  the top few at random. Port Deathmatch's `PickSpawnPoint` / `ScoreCandidate`
  scoring, but redefine "enemy" as **every other alive player** (in FFA everyone
  is a threat regardless of engine team). Scoring terms:
  - **Line-of-fire avoidance** — `Trace.Ray` from each other player's eye to the
    candidate; reject candidates inside a ~20° / 2500u firing cone with line of
    sight (the anti-spawn-camp core, see [[trace-api]]).
  - **Distance from death point** — spawn away from where you just died.
  - **Ideal-distance trapezoid** — not on top of an enemy, not across the map.
  - **Off-axis** — prefer spawns not directly in anyone's crosshair.
- **Teleport**: `pawn.Teleport(position: target)` after the engine spawn fires
  (the engine picks a default first; we relocate). Re-resolve the pawn by
  `EntityIndex` inside the handler — handles go stale across ticks.
- **First spawn**: `OnClientFullConnect` does **not** currently route through
  `PickSpawnPoint` in Deathmatch (a known gap). For FFA we should call it on
  first spawn too, or at minimum on the first `player_respawned`.

## Kill tracking

Hook the native game event:

```csharp
[GameEventHandler("player_death")]
public HookResult OnPlayerDeath(PlayerDeathEvent args) { ... }
```

`PlayerDeathEvent` exposes `AttackerController`, `AttackerPawn`, `UseridPawn`
(victim), and `VictimX/Y/Z` (used to seed `_lastDeathPos` for spawn scoring).

Scoring rules for FFA:

- A kill counts when `attacker != null`, `attacker != victim` (no suicide
  credit), and the attacker is a player pawn. **Unlike Deathmatch we do NOT
  require `attacker.TeamNum != victim.TeamNum`** — with everyone on one engine
  team, every non-self kill is a real kill.
- Maintain `Dictionary<int /*controller EntityIndex*/, int> _kills` plus a
  parallel `Dictionary<int, PlayerScore>` (name, hero, kills, deaths, hashed
  steam id) for the scoreboard and stats.
- On each counted kill: increment, fire kill feed/chat, then `CheckWinCondition`.
- Suicides / environment deaths: increment the victim's death count only.
- Clean all per-player dicts in `OnClientDisconnect` (keyed by controller and
  pawn EntityIndex), exactly as Deathmatch does.

**Known limitation:** some AoE/DoT/environment kills may go uncredited when
`AttackerController` is null — no kill is counted in that case. Suicides and
environment deaths only increment the victim's death count. This is accepted, not
a bug to fix.

## Economy (no souls, no items)

Goal: everyone equal, abilities-only. Two complementary locks:

1. **Suppress soul/currency gain** — override `OnModifyCurrency(ModifyCurrencyEvent)`
   and return `HookResult.Stop` for gold/soul gains (see [[events-surface]]:
   `OnModifyCurrency` Stop blocks the currency change). This stops kills/orbs
   from ever paying out. Keep `StartingGold = 0`.
   - Alternative/explicit grant path if we ever want a fixed budget:
     `pawn.SetCurrency(ECurrencyType.EGold, N)` or
     `pawn.ModifyCurrency(type, amount, source)` from the spawn ritual.
2. **Block purchasing** — `OnModifyCurrency` returning `HookResult.Stop` leaves
   players with 0 souls so nothing is affordable. No need to intercept buy
   concommands. **Implementer: confirm the shop UI doesn't error or soft-lock when
   a buy is attempted with 0 gold.**
3. **Fixed loadout (optional)** — if `FixedItems` is non-empty, grant them on
   spawn via `pawn.AddItem(name, enhanced: false)` in the spawn ritual. Default
   is empty: abilities only.

Signature abilities are still maxed on spawn (`MaxUpgradeSignatureAbilities`
pattern from Deathmatch — set `UpgradeBits |= 0b11111` on `Signature1..4`) so
that with no item economy players still have a fully-realized kit.

## Cooldown reduction

Port Deathmatch's `ScaleAbilityCooldowns`, driven by `Timer.Every(1.Ticks(), …)`:

- For each player pawn, for each ability with `CooldownEnd - CooldownStart > 0`,
  shift the **whole window backward** by `(1 - CooldownScale) * duration`:
  ```
  shift = (1 - CooldownScale) * duration   // 0.5 → half the remaining CD
  ability.CooldownStart -= shift;
  ability.CooldownEnd   -= shift;
  ```
- Shifting both `Start` and `End` (not just lowering `End`) makes the reduction
  survive the game's `End = Start + vdataDuration` recomputation — this is the
  key gotcha documented in [[deathmatch]] "Cooldown scaling".
- Dedup with a `Dictionary<nint, (float Start, float End)>` keyed by
  `ability.Handle` and a mark-and-sweep `HashSet` so the 64 Hz tick doesn't
  re-write or allocate every frame.
- `CooldownScale` comes from config so the 50% is tunable without a rebuild.

## Win condition

- **Primary**: first player to `_kills[idx] >= KillTarget` wins.
- **Check timing**: evaluate inside `OnPlayerDeath` immediately after
  incrementing, so the winning kill ends the match at once.
- **Tiebreaker** (two players cross in the same processed frame — rare but
  possible with AoE multi-kills): rank by (1) total kills, then (2) fewest
  deaths, then (3) earliest to reach the target if timestamped, else (4) stable
  pick (lowest slot) with the tie noted in the announcement. Keep it simple; log
  the tie.
- Warmup ignores the check entirely (`phase != Active` → skip).

## End-of-game

On reaching the win condition:

1. Set `phase = GameOver`, `gameOverUntil = CurTime + PostGameSeconds`.
2. Announce via `CCitadelUserMsg_HudGameAnnouncement` (see [[netmessages-api]]):
   - Title: `"{WinnerName} WINS!"`
   - Description: top-3 scoreboard, e.g. `"1. Alice 30  2. Bob 24  3. Cara 19"`.
3. Also `Chat.PrintToChatAll` the full top-N so it persists in chat history.
4. Emit match-summary stats (see HUD/feedback + Deathmatch's `StatsClient`
   usage) if `StatsClient.Enabled`.
5. The per-tick pinner watches `CurTime >= gameOverUntil` → `ResetMatch()`:
   clear `_kills`, `_scores`, reset `phase` to `Warmup`/`Active` based on
   `humanCount`, announce the next match start. Players are kept; no map reload.

The whole time, **gameover suppression stays on** so the engine never shows its
own "team won" screen:

- `[GameEventHandler("gameover_msg")] → HookResult.Stop`
- `[GameEventHandler("round_end")] → HookResult.Stop`
- per tick, if `m_eGameState != GameInProgress`, force it back to
  `GameInProgress` (Deathmatch's `FreezeMatchClock` pattern).

Our FFA "GameOver" is purely a plugin-level scoreboard phase; the engine match
stays live the entire time.

## Chat commands

Registered with the unified `[Command]` attribute (see [[command-attribute]]).
Typed arg binding + `CommandException` for error replies.

| Command | Effect |
|---------|--------|
| `!help` | List FFA commands |
| `!score` | Print caller's own kills/deaths + current rank + kills-to-win remaining |
| `!top` | Print the top-5 leaderboard to the caller (or all) |
| `!hero <name>` | Queue a hero swap for next respawn — reuse `HeroSelect.FuzzyMatchHero` rather than copying the matcher (it's exposed `public static` for exactly this) |
| `!stuck` / `!suicide` | Kill self to respawn |

**`!stuck` decision**: do **not** hand-roll it. Either compose the standalone
`StuckCommand` plugin (preferred, mirrors `normal`/`trooper-invasion`) **or**, if
FFA has its own spawn-protection that zeroes suicide damage, keep a local variant
that clears protection before the lethal hit — which is precisely why Deathmatch
was *excluded* from `StuckCommand` (see index note). Since FFA *does* use spawn
protection, the same exclusion logic applies: **carry a local `!stuck` that
clears `_invulnerableUntil` + modifier states before `pawn.Hurt(999_999f)`**, and
do **not** also compose `StuckCommand` (double-registration would be absorbed).

## HUD / feedback

- **Match banners** — `CCitadelUserMsg_HudGameAnnouncement` for: match start,
  milestone leads ("Alice leads with 25!"), winner. Requires the
  `Google.Protobuf` package reference in the csproj (see
  [[plugin-build-pipeline]] / [[netmessages-api]]).
- **Kill feed / chat** — if `AnnounceKillsInChat`, `Chat.PrintToChatAll` a concise
  "Alice ▸ Bob" line per kill (throttle if it gets noisy at high player counts).
- **HUD clock** — pin it the same way Deathmatch does so the on-screen timer
  shows match elapsed time instead of free-running: per tick write
  `m_flGameStartTime`, `m_fLevelStartTime`, `m_flRoundStartTime`,
  `m_flMatchClockAtLastUpdate = elapsed`, `m_nMatchClockUpdateTick = TickCount`
  (all five together — missing `m_flRoundStartTime` was a real bug). Optionally
  repurpose the clock to count **down** progress, but elapsed is simplest.
- **Scoreboard** — the native scoreboard is team-based and will look odd in FFA
  (everyone on one engine team). The reliable feedback channel is chat + HUD
  announcements; treat the native scoreboard as best-effort. Known trade-off, not
  a blocker.
- **Stats** — follow Deathmatch/TrooperInvasion: a `StatsClient` with
  `Capture(event, hashedSteamId, props)` for `ffa_player_joined`,
  `ffa_player_killed`, `ffa_match_started`, `ffa_match_outcome` (winner, kills,
  duration, player count), gated on `StatsClient.Enabled`.

## Plugin composition (`gamemodes.json`)

Add a `"free-for-all"` entry. Starting from the `deathmatch` composition and
adjusting for FFA:

```json
"free-for-all": [
  "StatusPoker",
  "FreeForAll",
  "Hostname",
  "FlexSlotUnlock",
  "TeamChangeBlock",
  "HealOnSpawn",
  "DisconnectCleanup",
  "HeroSelectOnNextSpawn",
  "Feedback"
]
```

Rationale per plugin:

- **StatusPoker** — keepalive/status reporting; in every gamemode.
- **FreeForAll** — this plugin.
- **Hostname** — sets server name; ubiquitous.
- **FlexSlotUnlock** — gives full ability/flex slots without the item-kill
  objectives that normally unlock them; FFA has no objectives, so this is needed
  to make kits whole (same reason Deathmatch composes it). Re-applied per join.
- **TeamChangeBlock** — blocks `changeteam`/`jointeam` concommands so players
  can't self-sort; FFA controls team assignment server-side. (Note the caveat
  from [[deathmatch]]: this blocks *client* concommands, not the engine
  auto-balancer.)
- **HealOnSpawn** — full HP on spawn/hero-change, handling the "max health reads
  0 for several ticks" race so we don't have to re-implement the retry loop.
- **DisconnectCleanup** — removes pawn+controller on disconnect (managed
  `Remove()` path, see [[disconnect-cleanup]]). FFA still keeps its own per-slot
  state cleanup in `OnClientDisconnect`; this handles the engine-entity teardown.
- **HeroSelectOnNextSpawn** — lets `!hero` / the hero picker queue a swap applied
  on next death/respawn rather than mid-fight; this is the right variant for a
  fast-respawn mode. Provides `!hero`, so FFA needn't register its own. **Note:**
  if event-handler ordering causes a conflict with FFA's own `player_death`
  handler, FFA owns the hero-swap queue internally and `HeroSelectOnNextSpawn` is
  dropped from the composition. Default: try composing it first; implement
  in-plugin only if there's a conflict.
- **Feedback** — `!feedback` to admins; in every gamemode.

Deliberately **excluded**:

- **HeroSelect** (the alive-swap variant) — FFA wants the next-spawn variant
  instead; composing both would double-register `!hero`.
- **StuckCommand** — excluded for the same reason Deathmatch excludes it: FFA's
  spawn protection requires a protection-aware `!stuck`, so FFA carries its own.
  (If FFA ends up **not** using a damage-zeroing protection, prefer composing
  `StuckCommand` and dropping the local copy.)
- **LockTimer / TrooperInvasion / Deathmatch** — other gamemodes, mutually
  exclusive.

## Files (proposed layout)

Mirror TrooperInvasion's partial-class split if the single file grows past
~400 lines; otherwise start as one `FreeForAll.cs` like Deathmatch:

- `FreeForAll/FreeForAll.cs` — plugin class, config, lifecycle, state machine.
- `FreeForAll/FreeForAll.Spawns.cs` — walker capture + `PickSpawnPoint`.
- `FreeForAll/FreeForAll.Cooldowns.cs` — `ScaleAbilityCooldowns`.
- `FreeForAll/FreeForAll.Commands.cs` — chat commands.
- `FreeForAll/FreeForAll.Stats.cs` — `StatsClient` wrapper (port from Deathmatch).
- `FreeForAll/FreeForAll.csproj` — triple-mode csproj with the `Google.Protobuf`
  package reference for HUD announcements.

---

## Open questions

All eight original questions (Q1–Q8) have been resolved by design decision and
are now reflected in the relevant sections above. Resolved summary:

- **Q1** Single team (Amber) + `mp_friendlyfire 1`; visuals trade-off accepted.
- **Q2** Native scoreboard best-effort; chat + HUD are the real feedback channel.
- **Q3** No buy-concommand interception needed; `OnModifyCurrency` Stop suffices.
- **Q4** Null-attacker kills go uncredited — known limitation, not a bug.
- **Q5** `citadel_player_spawn_time_max_respawn_time` ConVar is primary; per-pawn `RespawnTime` reserved for per-player variation later.
- **Q6** Spawn protection keys off victim's `_invulnerableUntil`; team irrelevant. No design change needed.
- **Q7** `KillTarget = 30` default kept; player-count scaling deferred to a later tuning pass.
- **Q8** Try composing `HeroSelectOnNextSpawn` first; implement in-plugin if handler ordering conflicts.

**Remaining open item (blocks implementation):**

Confirm the exact ConVar name and behavior for friendly fire in Deadlock.
`mp_friendlyfire` is the Source/CS convention but Deadlock may use a different
ConVar or a different mechanism. Verify on a live server before implementing Q1.
