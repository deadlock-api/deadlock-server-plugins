---
title: TrooperInvasion plugin
type: plugin
sources:
  - knowledge-base/raw/notes/2026-04-22-trooper-invasion-scaffold.md
  - knowledge-base/raw/notes/2026-04-22-trooper-invasion-gameplay-overhaul.md
  - knowledge-base/raw/notes/2026-04-22-trooper-invasion-round-cycle-and-balancing.md
  - knowledge-base/raw/notes/2026-04-22-trooper-convar-runtime-mutation.md
  - knowledge-base/raw/notes/2026-04-22-trooper-squad-size-cap.md
  - knowledge-base/raw/notes/2026-04-22-onentityspawned-remove-deferral.md
  - knowledge-base/raw/notes/2026-04-22-citadel-active-lane-bitmask.md
  - knowledge-base/raw/notes/2026-04-22-deadlock-three-lanes-only.md
  - knowledge-base/raw/notes/2026-04-22-hud-game-announcement.md
  - knowledge-base/raw/notes/2026-04-22-host-api-version-skew.md
  - knowledge-base/raw/notes/2026-04-23-boss-wave-native-crash.md
  - ../TrooperInvasion/TrooperInvasion.cs
  - ../TrooperInvasion/TrooperInvasion.csproj
related:
  - "[[deadlock-game]]"
  - "[[deadworks-runtime]]"
  - "[[plugin-build-pipeline]]"
  - "[[command-attribute]]"
  - "[[events-surface]]"
  - "[[schema-accessors]]"
  - "[[deathmatch]]"
  - "[[examples-index]]"
created: 2026-04-22
updated: 2026-04-23
confidence: high
---

# TrooperInvasion plugin

Co-op PvE horde mode: all human players are forced to Amber (team 2) and fight
waves of Sapphire (team 3) troopers spawned by the engine on the regular
`dl_midtown` map. Gamemode service tagged `trooper-invasion` on port **27018**.

The original scaffold was a god-mode sandbox; this page reflects the
gameplay-overhaul state after the first iteration session (2026-04-22).

## Wave scheduler

**Autonomous, player-count-driven, round-based.** No `!startwaves` required
for normal play.

- **Auto-arm** on first `OnClientFullConnect` (idempotent — `ArmWaves`
  no-ops if already active or mode-over).
- **Auto-pause + full reset** in `OnClientDisconnect` when
  `HumanPlayerCount(exclude=leaver) == 0`: `DisarmWaves` (spawn off, cull,
  cancel timers, `_waveNum=0`) plus `_roundNum=1`, `_modeOver=false`,
  `_starterGoldSeeded.Clear()`. Event-driven — no polling interval.
- **Round cycling.** Every `RoundLength = 10` waves triggers
  `BeginIntermission`: HUD toast "ROUND N CLEARED", burst-tail delay,
  `DisarmWaves`, `_roundNum++`, `_starterGoldSeeded.Clear()`,
  `IntermissionSeconds = 30`s wait, then auto-rearm if players remain.
  Bounds `_waveNum` (and thus catch-up gold + bounty) over long sessions.
  Player items/AP/earned gold persist across rounds — only the horde
  counter resets.
- **Wave interval** linearly interpolates `SlowWaveIntervalSeconds = 20f`
  at 1 player → `FastWaveIntervalSeconds = 5f` at 32 players. Re-computed
  every `ScheduleNextWave`.
- **First-wave grace** `FirstWaveGraceSeconds = 10f` after arming.
- **Timer tracking.** Every scheduled action (grace, next-wave,
  intermission transitions, burst-end) stored in `_pendingWaveTimer` or
  `_pendingBurstEnd` IHandle fields. `DisarmWaves` cancels both plus all
  timer lambdas re-check `_wavesActive` internally — solves the
  rapid-join/leave stacked-timer bug.
- `!startwaves` / `!stopwaves` retained as manual overrides.

## Wave volume — player-scaled + onboarding ramp

Volume = `MaxSquadSize=8 × activeLanes × 1s pulse × burstSeconds`. Both
`activeLanes` and `burstSeconds` scale with player count; only the
first-three-waves onboarding ramp varies by wave number.

- **Active lanes** (see "Lane gating" below): `Clamp(humans/2, 1, 4)`
- **Burst seconds**: `(MinBurstSeconds=1.0s at 1p → MaxBurstSeconds=8s at 32p linear) × rampFactor`
- **Ramp factor**: `1→0.35, 2→0.55, 3→0.8, 4+→1.0`

| Humans | Wave 1 ≈ troopers | Wave 4+ ≈ troopers |
|---|---|---|
| 1 | 3 (0.35s × 1 lane) | 8 (1.00s × 1 lane) |
| 4 | 9 (0.59s × 2 lanes) | 27 (1.68s × 2 lanes) |
| 8 | 29 (0.90s × 4 lanes) | 83 (2.58s × 4 lanes) |
| 16 | 49 (1.54s × 4 lanes) | 140 (4.39s × 4 lanes) |
| 32 | 90 (2.80s × 4 lanes) | 256 (8.00s × 4 lanes) |

**Why pulses, not squad size:** `citadel_trooper_squad_size` has an
engine-enforced **hard cap of 8** ("Squad … is too big!!! Replacing last
member" spew when exceeded — see [[deadlock-game]]). Total volume must come
from pulses × lanes × squad. The plugin pins squad at 8 and scales burst
duration. ConVars set in `OnStartupServer`:
`citadel_trooper_max_per_lane=256` (10× vanilla; higher correlated with
crashes — see "Friendly-trooper culling" below),
`citadel_trooper_spawn_interval_early/late/very_late=1f`,
`citadel_trooper_spawn_initial=0`, `citadel_trooper_spawn_wave_spread=2`.

## Lane gating — ≥ 2 players per active lane

`citadel_active_lane` accepts **explicit lane IDs OR'd together**, not a
naive `(1 << N) - 1` bitmask. **Deadlock currently has only 3 lanes**:
`{1 = Yellow, 4 = Blue, 6 = Purple}`. Green (value `3`) is no longer a
lane. `RunWave` OR's the first `activeLanes` markers from `{1, 4, 6}`
where `activeLanes = Clamp(humans/2, 1, 3)`:

| Humans | Active lanes | Mask | Lanes |
|---|---|---|---|
| 1–3 | 1 | `1` | Yellow |
| 4–5 | 2 | `1\|4 = 5` | Yellow + Blue |
| 6+ | 3 | `1\|4\|6 = 7` | Yellow + Blue + Purple |

Recomputed every wave, so lane count tracks live joins/leaves.

**Why not `(1 << N) - 1`:** the earlier formula produced mask `3` at
4 players, which references the defunct Green lane. The spawn pipeline
silently no-op'd — scheduler kept firing wave timers and printing
"next in 19s" chat lines but no troopers emerged. Only surfaced at
exactly 4 players because that was the first transition away from the
1-lane (Yellow-only) value that 1–3-player play had been pinned at.
Fix in commit `5382526`; see
`raw/notes/2026-04-22-deadlock-three-lanes-only.md`.

Reference plugins: `DeathmatchPlugin.cs:109` sets a single lane (value
`4` = Blue); `TagPlugin.cs:90` sets `255` (all lanes — effectively a
bitmask of "every possible bit, engine ignores extras"). Our multi-lane
masks `{5, 7}` sit between these patterns; they work because they
aren't any single valid lane ID, so the engine treats them as a combined
bitmask rather than a specific-lane reference.

## Friendly-trooper culling

No per-team spawn ConVar exists in Deadlock, so any team-2 trooper that
spawns is wasted. Plugin hooks `OnEntitySpawned`, filters
`DesignerName ∈ {"npc_trooper", "npc_trooper_boss"}` and `TeamNum == 2`, then
**defers** `Remove()` one tick via `Timer.Once(1.Ticks(), () =>
CBaseEntity.FromIndex(idx)?.Remove())`.

The deferral is load-bearing. A direct synchronous `Remove()` from
`OnEntitySpawned` (the [[examples-index|TagPlugin]] pattern) crashed with a
native AV during heavy spawn cascades — the engine's spawn iterator does not
tolerate mid-iteration deletion at horde-mode scale. **For per-spawn culling
during a horde, defer; TagPlugin's direct-Remove pattern is only safe for
low-volume one-shot map cleanup.**

## ConVar mutation: `Server.ExecuteCommand`, not `ConVar.Find().SetInt`

For *runtime* convar writes (mid-frame, after subsystem init), the plugin
**must** use `Server.ExecuteCommand("name value")`. Direct
`ConVar.Find("citadel_trooper_*").SetInt/SetFloat()` from a chat-command
handler or Timer callback crashed natively on several trooper convars (the
spawn-interval family, `max_per_lane`, re-toggling `spawn_enabled` 0→1).
C# `try/catch` doesn't see those — they're below the managed boundary.

The console path goes through the engine's own `CCVar` dispatch (same as
the boot-time `hostname` write and Deathmatch's pattern) and is stable.
Direct `ConVar.Find().Set*` is reserved for `OnStartupServer`
(pre-trooper-subsystem-init), where it works. See
[[source-2-engine]] / `2026-04-22-trooper-convar-runtime-mutation.md` —
this generalises beyond TrooperInvasion.

## Progression — real PvE loop

The scaffold's god-mode shortcuts (999 999 starting gold, max-upgraded
signature abilities, 3s spawn invulnerability) are all **removed**.

| Aspect | Behaviour |
|---|---|
| Starter capital | `StarterGold = 2500`, seeded **once per slot** via `HashSet<int>`. Respawn keeps your earned souls; disconnect clears the slot so reconnect re-seeds |
| Abilities | All signature abilities at tier-0 on spawn. Players earn AP from trooper kills and spend through the normal upgrade UI |
| Trooper bounty | `citadel_trooper_gold_reward = 35 + waveNum × 5` (halved from the prior 70+10×N formula to compensate for the toughened HP curve below; vanilla baseline was ~80) |
| Spawn protection | **Removed** — no invuln, no `OnTakeDamage` override, no `_invulnerableUntil` tracking. Death has weight |
| Heal on spawn | Kept — full health on respawn (PvE forgiveness, not a progression cheat) |

**Slot-from-pawn pattern** (used by `SeedStarterGold`):
`pawn.Controller?.Slot`. `CBasePlayerPawn.Controller` is a schema accessor
on `m_hController`; `CBasePlayerController.Slot => EntityIndex - 1`. Useful
for any per-player state keyed by slot.

## Hero switching

Original scaffold hard-blocked `selecthero` and `citadel_hero_pick` to lock
the auto-picked least-present hero. Requirement changed:
`OnClientConCommand` now blocks **only** `changeteam` and `jointeam` (to
keep all humans on team 2). The in-game hero-pick UI works any time;
`!hero <name>` fuzzy-match remains as an alternative.

## HUD announcements

Round-boundary events use `CCitadelUserMsg_HudGameAnnouncement` — centered
HUD toasts, not chat scroll. Wrapped as `AnnounceHud(title, description)`.
Fired for: round armed, round cleared, victory, defeat. Per-wave events
stay as chat to avoid spamming the HUD. No csproj change required — the
proto type is transitively available via `DeadworksManaged.Api`. Pattern
lifted from `TagPlugin.cs:342-346`.

## Other gameplay state

- **Map NPCs left intact** (opposite of [[deathmatch]]) — Sapphire patron,
  walkers, guardians, sentries are the gameplay content.
- **Gameover suppressed** — `OnGameoverMsg` and `OnRoundEnd` return
  `HookResult.Stop`. That alone keeps the mode in-progress indefinitely
  without touching the HUD clock or `m_eGameState`. Earlier iterations
  wrote the 5-field anchor + `eGameState` every tick; all that code was
  deleted. Engine's native HUD clock runs correctly on its own.
- **Win/lose conditions** — `entity_killed` listens for `npc_barrack_boss`
  (patron) deaths. Team-2 patron destroyed → defeat; team-3 patron
  destroyed → victory; either way `_modeOver = true`, scheduler stops.
- **Flex slots** force-unlocked at startup (schema-write pattern, same as
  [[deathmatch]]).
- **Fast respawn** — `citadel_player_spawn_time_max_respawn_time = 3`.
- **Allow duplicate heroes** — `citadel_allow_duplicate_heroes = 1` (no
  Amber-side hero lock).
- **Allow purchasing anywhere** — `citadel_allow_purchasing_anywhere = 1`.

## Chat / console commands

All commands use the v0.4.5 [[command-attribute|`[Command]`]] attribute
(matches LockTimer's post-`6ace83c` state, diverges from
[[deathmatch]] which still uses legacy `[ChatCommand]`).

| Command | Surface | Effect |
|---|---|---|
| `!help` / `/help` / `dw_help` | all | List commands |
| `!wave` | chat | Show current wave |
| `!startwaves` | chat | Manual arm (auto-armed by player join) |
| `!stopwaves` | chat | Manual pause |
| `!nextwave` | chat | Trigger next wave immediately (dev) |
| `!hero <name>` | chat | Fuzzy-match hero swap (multi-word: `grey talon`) |
| `!stuck` / `!suicide` | chat | `pawn.Hurt(999999f)` to respawn |

## Files

- `TrooperInvasion/TrooperInvasion.cs` — single-file plugin (~500 LOC)
- `TrooperInvasion/TrooperInvasion.csproj` — triple-mode reference pattern
  (DeadlockDir / ProjectReference / Docker fallback), identical to
  StatusPoker's csproj minus the assembly name
- `TrooperInvasion/Properties/launchSettings.json` — Rider debug launcher
- Registered in `gamemodes.json` as
  `"trooper-invasion": ["StatusPoker", "TrooperInvasion"]`
- Docker service in `docker-compose.yml` on port 27018
- Both CI workflows' `paths-filter` stanzas updated
  (`.github/workflows/build-plugins.yml`,
  `.github/workflows/docker-gamemodes.yml`)

## Operational gotcha — host/Api version skew

When iterating this plugin we discovered that the deployed game's
`managed/DeadworksManaged.dll` (the host) and `DeadworksManaged.Api.dll`
(the plugin-facing API) are independently built and **must match** —
otherwise every `[Command]` chat invocation crashes natively with
`MissingMethodException` thrown from `PluginLoader.DispatchChatMessage`.

Symptom: every `!cmd` instant-crashes the server, but the plugin's
`OnLoad` log line still appears. File-flushed breadcrumbs in the
`[Command]` handler never trigger — the crash is in the host's command
binder, before the handler runs. See
`2026-04-22-host-api-version-skew.md` for the recovery procedure
(`dotnet build deadworks/managed/DeadworksManaged.csproj`, copy the full
`bin/Debug/net10.0/` contents — host + 14 transitive deps — into the
game's `managed/` folder). Affects every plugin that uses `[Command]`,
not just TrooperInvasion.

**Diagnostic trick**: managed exception messages in Windows minidumps are
UTF-16. `strings` defaults to ASCII and misses them. Use
`strings -e l -n 12 dump.mdmp | grep 'Method not found\|Exception'` to
recover the full exception message + missing-method signature without
needing WinDbg.

## Boss waves — removed (native crash on first spawn)

An earlier iteration added round-end boss waves that manually spawned
`npc_trooper_boss` via the managed entity API:

```csharp
var boss = CBaseEntity.CreateByDesignerName("npc_trooper_boss");
boss.TeamNum = EnemyTeam;
boss.Teleport(pos);
boss.Spawn();             // null CEntityKeyValues
```

**This crashed the server natively on the first boss spawn every time.**
`npc_trooper_boss` is a lane-AI NPC — the engine's normal spawn path
(`CEntitySpawner<CNPC_TrooperBoss>::Spawn`, driven by
`CCitadelTrooperSpawnGameSystem` + the map's `info_trooper_spawn` /
`info_super_trooper_spawn` entities) feeds it `m_iLane`, squad
registration, and a navmesh region via a fully-populated CEntityKeyValues.
With null KV and only `TeamNum` set via schema, the post-`Spawn` AI init
dereferences a null/uninitialised lane or squad pointer. C# `try/catch`
can't see the crash — it's below the managed boundary.

Point-entity managed spawns (`CPointWorldText.Create`,
`ParticleSystem.Spawn`) don't trip this because they pass an explicit
`CEntityKeyValues` into `Spawn(ekv)` and have no lane/squad dependency.
**The managed-spawn pattern is only safe for non-AI entities with
explicit KV.** No managed API wrapper exists for trooper classes, and the
schema of CEntityKeyValues keys on `CNPC_TrooperBoss` that would let us
feed it lane/squad safely is not published.

Contradicts raw note `2026-04-23-trooper-invasion-boss-waves.md` (which
described the `CreateByDesignerName + Spawn()` pattern as working — it
was written pre-production-test). That note stands as historical context
but its first section is superseded by
`2026-04-23-boss-wave-native-crash.md`.

**If boss waves are reintroduced**, use the engine's native cheat
concommand `citadel_spawn_trooper %f,%f,%f %s` (valid types:
`default` / `boss` / `melee` / `medic` / `flying`) which routes through
`CCitadelTrooperSpawnGameSystem` properly. It's FCVAR_CHEAT, so bracket
with `sv_cheats 1` / `sv_cheats 0` — same idiom
`FlexSlotUnlock.cs:29-31` uses for `citadel_unlock_flex_slots`. Format
coords with `CultureInfo.InvariantCulture` so a Wine locale doesn't
decimal-comma the float list. The engine still naturally promotes regular
troopers to `npc_trooper_boss` via the super-trooper progression (see
`citadel_super_trooper_gold_mult`), so the plugin's `_trooperDesigners`
list and kill-attribution filters still recognise bosses without any
manual spawn.

## What's intentionally missing (vs Deathmatch)

- No lane rotation / round timer / `[DM]` kill-tracking — single-team PvE
  has no lane/round concept.
- No walker spawn capture (`PickSpawnPoint`) — default engine respawn
  locations are correct.
- No cooldown scaling — vanilla cooldowns are fine in PvE.
- No rank-based team balancing — one team, nothing to balance.
- No `NetMessages.Send` HUD announcements — csproj does **not** reference
  `Google.Protobuf`. Add per [[plugin-build-pipeline]] if announcements
  become desired.
- No `HeroItemSets.jsonc` per-hero loadouts.
