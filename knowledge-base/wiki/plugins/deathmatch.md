---
title: Deathmatch plugin
type: plugin
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-310fc296.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-3636296d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-493a9384.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-5233473a.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-6d3a9327.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-73f32122.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-980b8b28.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-c51730eb.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-e6b640b7.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-fa5d1d7e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-65d13a2e.md
  - ../DeathmatchPlugin/DeathmatchPlugin.cs
related:
  - "[[deadlock-game]]"
  - "[[source-2-engine]]"
  - "[[deadworks-runtime]]"
  - "[[plugin-build-pipeline]]"
created: 2026-04-21
updated: 2026-04-21
confidence: high
---

# Deathmatch plugin

A team-vs-team deathmatch gamemode built on top of Deadlock. Lives under
`DeathmatchPlugin/` (formerly `Deathmatch/`) in this repo, and is
independently maintained in the sibling `deadlock-deathmatch` repo.

## What it does

- Forces match state into a permanent deathmatch round loop on
  `dl_midtown` with:
  - Pause/HUD clock frozen (no time limit, no gameover).
  - Walker-based spawn points around the map.
  - Round-rotating active lane: cycle `{1, 3, 6}` = Yellow / Green /
    Purple. Blue (4) is explicitly skipped (deathmatch-3636296d).
  - `RoundSeconds = 180f` per round.
- Strips non-combat NPCs so the map is purely PvP: `npc_boss_tier1`
  (Guardian), `npc_boss_tier2` (Walker — kept as spawn anchors),
  `npc_boss_tier3` (Base Guardian/Shrine), `npc_barrack_boss` (Patron),
  `npc_base_defense_sentry`, `npc_trooper_boss` (deathmatch-3636296d).
- On respawn: re-applies `ApplySpawnRitual` = max sig abilities + spawn
  protection modifier + heal to full + gold grant + flex-slot re-unlock
  (server-plugins-65d13a2e).

## Team picker bypass

Default Deadlock sends clients through a team-picker prompt that appears
**before** `OnClientFullConnect` fires. Plugin approach
(deathmatch-e6b640b7):

- `OnClientFullConnect` calls `controller.ChangeTeam(int)` +
  `SelectHero`. `ChangeTeam` on the controller is **server-initiated**
  and bypasses the client picker entirely.
- `OnClientConCommand` intercepts `changeteam`, `jointeam`, `selecthero`,
  `citadel_hero_pick` client concommands and returns `HookResult.Stop`
  to block manual switching (deathmatch-493a9384).
- Caveat: the engine's auto-balancer also calls `ChangeTeam` from the
  server side — that path does NOT flow through the concommand hook, so
  blocking concommands does not disable auto-balance.

## Flex slot unlock

Requires both gamerules bool AND per-team bitmask, written at several
lifecycle points (deathmatch-e6b640b7):

- `CCitadelGameRules.m_bFlexSlotsForcedUnlocked = true`
- `CCitadelTeam.m_nFlexSlotsUnlocked = 0xF` on **every** team entity
  (bits `Kill2Tier1=0x1 | Kill1Tier2=0x2 | Kill2Tier2=0x4 | BaseGuardians=0x8`).

Re-applied at: startup after 1s delay (so gamerules/teams network),
`OnClientFullConnect`, `OnPlayerHeroChanged`, `OnPlayerRespawned`.

## Walker capture

- `npc_boss_tier2` carries `m_eLaneColor` (`CNPC_TrooperBoss::m_eLaneColor`,
  a `CMsgLaneColor` uint enum). Values: `1=Yellow, 3=Green, 4=Blue,
  6=Purple` (deathmatch-980b8b28, deathmatch-fa5d1d7e).
- Captured at startup loop and in `OnEntitySpawned`. `ent.TeamNum`
  identifies which side it belongs to.
- **Schema lane read races spawn-time init** — it can read back as 0 on
  early `OnEntitySpawned`. Plugin must not gate capture on a valid read.
  Fallback: bearing-heuristic bucketing (deathmatch-980b8b28):
  ```
  Atan2(pos.Y - approxCenter.Y, pos.X - approxCenter.X)
  ```
  sort + lane cycle `{1, 4, 3, 6}` modulo index for per-team deterministic
  assignment.
- `OnEntitySpawned` triggers full `RebuildWalkerBuckets()` +
  `RecomputeMapCenter()` per walker — assumed to be map-load-time-only,
  would thrash if walkers spawned at tick rate (deathmatch-5233473a).

## HUD match clock anchor

Client computes `game_clock ≈ m_flMatchClockAtLastUpdate + (CurTick -
m_nMatchClockUpdateTick) * IntervalPerTick`, with extra factoring on
`m_fLevelStartTime`. Writing only the float leaves tick anchor stale
(deathmatch-fa5d1d7e, deathmatch-980b8b28, deathmatch-493a9384,
deathmatch-5233473a).

Working pattern: per-tick callback that reads `GlobalVars.CurTime -
_roundStart` and writes:

- `m_flGameStartTime = CurTime - elapsed - (TotalPausedTicks * IntervalPerTick)`
- `m_fLevelStartTime = same`
- `m_flRoundStartTime = same`
- `m_flMatchClockAtLastUpdate = elapsed`
- `m_nMatchClockUpdateTick = TickCount`

Missing `m_flRoundStartTime` was the specific bug fixed in commit
`dc9114e`. Driving timers off a ticking schema write is more reliable
than `Timer.Every(180)` — `Timer.Every` may not fire while the server
is idle/empty, but the tick callback still runs.

## Cooldown scaling

`ScaleAbilityCooldowns` shifts the cooldown window so remaining time
becomes `scale × duration` (50% with `CooldownScale = 0.5f`). The game
re-derives `End = Start + vdataDuration`, so shifting the WHOLE window
(both start AND end) makes the reduction sticky (deathmatch-980b8b28).

Pattern:

```csharp
CooldownStart -= shift;
CooldownEnd   -= shift;
// where shift = (1 - scale) * duration
// and _writtenCooldowns map keyed by ability.Handle deduplicates per tick
```

Optimized to eliminate per-tick dictionary allocations
(deathmatch-5233473a): `ScaleAbilityCooldowns` previously allocated a
fresh `Dictionary<nint, (float, float)>` every tick at 64 Hz. Rewrote to
mark-and-sweep in place with a reused `HashSet`/`List`.

## `player_respawned` / hero-changed

- `player_respawned` fires on BOTH fresh spawns and respawns. Distinguish
  via `player_death` handler storing `_lastDeathPos[pawn.EntityIndex]`
  and checking dict presence (deathmatch-493a9384).
- `player_death` payload exposes `VictimX/Y/Z`, `UseridPawn`,
  `AttackerController`.
- `player_hero_changed` provides `Userid` as a pawn-like handle needing
  `.As<CCitadelPlayerPawn>()`; `.Controller` on the pawn walks back to
  the controller (server-plugins-65d13a2e).
- On respawn without prior death (first spawn), plugin places pawn via
  `PickSpawnPoint`. On respawn with prior death, same — but a rotation
  handler may have shifted `ActiveLane` in between.

**Drift-on-respawn rotation pattern** (commit `65eb42c`,
deathmatch-5233473a): rotating active lane does NOT teleport alive
players — they stay in the old lane and naturally drift to the new one
on next respawn because `PickSpawnPoint` targets `ActiveLane`. Replaced
the earlier mass-teleport reset.

## Healing after respawn / hero change

`GetMaxHealth()` returns 0 for several ticks after `player_respawned` /
`player_hero_changed` — writing `pawn.Health` at event-fire time leaves
the player at 0 HP (deathmatch-73f32122, deathmatch-980b8b28,
deathmatch-c51730eb).

Fix: retry loop via `Timer.Once(1.Ticks(), ...)` up to 20 ticks
(`HealToFull` / `TryHeal`). Capture `pawn.EntityIndex` (int), re-resolve
via `CBaseEntity.FromIndex<CCitadelPlayerPawn>(idx)` inside each tick —
entity handles become stale across ticks.

**Static-vs-instance gotcha** (deathmatch-c51730eb): `Timer` is an
**instance** property on `DeadworksPluginBase`, not static. `HealToFull`
and `TryHeal` must be `private` (not `private static`), else CS0120
"object reference required". Fix applied in commit `caa9b05`.

## Spawn protection

- `_invulnerableUntil[idx]` (keyed by pawn.EntityIndex) tracks timed
  invulnerability with `EModifierState.Invulnerable | BulletInvulnerable`.
- Removal must compare `_invulnerableUntil[idx]` against the scheduled
  `until` with 0.05s epsilon before clearing modifier state — stacked
  grants can cancel each other prematurely otherwise (deathmatch-980b8b28).
- `OnTakeDamage` zeroes damage for invulnerable entities INCLUDING
  suicide. To force suicide via `!stuck`, remove from
  `_invulnerableUntil` first and clear the modifier state
  (server-plugins-65d13a2e).

## Chat commands

Registered with `[ChatCommand("!help")]` / `"!hero"` / `"!stuck"` /
`"!suicide"` — string includes the `!` prefix by deadworks convention
(server-plugins-65d13a2e, commit `a81201b`).

- `!help` — list commands.
- `!hero <name>` — fuzzy hero selection (uses `HeroTypeExtensions`
  filtering via `CitadelHeroData.AvailableInGame`).
- `!stuck` / `!suicide` — force-kill self via `pawn.Hurt(999_999f)`
  after clearing spawn-protection (`CBaseEntity.Hurt` auto-sets
  `TakeDamageFlags.AllowSuicide = 0x40000`).

`ChatCommandContext` exposes `ctx.Message.SenderSlot`, `ctx.Controller`
(nullable), whitespace-split `Args[]` (deathmatch-493a9384,
server-plugins-65d13a2e).

## HUD announcements

Rotation announces send `CCitadelUserMsg_HudGameAnnouncement`
(from `citadel_usermessages.proto`) via:

```csharp
NetMessages.Send(new CCitadelUserMsg_HudGameAnnouncement {
    TitleLocstring = ...,
    DescriptionLocstring = ...,
}, RecipientFilter.All);
```

Includes Amber vs Sapphire score + top-killer + next-lane name
(deathmatch-3636296d).

Requires `<PackageReference Include="Google.Protobuf" Version="3.29.3" Private="false" ExcludeAssets="runtime" />`
in the plugin csproj — see [[plugin-build-pipeline]].

## Post-death hero swap window

`_heroSwapUntil[ctrl.EntityIndex]` — 10s window seeded only on actual
respawn-after-death (deathmatch-5233473a, deathmatch-980b8b28):

- Normally `selecthero` / `citadel_hero_pick` concommands return
  `HookResult.Stop`.
- During window, they're allowed through.
- Bug pattern: window not auto-reopened by engine — needs UX cue (chat
  hint) to be usable.
- Cleanup must happen on successful `player_hero_changed` to avoid
  lingering entries between reconnects with reused entity indices
  (server-plugins-65d13a2e).

## Per-player state dicts (disconnect cleanup)

`OnClientDisconnect` cleans ALL dicts keyed by controller/entity index
(deathmatch-5233473a):

- `_invulnerableUntil`
- `_heroSwapUntil`
- `_lastDeathPos`
- `_killsThisRound`
- `_writtenCooldowns`

Also `controller.GetHeroPawn()?.Remove()` + `controller.Remove()` for
full teardown.

## Friendly fire

Deadlock ships with `mp_friendlyfire = 0` default — blocks same-team
**bullet** damage. But ability AoE/grenades/debuff pulses are NOT
guaranteed to respect team. Plugin adds explicit
`if (v.TeamNum == a.TeamNum && a != v) Damage = 0` to `OnTakeDamage` to
close gaps (deathmatch-e6b640b7).

## `FreezeMatchClock` (gameover suppression)

Forces `EGameState.GameInProgress` + `OnGameoverMsg` Stop + `OnRoundEnd`
Stop — the comprehensive gameover-suppression pattern. Pins the clock
and rules out any time-limit mode until relaxed (deathmatch-e6b640b7).

## Config hot reload

`DeathmatchConfig` is `[PluginConfig]`-decorated; hot-reloaded via
`OnConfigReloaded()`, which restarts the swap timer via
`_swapTimer?.Cancel()` + `Timer.Every(...)` (deadworks-3beeff54).

Empty `DeathmatchConfig` is **required** by the host contract — not
dead code (deathmatch-5233473a).

## `HeroItemSets.jsonc`

`DeathmatchPlugin/HeroItemSets.jsonc` — numeric `hero_id` keys 1..81
with per-hero item sets. `Config.HeroItemSets` resolves by stringified
hero id on respawn (deadworks-88df5d67).

## Repo layout note

The standalone `deadlock-deathmatch` repo
(`github.com:raimannma/deadlock-deathmatch.git`) is a satellite that
consumes `DeadworksManaged.Api` from the sibling `deadworks/` checkout.
It uses the same dual-mode csproj pattern documented in
[[plugin-build-pipeline]] (deathmatch-6d3a9327, deathmatch-3636296d,
deathmatch-73f32122).

The in-repo version at `deadlock-server-plugins/DeathmatchPlugin/` is
largely the same code with the same csproj triple-mode logic.

## Known issues / future work

- First-spawn placement (`OnClientFullConnect`) does NOT call
  `PickSpawnPoint` — initial spawn can land at a stale default
  (deathmatch-980b8b28).
- `_heroSwapUntil` window has no UX surface — chat hint/menu prompt
  would make it usable.
- User-requested design change (unaddressed at last session
  deathmatch-3636296d): switch from bulk-teleport-all-alive at rotation
  to per-player teleport on next death so active lane shifts gradually.
