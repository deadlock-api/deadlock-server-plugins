# TeamDeathmatch — Product Plan

## Overview

**TeamDeathmatch (TDM)** is a classic 6v6 team-elimination gamemode for Deadlock.
Two teams (Amber = team 2, Sapphire = team 3) fight in a single continuous match
on `dl_midtown`. The first team to reach the kill limit (default 50) wins.
Respawns are instant or near-instant, there is no soul/item economy, and heroes
are fixed for the duration of the match. The mode is pure combat: no objectives,
no NPCs, no lanes.

**How this differs from the existing `Deathmatch` plugin:**

| Aspect | `Deathmatch` (existing) | `TeamDeathmatch` (this plugin) |
|--------|------------------------|-------------------------------|
| Structure | Rounds (3×180s) rotating an "active lane" across `{1,3,6}` | One continuous match, no rounds, no lanes |
| Win condition | None — match runs forever, per-round score banners only | First team to kill limit (or highest at time cap) — match actually **ends** |
| Spawn anchors | Walker (`npc_boss_tier2`) positions, bucketed per team **and lane** | Walker positions bucketed per team only (no lane dimension) |
| Economy | Floods gold (`SetCurrency EGold 999_999`), purchasing allowed anywhere | Suppressed — fixed starting state, no soul gain, no buying |
| Abilities | Sig abilities maxed, cooldowns scaled 50% | Sig abilities maxed (combat-ready); native cooldowns (`CooldownScale = 1.0`) |
| Gameover | Permanently suppressed (`EGameState.GameInProgress` pinned) | Suppressed throughout; our own HUD banner shown at win; engine gameover never fires |
| Hero choice | Auto-assigned least-present hero on join | Random (least-present picker, same as Deathmatch) at join; fixed for the match |
| Lifecycle | Stateless infinite loop | Warmup → Active → GameOver → reset |

The existing `Deathmatch` plugin is the closest reference implementation and most
of the low-level mechanics (Walker capture, spawn protection, HUD clock anchor,
team-picker bypass, anti-camp spawn scoring) are reused directly. See
[[deathmatch]] in the wiki for the battle-tested patterns.

---

## Game loop

```
        players present
  ┌──────────────────────┐
  │                      ▼
WARMUP ──countdown──▶ ACTIVE ──kill limit OR time cap──▶ GAMEOVER ──reset──▶ WARMUP
  ▲                                                                            │
  └────────────────────────────────────────────────────────────────────────┘
```

1. **Warmup** — server idle or filling. Heroes are picked (random/vote), teams
   balanced. Players can move and shoot but kills do **not** count toward the
   limit. A countdown starts once both teams have ≥1 player (configurable
   minimum). Ends by countdown expiry → Active.
2. **Active** — the real match. Kill counters increment, kill feed prints, score
   announcements fire periodically. Ends when a team reaches `KillLimit`, or when
   `MatchTimeLimit` elapses (fallback) → GameOver.
3. **GameOver** — winner + top fragger announced, score frozen, engine gameover
   screen allowed to display for `PostGameSeconds`, then full reset → Warmup.

---

## Configuration

`TeamDeathmatchConfig` (`[PluginConfig]`, hot-reloadable via `OnConfigReloaded`).
Note: the host contract **requires** the config class to exist even if empty
(see [[deathmatch]] — `DeathmatchConfig` is required, not dead code).

| Field | Type | Default | Meaning |
|-------|------|---------|---------|
| `KillLimit` | `int` | `50` | Team kills to win. |
| `RespawnDelaySeconds` | `float` | `2.0` | Delay from death to respawn. `0` = instant. |
| `MatchTimeLimitSeconds` | `float` | `900` (15 min) | Fallback cap; highest kills wins on expiry. `0` = no cap. |
| `SpawnProtectionSeconds` | `float` | `3.0` | Post-spawn invulnerability window. |
| `WarmupCountdownSeconds` | `float` | `15` | Countdown once min players present. |
| `MinPlayersPerTeam` | `int` | `1` | Teams needed before the warmup countdown starts. |
| `PostGameSeconds` | `float` | `15` | How long the win/end screen stays up before reset. |
| `ScoreAnnounceIntervalSeconds` | `float` | `30` | Periodic chat score broadcast cadence. |
| `HeroSelectionMode` | `enum` | `Random` | `Random` only for v1. Vote is out of scope. |
| `CooldownScale` | `float` | `1.0` | `1.0` = native cooldowns (TDM is about coordination, not spam). Operators may lower it; `0.5` mirrors Deathmatch. |

---

## State machine

```
enum Phase { Warmup, Active, GameOver }
```

| From | Event / condition | To | Side effects |
|------|-------------------|----|--------------|
| (load) | `OnStartupServer` | Warmup | strip NPCs, capture Walkers, pin clock, reset counters |
| Warmup | both teams ≥ `MinPlayersPerTeam` | Warmup (armed) | start countdown timer |
| Warmup | countdown reaches 0 | Active | zero kill counters, announce "FIGHT", record match start time |
| Warmup | player count drops below min | Warmup (disarmed) | cancel countdown |
| Active | `team kills ≥ KillLimit` | GameOver | freeze, compute winner + top fragger |
| Active | `match elapsed ≥ MatchTimeLimit` | GameOver | winner = higher kills (draw possible) |
| Active | all human players leave | Warmup | abandon, reset counters, emit session-abandoned stat |
| GameOver | `PostGameSeconds` elapsed | Warmup | full reset (clock, counters, hero re-pick on next warmup) |

The phase variable gates every per-tick and event handler: kill counting only in
`Active`; spawn rituals always; score announcements only in `Active`.

**Clock handling.** Borrow the Deathmatch HUD-clock anchor (write
`m_flGameStartTime`, `m_fLevelStartTime`, `m_flRoundStartTime`,
`m_flMatchClockAtLastUpdate`, `m_nMatchClockUpdateTick` together every tick — see
[[deathmatch]] "HUD match clock anchor"). For TDM the clock counts down from
`MatchTimeLimit`. Pin `EGameState.GameInProgress` throughout all phases — the
engine gameover is permanently suppressed (see End-of-game).

---

## Spawning

Reuse Deathmatch's Walker-based spawn capture, **minus the lane dimension**:

- At `OnStartupServer`, iterate `Entities.All`; collect `npc_boss_tier2` (Walker)
  positions keyed by `ent.TeamNum` only. Also handle late spawns in
  `OnEntitySpawned`. (Deathmatch's `_walkersByTeamLane` collapses to
  `_walkersByTeam`.)
- Strip all map NPCs (`npc_boss_tier1/2/3`, `npc_barrack_boss`,
  `npc_base_defense_sentry`, `npc_trooper_boss`) so the map is pure PvP. Walkers
  are removed visually via `ent.Remove()` (same as Deathmatch removes combat NPCs);
  their captured positions are retained in memory as spawn anchors.

**Anti-camp spawn logic** — reuse `PickSpawnPoint` / `ScoreCandidate` from
Deathmatch wholesale:

- Candidate set = own team's Walker positions.
- Reject candidates in any enemy's **line of fire** (20° cone, 2500u range,
  visibility ray via `Trace.Ray`) when a non-camped alternative exists.
- Score remaining candidates by: distance from death position, trapezoidal
  proximity to nearest enemy (ideal 1500–3500u), proximity to map center,
  off-axis angle from enemy aim.
- Pick randomly among the top 3 to avoid deterministic spawn-camping.

On spawn apply a **spawn ritual** (TDM variant of `ApplySpawnRitual`):
- Max signature abilities (`MaxUpgradeSignatureAbilities`) so heroes are
  combat-ready without an economy.
- Grant spawn protection (invuln + bullet-invuln modifiers for
  `SpawnProtectionSeconds`, tracked in `_invulnerableUntil`).
- Heal to full — **note the timing gotcha**: `GetMaxHealth()` returns 0 for
  several ticks after `player_respawned`/`player_hero_changed`; use the retry
  loop pattern (`Timer.Once(1.Ticks(), …)` up to ~20 ticks, re-resolving the pawn
  by `EntityIndex` each tick). See [[deathmatch]] "Healing after respawn".
- **Do NOT** flood gold — see Economy suppression.

---

## Kill tracking

Hook `player_death` (`[GameEventHandler("player_death")]`, `PlayerDeathEvent`):

- A kill counts only if `Phase == Active` **and** it is a real cross-team kill:
  `attacker != victim` and `attackerPawn.TeamNum != victimPawn.TeamNum`
  (mirrors the `scored` guard in `Deathmatch.OnPlayerDeath`).
- Increment `_teamKills[attackerTeam]` and per-player `_killsByEntity[attackerIdx]`
  (the latter drives the top-fragger award and `!score`).
- Store `_lastDeathPos[victimIdx]` from `VictimX/Y/Z` for the anti-camp respawn.
- After each scored kill: check win condition (`_teamKills[t] >= KillLimit`).

**Kill-feed chat messages.** On each scored kill, print to all:
`[TDM] <Killer> (Amber) killed <Victim> (Sapphire)  —  Amber 23 - 19 Sapphire`
via `Chat.PrintToChatAll`. Keep it terse to avoid spam; the running score in the
suffix doubles as a lightweight live scoreboard. (Optional: suppress per-kill
lines and rely only on periodic announcements — open question on spam tolerance.)

---

## Economy suppression

The goal is a fixed starting state: no soul gain, no item purchases, no level-ups
driving stat creep.

Layers (apply all; belt-and-suspenders):

1. **Disable NPC spawns** — `ConVar.Find("citadel_npc_spawn_enabled")?.SetInt(0)`
   (Deathmatch does this), removing trooper/jungle soul sources.
2. **Block currency gain** — override `OnModifyCurrency(ModifyCurrencyEvent)` and
   return `HookResult.Stop` for soul/gold gains (the hook exposes slot, type,
   amount, source — see [[events-surface]]). Primary suppress layer. Belt-and-suspenders:
   also set starting gold to 0 on spawn. Implementer should verify no soul leaks on
   a live server.
3. **Block purchasing** — do **not** set `citadel_allow_purchasing_anywhere`.
   Zero souls from (2) make shops effectively dead; no further action needed.
4. **Fixed loadout** — bare heroes + `MaxUpgradeSignatureAbilities` on spawn. No
   fixed item set (`HeroItemSets.jsonc`). Heroes are combat-ready without an economy.

---

## Respawn

- Hook `player_death`; schedule respawn after `RespawnDelaySeconds`.
- Belt-and-suspenders respawn timing: set `citadel_player_spawn_time_max_respawn_time = 2`
  at startup (proven Deathmatch approach), **and** set `CCitadelPlayerPawn.RespawnTime`
  on `player_death` (added in Deadworks v0.4.8 — see [[deadworks-0.4.8-release]]) for
  per-player control.
- On `player_respawned` (`PlayerRespawnedEvent`), teleport the pawn to the
  anti-camp spawn point (`pawn.Teleport(position: …)`) and apply the spawn ritual.
- `player_respawned` fires on **both** first spawn and respawn — distinguish via
  presence in `_lastDeathPos` (Deathmatch pattern) if first-spawn placement needs
  to differ.

Default is `2s` — reduces spawn-frag chaos, gives the kill feed time to read. Configurable.

---

## Win condition

- **Primary:** first team whose `_teamKills` reaches `KillLimit` wins. Checked
  after every scored kill in `Active`.
- **Fallback (time cap):** if `MatchTimeLimitSeconds > 0` and elapsed match time
  reaches it, the team with more kills wins; equal kills → draw.
- Transition to `GameOver`, freeze counters, stop counting further kills.

---

## End-of-game

On entering `GameOver`:

1. Compute winner (team) and **top fragger** (max `_killsByEntity`, resolve name
   via `CBaseEntity.FromIndex<CCitadelPlayerController>`).
2. Announce via `CCitadelUserMsg_HudGameAnnouncement` (requires `Google.Protobuf`
   PackageReference in the csproj — see [[deathmatch]] / [[plugin-build-pipeline]]):
   - Title: `"Amber Team Wins!"` / `"Sapphire Team Wins!"` / `"Draw!"`
   - Description: `"Amber 50 - 41 Sapphire  |  Top fragger: <name> (17)"`
3. Also `Chat.PrintToChatAll` the same summary for persistence in the chat log.
4. **Keep gameover suppressed:** continue pinning `EGameState.GameInProgress` and
   suppressing `gameover_msg`/`round_end` — the engine kicking players or changing
   level is not worth the risk. The `CCitadelUserMsg_HudGameAnnouncement` banner
   (step 2) is the win screen. This is the proven Deathmatch approach.
5. After `PostGameSeconds`, **reset**: zero `_teamKills` / `_killsByEntity`,
   clear `_lastDeathPos` / `_invulnerableUntil`, re-pin the clock, return to
   Warmup, re-pick heroes for the next match.

---

## Chat commands

Use the unified `[Command]` attribute (typed arg binding, `CommandException` for
errors — see [[command-attribute]]). Note `Chat.PrintToChat(slot, …)`.

| Command | Behavior |
|---------|----------|
| `!score` | Print current score + each team's kill count + caller's personal kills + kills remaining to win. |
| `!stuck` / `!suicide` | Force-kill self to respawn. Must clear spawn protection first (remove from `_invulnerableUntil`, clear `Invulnerable`/`BulletInvulnerable` modifiers) **before** `pawn.Hurt(999_999f)`, or the damage is absorbed (Deathmatch gotcha). |
| `!help` | List available commands. |

Note from [[stuck-command]]: a standalone `StuckCommand` plugin exists and is
composed into other gamemodes, but was **deliberately not added to Deathmatch**
because Deathmatch needs the spawn-protection-clearing variant. TDM ships its own
`!stuck` for the same reason — composing both would double-register.

---

## HUD / feedback

- **Periodic score announcement:** `Timer.Every(ScoreAnnounceIntervalSeconds)`
  during `Active` → `CCitadelUserMsg_HudGameAnnouncement` and/or
  `Chat.PrintToChatAll` with `Amber X - Y Sapphire | first to KillLimit`.
- **Kill feed:** per-kill chat line (see Kill tracking).
- **Milestone callouts:** when a team crosses e.g. 75% / 90% of `KillLimit`,
  emit a louder HUD banner ("Amber needs 5 more!"). Optional.
- **Match clock:** count down `MatchTimeLimit` via the schema-write anchor
  pattern so the in-world HUD clock reflects remaining time.
- **Warmup countdown:** HUD banner ticking down to match start.

---

## Plugin composition (`gamemodes.json`)

Add a `team-deathmatch` gamemode entry that composes this plugin. Following the
build pipeline (see [[plugin-build-pipeline]] / [[docker-build]]), each gamemode
is a list of plugin folders built into a server image.

Composition:

```json
"team-deathmatch": [
  "StatusPoker",
  "TeamDeathmatch",
  "Hostname",
  "FlexSlotUnlock",
  "TeamChangeBlock",
  "HealOnSpawn",
  "DisconnectCleanup",
  "Feedback"
]
```

Notes: no `StuckCommand` (TDM ships its own spawn-protection-aware `!stuck`); no
`HeroSelect` (TDM assigns heroes at join); `HealOnSpawn` handles the respawn heal
retry loop. The csproj should follow the triple-mode pattern from
[[plugin-build-pipeline]].

**Team-picker bypass & team locking** (reuse Deathmatch pattern, see
[[deathmatch]] "Team picker bypass"): assign teams server-side via
`controller.ChangeTeam(int)` in `OnClientFullConnect` (bypasses the client
picker), and block `changeteam`/`jointeam`/`selecthero`/`citadel_hero_pick`
concommands via `OnClientConCommand` returning `HookResult.Stop` (heroes are
fixed once assigned at join). Balance teams by count.

---

## Resolved design decisions

All pre-implementation questions are closed. Decisions recorded here for reference:

1. **Hero selection:** Random for v1 — reuse Deathmatch's least-present picker at join. Vote is out of scope.
2. **Hero locking:** Fixed for the match. Block `selecthero`/`citadel_hero_pick` via `OnClientConCommand` returning `HookResult.Stop`. No re-pick on death.
3. **Cooldown scaling:** None — `CooldownScale = 1.0` (native). TDM is about team coordination and positioning; Deathmatch's 50% scaling would make it too chaotic. Config field retained for operators.
4. **Economy depth:** Bare heroes + `MaxUpgradeSignatureAbilities` on spawn. No fixed item set, no gold.
5. **Engine end screen:** Keep suppressed. Do not let `m_eGameState` advance or `gameover_msg`/`round_end` fire — risk of engine kicking players is not acceptable. Show `CCitadelUserMsg_HudGameAnnouncement` win banner instead; after `PostGameSeconds` the plugin resets to Warmup. Proven Deathmatch approach.
6. **Respawn delay:** `RespawnDelaySeconds = 2.0` default.
7. **`RespawnTime` reliability:** Belt-and-suspenders — set `citadel_player_spawn_time_max_respawn_time = 2` at startup (proven), and set `CCitadelPlayerPawn.RespawnTime` on `player_death` for per-player control.
8. **Walkers:** Remove visually via `ent.Remove()`. Positions captured first and retained in memory as spawn anchors.
9. **`OnModifyCurrency` sufficiency:** Primary suppress via `HookResult.Stop`. Belt-and-suspenders: set starting gold to 0 on spawn. Implementer verifies no soul leaks on live server.
10. **Map:** `dl_midtown` only. Walker capture assumes midtown.
11. **Min players / bots:** Hold in Warmup with no bots. Start when `WarmupCountdownSeconds` expires even if teams are uneven — uneven teams are acceptable.
12. **Stats:** Out of scope for v1.

---

*References: [[deathmatch]], [[events-surface]], [[command-attribute]],
[[netmessages-api]], [[deadworks-0.4.8-release]], [[stuck-command]],
[[disconnect-cleanup]], [[plugin-build-pipeline]], [[schema-accessors]],
[[trace-api]].*
