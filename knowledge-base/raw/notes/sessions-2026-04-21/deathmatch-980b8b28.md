---
date: 2026-04-21
task: session extract — deathmatch 980b8b28
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-deathmatch/980b8b28-e889-42e6-9b43-479a300017cc.jsonl]
---

## Deadlock game systems

- `citadel_player_spawn_time_max_respawn_time` caps max respawn time, but the engine still enforces a **minimum** respawn floor that is not pinned by that convar — context-dependent death scenarios can leave a longer floor active despite the cap (`DeathmatchPlugin.cs:70`).
- `OnClientFullConnect` calls `ChangeTeam` + `SelectHero` but does NOT place the pawn — first-spawn placement is left to the engine's default, which on a lane map with the Patron removed can land the player at a stale default location. `PickSpawnPoint` only runs via `player_respawned` with `hadPriorDeath=false` as a fallback; initial spawn doesn't reliably trigger the fallback (`DeathmatchPlugin.cs:278-313`, `376-406`).
- `_heroSwapUntil` is used to let a player re-issue `selecthero`/`citadel_hero_pick` in a 10s post-death window while those concommands are otherwise blocked by `OnClientConCommand` (`DeathmatchPlugin.cs:328-342`). Without UX surface (chat hint / menu prompt), the window is invisible — the engine does not auto-reopen the picker.
- Canonical Deadlock lane color ints used across schema + code: Yellow=1, Blue=4, Green=3, Purple=6 (`DeathmatchPlugin.cs:262-269`). Deathmatch's _laneCycle skips Blue (4).
- Deadlock teams used in this gamemode: 2=Amber, 3=Sapphire (`DeathmatchPlugin.cs:271-276`).

## Deadworks runtime

- Cooldown-shift pattern survives game-side recomputation: writing `CooldownStart -= shift; CooldownEnd -= shift` where `shift = (1-scale)*duration` and re-detecting via `_writtenCooldowns` map keyed by `ability.Handle` each tick. The game re-derives `End = Start + vdataDuration`, so shifting the WHOLE window (not just end) is what makes 50% cooldowns sticky (`DeathmatchPlugin.cs` cooldown scaling block around the `ScaleAbilityCooldowns` method).
- HUD match clock requires writing BOTH `m_flMatchClockAtLastUpdate` AND `m_nMatchClockUpdateTick` together each tick — client computes `game_clock ≈ m_flMatchClockAtLastUpdate + (CurTick - m_nMatchClockUpdateTick) * IntervalPerTick`, so a stale anchor makes the displayed clock keep climbing even when the float is pinned. Setting `tick = GlobalVars.TickCount` makes the extrapolation delta zero (`DeathmatchPlugin.cs:108-121`).
- `GameRules.TotalPausedTicks * GlobalVars.IntervalPerTick` is the correct offset to subtract from the clock anchor to account for paused ticks when freezing the match clock (`DeathmatchPlugin.cs:115-116`).
- `HealToFull` needs a retry loop: `GetMaxHealth` returns 0 until stats/modifiers settle after `player_respawned`/`player_hero_changed` — retry up to 20 times via `Timer.Once(1.Ticks(), ...)` before giving up (`DeathmatchPlugin.cs:453-474`).
- Spawn-protection removal must be guarded against stacked grants: compare `_invulnerableUntil[idx]` against the scheduled `until` (with 0.05s epsilon) before clearing modifier state, otherwise a fresh grant overlapping the old timer gets cancelled prematurely (`DeathmatchPlugin.cs:487-498`).
- `NetMessages.Send(new CCitadelUserMsg_HudGameAnnouncement { TitleLocstring, DescriptionLocstring }, RecipientFilter.All)` is the working pattern for centered game announcements (`DeathmatchPlugin.cs:439-443`).
- `CNPC_TrooperBoss::m_eLaneColor` is the schema field exposing Walker/Tower lane color; `SchemaAccessor<uint>` is used, cast to int, and validated against `{1,3,4,6}` (`DeathmatchPlugin.cs:54, 175-183`).
- Bearing-heuristic fallback for lane bucketing: when not all Walkers report a schema lane, `Atan2(pos.Y - approxCenter.Y, pos.X - approxCenter.X)` sort + lane cycle `{1,4,3,6}` modulo index assigns lanes deterministically per team (`DeathmatchPlugin.cs:199-217`).

## Source 2 engine

- Assistant-asserted but unverified: the engine's team auto-balancer invoked via `ChangeTeam` may run BEFORE plugin `OnClientFullConnect` completes full placement; `controller.ChangeTeam(int)` is server-initiated and bypasses client `changeteam`/`jointeam` concommand hooks. (Session only mentions this as a caveat, not verified — treat as low confidence.)

## Plugin build & deployment

_No substantive findings._
