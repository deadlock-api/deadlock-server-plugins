# HordeSurvival — Product Plan

> Status: design / implementation-ready. No code written yet.
> Direct predecessor: **TrooperInvasion** (`../TrooperInvasion/`). Read that
> plugin's wiki page (`knowledge-base/wiki/plugins/trooper-invasion.md`) before
> implementing — most of the engine-level gotchas it documents apply verbatim here.

---

## 1. Overview

**HordeSurvival** is a co-op PvE survival gamemode: every human is forced onto a
single team (Amber, team 2) and together they defend their lane objectives
against escalating waves of engine-spawned enemy NPCs (Sapphire, team 3). It is
a fork of **TrooperInvasion** and reuses its hardest-won engine integration:
engine-driven trooper spawning via `citadel_trooper_*` ConVars, deferred
`OnEntitySpawned` culling, the alive-trooper reconciler, self-spawned tier1 lane
Guardians, the HUD-announcement wrapper, and `changelevel`-based world reset.

**What changes vs TrooperInvasion** (the diff that makes this a distinct mode):

| Aspect | TrooperInvasion | HordeSurvival |
|---|---|---|
| Cadence | Continuous pressure — waves overlap, spawn window opens/closes on a rolling timer, no breathing room | **Discrete waves** separated by an explicit **30s Prep Phase** buy window |
| Wave clear | No "clear" concept — waves just keep coming until a round of 10 ends | **Wave Cleared** is a first-class state: the next wave does not spawn until the current wave is fully dead |
| Scaling | Health scales by round/wave; volume scales by player count | Health **and count** scale jointly on `(waveNumber, playerCount)` with explicit formulas |
| End condition | Survive 10 waves per round, loop forever; lose if Patron dies | **Win** by surviving N waves (configurable) **or Endless mode** (no win; score = waves survived); lose if a defended objective falls |
| Friendly fire | Vanilla (allies can't damage allies on the same team) | **Friendly knockback** — friendly fire applies a non-lethal positional impulse but deals no damage |
| Objective | Patron (`npc_boss_tier3`) is the lose anchor | **Walkers** (`npc_boss_tier2`) are passive defended objectives; lose if they (or the Patron) fall |
| Income | Starter gold once per slot + per-wave catch-up | Same, **plus** souls/gold flow freely during Prep Phase |

The mode is registered as a new gamemode service `horde-survival` and composed
from the same supporting plugins as `trooper-invasion`.

---

## 2. Game loop

```
        ┌─────────────────────────────────────────────────────────────┐
        │                                                               │
        ▼                                                               │
  ┌───────────┐   first player joins   ┌────────────┐   timer expires  │
  │  Warmup   │ ─────────────────────► │ Prep Phase │ ───────────────┐ │
  └───────────┘                        │   (30s)    │                │ │
        ▲                              └────────────┘                ▼ │
        │ last player leaves                                  ┌────────────┐
        │ (full reset)                                        │ Wave Active│
        │                                                     └────────────┘
        │                                                            │
        │                                              all enemies dead
        │                                                            ▼
  ┌───────────┐   survived N waves   ┌──────────────┐      ┌──────────────┐
  │ GameOver  │ ◄─────────────────── │ Wave Cleared │ ◄────│ (reconciler) │
  │(win/lose) │   OR objective fell  └──────────────┘      └──────────────┘
  └───────────┘            │                 │
        │                  │                 │ more waves remain (or endless)
        │ changelevel      │                 └──────────────► back to Prep Phase
        └──────────────────┘
```

1. **Warmup** — empty/idle server. No waves. First `OnClientFullConnect` arms
   the loop and transitions to Prep Phase (idempotent, exactly like TI's
   `ArmWaves`).
2. **Prep Phase** — 30s buy window. Soul income on, free repositioning, HUD
   countdown. Spawn objectives (Guardians) ensured here on the first cycle.
3. **Wave Active** — enemy troopers spawn for this wave's count, then the spawn
   window closes. Players fight until every enemy is dead.
4. **Wave Cleared** — detected by the reconciler reaching zero alive enemies.
   Brief banner, then either loop back to Prep Phase (next wave) or GameOver
   (survived the final wave in non-endless mode).
5. **GameOver** — win or lose. HUD toast, post-mode cooldown, `changelevel`
   reset (TI's proven full-reset path).

---

## 3. Configuration

All tunables live in a `HordeTuning` static class (mirrors `WaveTuning` — pure
functions of `(waveNumber, playerCount)`, no state) plus a small
`HordeSurvivalConfig` `[PluginConfig]` for operator-facing toggles.

### Operator config (`HordeSurvivalConfig`, JSONC, hot-reloadable)

| Key | Default | Meaning |
|---|---|---|
| `Endless` | `false` | If true, no win condition; play until an objective falls. Score = waves survived |
| `WaveCount` | `20` | Non-endless: survive this many waves to win |
| `PrepDurationSeconds` | `30` | Buy-window length between waves |
| `FirstPrepSeconds` | `45` | Longer first prep so players can buy a starting build |
| `FriendlyKnockbackForce` | `600` | Impulse magnitude applied to a friendly-fire victim (units/s); `0` disables |
| `RespawnDuringWave` | `true` | If false, deaths during a wave wait until the next Prep Phase to respawn |

### Difficulty constants (`HordeTuning`)

```
BaseEnemyCount        = 6      // wave-1, single-player baseline enemy count
CountPerWave          = 0.20   // +20% enemies per wave number
CountPerExtraPlayer   = 0.25   // +25% enemies per player beyond the first
MaxEnemyCount         = 600    // hard cap (matches WaveTuning.MaxTrooperCap)

BaseHealthScale       = 1.0
HealthPerWave         = 0.15   // +15% enemy HP per wave number
HealthPerExtraPlayer  = 0.10   // +10% enemy HP per player beyond a 4-player baseline
MaxHealthScale        = 24.0   // matches WaveTuning.MaxHealthScale

MaxSquadSize          = 8      // engine hard cap — DO NOT exceed (see TI note)
```

### Scaling formulas

Player count `p` is `HumanPlayerCount()` (the `_humanCount` counter maintained
in `OnClientFullConnect`/`OnClientDisconnect`, never `Players.GetAll().Count()`
in a hot path — see TI).

```
enemyCount(w, p)  = clamp( round( BaseEnemyCount
                                  * (1 + CountPerWave * (w - 1))
                                  * (1 + CountPerExtraPlayer * (p - 1)) ),
                           BaseEnemyCount, MaxEnemyCount )

healthScale(w, p) = min( MaxHealthScale,
                         BaseHealthScale
                         + HealthPerWave * w
                         + HealthPerExtraPlayer * max(0, p - 4) )
```

The health formula is the example the brief gave, adapted to the existing
`ScaleTrooperHealth` hook point. `enemyCount` replaces TI's continuous
`burstSeconds × lanes × squad` volume model with an explicit per-wave target
(see §5/§6). Active lanes still scale via `HordeTuning.ComputeActiveLanes(p)`
reusing TI's `Clamp(p/2, 1, 3)` and the `{1,4,6}` lane bitmask — there are still
only 3 lanes in Deadlock (see TI wiki "Lane gating").

---

## 4. State machine

Single `enum HordePhase { Warmup, PrepPhase, WaveActive, WaveCleared, GameOver }`
field replaces TI's looser `_wavesActive`/`_modeOver` booleans. The reconciler and
spawn logic gate on the phase rather than on two booleans.

| State | Entered when | On entry | Exit |
|---|---|---|---|
| **Warmup** | startup, or last player leaves | spawn off, cull, counters reset (`OnStartupServer` is the single re-init path) | first `OnClientFullConnect` → PrepPhase |
| **PrepPhase** | wave cleared (and waves remain) or first arm | soul income on, HUD "PREP — Wave N in 30s" countdown, ensure Guardians (first cycle only) | prep timer expires → WaveActive |
| **WaveActive** | prep timer expires | compute `enemyCount`/`healthScale`/lanes, open spawn window, set per-wave ConVars, HUD "WAVE N" | reconciler hits 0 alive enemies → WaveCleared |
| **WaveCleared** | reconciler 0-alive | HUD "WAVE N CLEARED", award clear bonus | waves remain → PrepPhase; final wave & non-endless → GameOver(win) |
| **GameOver** | objective destroyed, or final wave cleared | HUD win/lose, emit stats, post-mode cooldown | `changelevel` → Warmup via fresh `OnStartupServer` |

Transitions out of any non-GameOver state to **GameOver(lose)** fire the instant
a defended objective is destroyed (§9). All scheduled timers are tracked in
`IHandle` fields (`_pendingPhaseTimer`, `_pendingSpawnEnd`) and cancelled on
every transition — TI's stacked-timer guard pattern carries over directly.

---

## 5. Enemy spawning

**Reuse TI's engine-driven approach. Do NOT manually `CreateByDesignerName` enemy
troopers** — TI documents that `CreateByDesignerName("npc_trooper_boss") + Spawn()`
crashes the server natively on the first spawn (lane/squad AI init dereferences a
null pointer with no KV). Enemy volume comes from the engine spawner toggled by
ConVar.

- **Spawn window.** `SetSpawnEnabled(true/false)` wraps
  `Server.ExecuteCommand("citadel_trooper_spawn_enabled …")`. Runtime ConVar
  writes **must** go through `Server.ExecuteCommand`, never
  `ConVar.Find().SetInt` (crashes mid-game — see TI "ConVar mutation" note).
  Startup-only ConVars are set in `OnStartupServer` exactly as TI does.
- **Per-wave ConVars** set on entering WaveActive: `citadel_trooper_squad_size`
  (pinned at `MaxSquadSize = 8`), `citadel_trooper_gold_reward`,
  `citadel_active_lane` (lane bitmask from `ComputeActiveLanes(p)`).
- **Count control — the key difference from TI.** TI closes its spawn window on a
  timed `burstSeconds`. HordeSurvival instead closes it the moment
  `_aliveEnemyTroopers.Count` reaches this wave's `enemyCount(w, p)` target.
  `OnEntitySpawned` already increments the alive set and already calls
  `SetSpawnEnabled(false)` when a cap is hit — point that cap at the per-wave
  `enemyCount` target instead of TI's static `ComputeTrooperCap`. A safety
  `_pendingSpawnEnd` timer (e.g. 15s) force-closes the window if the engine
  under-delivers, so a wave can never hang open.
- **Health scaling.** `ScaleTrooperHealth` on spawn, using `healthScale(w, p)`
  from §3 (TI sets both `MaxHealth` and `Health` because `m_iHealth` doesn't
  auto-clamp).
- **Friendly (team-2) troopers** are still culled via deferred `Remove()`
  (no per-team spawn ConVar exists). Deferral is load-bearing — a synchronous
  `Remove()` in `OnEntitySpawned` AVs under horde load.
- **Guardians** (tier1 lane bosses) are self-spawned exactly as in
  `TrooperInvasion.Guardians.cs` — same six specs, same `_guardianIndices`
  exemption (they report `DesignerName == "npc_trooper_boss"`), spawned once per
  map from the first Prep Phase (not a startup timer — timers don't tick while
  the server hibernates empty).

---

## 6. Wave composition

| Wave band | Enemy mix | Source |
|---|---|---|
| Early (1–3) | Regular troopers only (`npc_trooper`), small `enemyCount`, onboarding ramp | engine spawner |
| Mid (4+) | Regular troopers + naturally-promoted super-troopers (`npc_trooper_boss`) via `citadel_super_trooper_gold_mult` | engine progression |
| Optional miniboss waves (every 5th, e.g. 5/10/15) | As above + a flagged HUD callout; tougher `healthScale` | tuning only |

- **Enemy types.** Same two designers TI recognizes: `npc_trooper` and
  `npc_trooper_boss` (`IsTrooperDesigner`). No manual boss spawning. If a
  dedicated boss is ever wanted, use the native cheat concommand
  `citadel_spawn_trooper x,y,z boss` bracketed by `sv_cheats 1/0` (TI's
  documented safe path), **not** the managed entity API.
- **Spawn positions.** Engine lane spawners (`info_trooper_spawn`) at the
  Sapphire (team 3) end of each active lane — identical to TI; no custom spawn
  points.
- **Count per wave / player.** `enemyCount(w, p)` from §3. Onboarding ramp for
  waves 1–3 (reuse TI's `ComputeBurstSeconds` ramp factors `0.35/0.55/0.8`
  applied to the count target so wave 1 stays tiny even at high player counts).
- **Health per wave / player.** `healthScale(w, p)` from §3.
- **Lanes.** `ComputeActiveLanes(p) = Clamp(p/2, 1, 3)`, OR'd from `{1,4,6}`.

Example counts (post-ramp, rounded):

| Players | Wave 1 | Wave 5 | Wave 10 | Wave 20 |
|---|---|---|---|---|
| 1 | ~2 | ~10 | ~17 | ~29 |
| 4 | ~4 | ~22 | ~39 | ~67 |
| 8 | ~8 | ~44 | ~78 | ~135 |

---

## 7. Prep phase

- **Timer.** `PrepDurationSeconds` (default 30; first prep `FirstPrepSeconds`
  default 45), tracked in `_pendingPhaseTimer`.
- **Soul income.** During Prep the goal is for souls/gold to flow freely so
  players can build. Concretely:
  - Keep `citadel_allow_purchasing_anywhere = 1` (set at startup, as TI does) so
    the shop is reachable anywhere during the breather.
  - Grant a flat **prep stipend** per player on entering Prep Phase via
    `pawn.ModifyCurrency(ECurrencyType.EGold, amount, …)`. Formula:
    `200 + 50 × waveNumber` gold per player. This is layered on top of
    TI's once-per-slot starter gold and already-earned wave bounties.
- **What players can do.** Buy/upgrade items and abilities through the normal UI,
  reposition freely, regroup at chokepoints. No enemies spawn during Prep.
- **Countdown announcement.** HUD toast on Prep entry ("PREP — Wave N starts in
  30s, spend your souls"), chat reminders at T-10 and T-5, and a final HUD
  "WAVE N" on transition. Reuse `AnnounceHud(title, description)` for the
  boundary toasts; chat for the mid-countdown ticks (TI keeps the HUD uncluttered
  by routing per-tick events to chat).

---

## 8. Wave clear detection

Reuse TI's alive-trooper tracking verbatim:

- `_aliveEnemyTroopers` — `HashSet<int>` keyed by `EntityIndex`. Added in
  `OnEntitySpawned` (enemy team-3 trooper), removed in `OnEntityDeleted`.
- `ReconcileAliveTroopers()` sweeps the set for entries that are gone / dead /
  no-longer-enemy via `CBaseEntity.FromIndex(idx)?.IsAlive` — this catches dying
  troopers that linger as live entities for a tick before `OnEntityDeleted`
  fires (TI's "dead-trooper lingering" bug fix).
- **Clear trigger.** Unlike TI (which only reconciles before a `RunWave` cap
  check), HordeSurvival must detect *zero alive* to advance.
  - **Authoritative path:** a `Timer.Every(0.5s)` reconcile+check gated on
    `phase == WaveActive && spawnWindowClosed`. The poll is the source of truth —
    it is robust against missed `OnEntityDeleted` events (super-trooper promotion
    and end-of-lane despawn both bypass it per TI).
  - **`OnEntityDeleted`-driven path:** a nice-to-have optimization (avoids the
    half-second lag on the final kill) but not the primary mechanism. Add it later
    if the poll latency is noticeable in play.

---

## 9. Walker defense (objective)

Walkers (`npc_boss_tier2`) are the **passive defended objective**. They are
map-placed on `dl_midtown` (3 per team), sit in their lanes, and do not move.
Enemy troopers path down the lanes toward the Amber side and attack the Amber
(team-2) Walkers; players must intercept them.

- **Lose anchor.** A team-2 Walker being destroyed is a loss trigger. The Amber
  Patron (`npc_boss_tier3`) falling is also a loss (defense-in-depth: if all
  Walkers fall and troopers reach the Patron).
  - **Decision: Option A** — lose the instant the **first** team-2 Walker is
    destroyed. Clean, makes every lane matter. Configurable via `LoseOnFirstWalker`
    bool (default `true`) so operators can later switch to Option B (all Walkers +
    Patron fallen) without a code change.
- **Detection.** `OnEntityKilled` already fires for sub-objectives. Filter
  `IsGuardianDesigner(killed.DesignerName)` (which is `npc_boss_tier2 ||
  npc_barrack_boss`) and `killed.TeamNum == HumanTeam` → trigger GameOver(lose).
  - **Carry over TI's two Patron subtleties** since the Patron is also a lose
    anchor here: (1) the two-phase Patron death (only the 2nd real death is the
    true end — `npc_boss_tier3` drops a Shrine and revives), and (2) the
    scripted-weaken absorb window. If HordeSurvival ends on the **Walker**
    instead, the Patron machinery can be simplified — but keep the Patron-death
    handler as a secondary lose path. Flag the Walker-as-lose-anchor behavior as
    **needs in-game verification** (does a Walker death fire the same scripted
    weaken hit? — see TI's unverified caveat).
- **Healing / no-respawn for objectives.** Walkers do not respawn within a
  session (like Guardians, they're map/once-per-load entities). Their attrition
  *is* the tension. No plugin-side Walker healing in the base design.

---

## 10. Difficulty scaling (summary)

Covered by the formulas in §3. Two independent axes, both functions of
`(waveNumber, playerCount)`:

- **Health:** `healthScale(w, p) = min(24, 1 + 0.15·w + 0.10·max(0, p-4))`,
  applied in `ScaleTrooperHealth` on each enemy spawn.
- **Count:** `enemyCount(w, p) = clamp(round(6 · (1+0.20·(w-1)) · (1+0.25·(p-1))),
  6, 600)`, used as the per-wave alive-cap that closes the spawn window.
- **Lanes / cadence:** lanes via `Clamp(p/2,1,3)`; intra-wave spawn interval kept
  at TI's `citadel_trooper_spawn_interval_* = 1` so count, not cadence, is the
  knob.

These reuse `WaveTuning`'s constant style (`MaxHealthScale = 24`, squad cap 8,
trooper cap 600) so the two modes stay balance-comparable.

---

## 11. Friendly knockback

Goal: friendly fire between two team-2 players deals **no damage** but applies a
**non-lethal positional impulse** to the victim — rewarding spacing discipline
without enabling teamkills.

- **Hook.** `OnTakeDamage(TakeDamageEvent)`. Identify friendly fire:
  `args.Entity` is a `CCitadelPlayerPawn` on `HumanTeam` **and**
  `args.Info.Attacker` is a `CCitadelPlayerPawn` on `HumanTeam` **and** attacker
  ≠ victim (don't knock back self-damage).
- **Action.** Return `HookResult.Stop` to block the damage entirely (proven
  pattern — TI uses `Stop` to swallow lethal Patron hits), then apply an impulse
  to the victim along the hit direction scaled by `FriendlyKnockbackForce`.
- **Impulse API.** Write the victim's velocity directly via the
  `m_vecAbsVelocity` (or `m_vecVelocity`) schema accessor on the pawn. Fallback
  if no velocity accessor is available: `Teleport` with a small positional nudge
  (crude — ignores physics — but always works). The implementer picks whichever
  surface exists in `DeadworksManaged.Api`; the `Teleport` fallback is the
  guaranteed floor.
  Direction = normalized (victim.origin − attacker.origin), optionally with a
  small upward component so players get a readable "shove". Magnitude clamped so
  it can never fling a player off the map.
- **Prerequisite — team damage ConVar.** Deadlock teammates normally cannot
  damage each other, so friendly-fire events will not reach `OnTakeDamage`
  without enabling team damage via `mp_friendlyfire` (or its Deadlock/`citadel_*`
  equivalent). This ConVar must be confirmed available at runtime.
  - **If the ConVar is unavailable:** the friendly-knockback feature is **dropped
    entirely**. Replace it with a proximity-based "bump": a `Timer.Every(0.1s)`
    that checks pawn pair distances while in WaveActive; when two friendly pawns
    overlap (distance < hull radius, ~50 units), apply a separation impulse to
    each. No damage event needed. This is the explicit fallback and requires no
    engine team-damage support.

---

## 12. Endless mode

- **No win.** When `Endless = true`, WaveCleared always loops back to PrepPhase;
  there is no `WaveCount` terminus.
- **Score = waves survived** (`_waveNum` at the moment an objective falls).
- **Record announcement.** On GameOver, compare `_waveNum` to a tracked best:
  - **In-memory best** within the server's lifetime (trivial, always works) —
    HUD "NEW RECORD — Wave N!" when beaten, else "Reached Wave N (record: M)".
  - **Persistence (v1):** in-memory only + emit the score to the existing stats
    backend via `StatsClient.Capture`. The plugin instance survives `changelevel`
    so the in-memory record persists across map resets but not process restarts.
    File persistence is explicitly deferred past v1.
- **Scaling never plateaus problematically** because `healthScale` caps at 24×
  and `enemyCount` at 600 — past a certain wave the mode is a pure endurance
  test at max difficulty, which is the intended "how far can you get" fantasy.

---

## 13. Win condition (non-endless)

- Survive `WaveCount` waves (default 20). Clearing the final wave →
  GameOver(win): HUD "VICTORY — survived N waves", stats emit, post-mode
  cooldown, `changelevel`.
- This replaces TI's "kill the enemy Patron" victory — HordeSurvival has no
  enemy-Patron-assault objective; survival *is* the win.

---

## 14. Death & respawn

- **During a wave.** Default `RespawnDuringWave = true`: respawn on a scaling
  delay so deaths sting but don't bench a player for the whole wave. Reuse TI's
  `pawn.RespawnTime = GlobalVars.CurTime + ComputeRespawnDelay(w)` set in
  `OnEntityKilled` for team-2 pawns. A horde-tuned `ComputeRespawnDelay(w)` (e.g.
  `min(20, 5 + 0.5·w)`) — longer than TI's, since waves are discrete and a quick
  respawn trivializes them.
- **Optional hardcore.** `RespawnDuringWave = false`: dead players stay dead
  until the next Prep Phase, then all respawn together. Higher stakes; better for
  Endless leaderboard runs.
- **On spawn/respawn.** Reuse TI's `DeferredSpawnRitual` → full heal + once-per-
  slot starter gold (`SeedStarterGold`). Earned souls persist across respawns;
  disconnect clears the slot.
- **Fast base respawn ConVar** `citadel_player_spawn_time_max_respawn_time` is
  set at startup (TI does this) but the plugin overrides per-death via
  `RespawnTime`, which is authoritative.

---

## 15. Chat commands, HUD/feedback, plugin composition

### Chat commands (`[Command]` attribute, v0.4.5+, same as TI)

| Command | Effect |
|---|---|
| `!help` | List commands |
| `!wave` | Show current wave / phase ("Wave 7 — PREP, starts in 12s") |
| `!score` | Show waves survived this session + record (Endless-focused) |
| `!stuck` / `!suicide` | Self-respawn — provided by the standalone **StuckCommand** plugin via `gamemodes.json`, not implemented here (TI pattern) |
| `!hero <name>` | Fuzzy hero swap (reuse TI) |
| `!startwaves` / `!stopwaves` | Manual arm/pause (dev/operator) |
| `!nextwave` | Skip remaining prep, start the wave now (dev) |

`!stuck` requested by the brief is satisfied by composing **StuckCommand** (as TI
does) — keep the `!help` line, don't reimplement.

### HUD / feedback

- **HUD toasts** (`AnnounceHud`) for state boundaries only: Prep start, Wave
  start, Wave cleared, Victory/Defeat, New Record. Keeps the HUD uncluttered.
- **Chat** for high-frequency / per-player events: prep countdown ticks (T-10,
  T-5), player death notices ("Alice fell — respawning in 8s"), clear bonus
  awards, wave bounty.
- **Player death notice** in chat on each team-2 `OnEntityKilled`.
- **Gameover suppression.** Like TI, hook `gameover_msg` and `round_end` →
  `HookResult.Stop` so the engine's native end sequence never kicks clients; the
  plugin drives all win/lose itself. Keep TI's `gameover_msg` safety
  `changelevel` fallback for the case where `OnTakeDamage` didn't intercept.

### Plugin composition (`gamemodes.json`)

Add a new service mirroring `trooper-invasion`'s supporting cast:

```json
"horde-survival": ["StatusPoker", "HordeSurvival", "Hostname", "FlexSlotUnlock",
                   "TeamChangeBlock", "HeroSelect", "HealOnSpawn",
                   "DisconnectCleanup", "Feedback", "StuckCommand"]
```

Plus: a Docker service on a new port (TI uses 27018 — pick the next free one),
`paths-filter` stanzas in both CI workflows
(`.github/workflows/build-plugins.yml`, `docker-gamemodes.yml`), and a csproj
following TI's triple-mode reference pattern (DeadlockDir / ProjectReference /
Docker fallback). File layout should mirror TI's post-split partials:
`HordeSurvival.cs` (core/lifecycle/fields), `HordeSurvival.Waves.cs`
(phase machine), `HordeSurvival.Enemies.cs` (spawn/cull/scale/reconcile),
`HordeSurvival.Objectives.cs` (Walker/Patron lose detection),
`HordeSurvival.Players.cs` (join/leave/spawn/knockback),
`HordeSurvival.Guardians.cs` (lifted from TI), `HordeSurvival.Commands.cs`,
`HordeTuning.cs`, `SessionStats.cs`.

---

## 16. Open questions

**All 8 original questions resolved** during design review. Summary of decisions:

- **Q1 (impulse API):** Use `m_vecAbsVelocity`/`m_vecVelocity` schema accessor.
  Fallback: `Teleport` with a small positional nudge (see §11).
- **Q2 (friendly-fire reach):** Requires enabling team damage via `mp_friendlyfire`
  (or equivalent). If unavailable, friendly knockback is dropped in favour of the
  proximity-bump fallback (see §11).
- **Q3 (prep stipend):** Grant `200 + 50 × waveNumber` gold per player via
  `pawn.ModifyCurrency(ECurrencyType.EGold, amount, …)` on Prep Phase entry (see §7).
- **Q4 (Walker lose anchor):** Option A — lose on the first team-2 Walker
  destroyed; `LoseOnFirstWalker = true` (default); operators can switch to Option B
  via config (see §9).
- **Q5 (endless record):** In-memory + `StatsClient.Capture`; no file persistence
  in v1 (see §12).
- **Q6 (wave-clear detection):** 0.5s poll is authoritative; event-driven path is
  a future optimization (see §8).
- **Q7 (`enemyCount` vs engine delivery):** Safety-timeout backstop (15s) is the
  guard; minor over/undershoot per wave is accepted (see §5).
- **Q8 (`EnsureNormalMatchMode`):** Keep it, same as TI; re-test in HordeSurvival
  context (see §9).

### Remaining open item

**Confirm the team-damage ConVar name.** Verify that `mp_friendlyfire` (or its
Deadlock/`citadel_*` equivalent) is available and effective at runtime. If it is
not, the proximity-bump fallback in §11 applies automatically — no further design
work needed.
