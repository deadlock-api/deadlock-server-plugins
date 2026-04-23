---
title: Deadlock (the game) — specifics for plugin work
type: concept
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-1b75db40.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-34752d6a.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-4f2af8b9.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-7554a944.md
  - knowledge-base/raw/notes/sessions-2026-04-21/server-plugins-65d13a2e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/custom-server-2c3ccbd4.md
  - knowledge-base/raw/notes/sessions-2026-04-21/custom-server-a6b83c6e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-52a01b09.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-a54dc08d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-ddfface7.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-d75e1c40.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-88df5d67.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deadworks-328372c6.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-3636296d.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-493a9384.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-980b8b28.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-e6b640b7.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-fa5d1d7e.md
  - knowledge-base/raw/notes/sessions-2026-04-21/deathmatch-73f32122.md
  - knowledge-base/raw/notes/2026-04-23-citadel-kick-disconnected-players.md
related:
  - "[[source-2-engine]]"
  - "[[deathmatch]]"
  - "[[trooper-invasion]]"
  - "[[deadworks-runtime]]"
created: 2026-04-21
updated: 2026-04-23
confidence: high
---

# Deadlock (the game) — specifics for plugin work

Deadlock is Valve's MOBA-ish hero shooter, in closed testing (Apr 2026).

## Identity

- **Steam app ID: `1422450`** (client/dedicated server, both use this in
  SteamCMD `app_update`, `steam_appid.txt`, and Proton `compatdata/1422450`).
  Hardcoded across every entrypoint script (server-plugins-9df5d718,
  deadworks-a54dc08d, custom-server-2c3ccbd4).
- Some older sources reference `1422460` as a separate "dedicated server"
  app id (server-plugins-1b75db40, server-plugins-34752d6a), but all current
  server-side infrastructure uses `1422450` for both install and compatdata.
- Game directory name (the "mod" in Source terms) is **`citadel`**, not
  "deadlock". Launch flag is `-game citadel`. `server.dll` lives under
  `game/citadel/bin/win64/server.dll`.
- Default map: `dl_midtown`. Idle server has ~860 entities loaded.
- Both `SteamAppId` and `SteamGameId` env vars must be exported inside the
  gosu subshell that launches the server (server-plugins-9df5d718).

## Engine constants

`CGlobalVarsBase` layout (deathmatch-fa5d1d7e):

- `kCurTime = 0x30`
- `kTickCount = 0x44`
- `kIntervalPerTick = 0x54`
- base ends at `0x5C` before `CGlobalVars` adds mapname/etc.

## Teams, lanes, match states

- **Team IDs** (server-plugins-34752d6a, deathmatch-980b8b28):
  - `0` = unassigned / spectator
  - `2` = Amber
  - `3` = Sapphire
- **Lane color IDs** (from `CNPC_TrooperBoss::m_eLaneColor`,
  `CMsgLaneColor` uint enum; deathmatch-980b8b28, deathmatch-fa5d1d7e):
  - `1` = Yellow
  - `3` = Green
  - `4` = Blue
  - `6` = Purple
- The lane color field can read back as **0** on early `OnEntitySpawned`
  events — schema reads race spawn-time initialisation. Plugins must not gate
  capture on a valid read; fall back to bucketing by bearing angle around
  team centroid.
- **Game states** (`m_eGameState`, values 0–11): Init / WaitingForPlayers /
  HeroSelection=3 / MatchIntro=4 / WaitForMapToLoad=5 / PreGameWait=6 /
  GameInProgress=7 / PostGame / End / Abandoned (server-plugins-1b75db40,
  server-plugins-34752d6a).

## `CCitadelGameRules`

Discovery: walk entity system for entity with designer_name
`citadel_gamerules` (the `CCitadelGameRulesProxy`), then read `m_pGameRules`
at proxy offset **`0x4A0`** → actual `CCitadelGameRules` instance
(server-plugins-1b75db40).

Resolved offsets (Deadlock build ~6411; server-plugins-1b75db40):

- `m_eGameState = 0xFC`, `m_eMatchMode = 0x12C`, `m_eGameMode = 0x130`
- `m_bFreezePeriod = 0xE0`, `m_flGameStartTime = 0xE8`
- `m_bServerPaused = 0x27D8`, `m_iPauseTeam = 0x27DC`
- `m_flMatchClockAtLastUpdate = 0x27E4`, `m_nMatchClockUpdateTick = 0x27E0`
- `m_unMatchID = 0x2870`

Clock-related schema fields used by plugins (deathmatch-493a9384,
deathmatch-fa5d1d7e):

- `m_flGameStartTime`, `m_fLevelStartTime`, `m_flGameStateStartTime`,
  `m_flGameStateEndTime`, `m_flRoundStartTime`, `m_flMatchClockAtLastUpdate`,
  `m_nMatchClockUpdateTick`, `m_eGameState`, `m_eMatchMode`, `m_eGameMode`.
- All three `m_fLevelStartTime`, `m_flGameStartTime`, `m_flRoundStartTime`
  must be pinned in lockstep to reset the HUD clock.

Also present: `m_bGamePaused`, `m_bRoundDown`, `m_nLevel`,
`m_nMatchLengthMinutes`, `m_nRoundsTotal`, `m_nRoundsRemaining`,
`m_nGameOverEvent`, `m_bFlexSlotsForcedUnlocked`,
`m_flGameTimeAllPlayersDisconnected`.

RTTI-confirmed network vars: `m_nMatchClockUpdateTick` (int) and
`m_flMatchClockAtLastUpdate` (float). Client-side class is
`C_CitadelGameRules`, server-side is `CCitadelGameRules`.

## Player entities

- Controller class: `citadel_player_controller` (index 1 in entity list).
  Pawn class: `player` (NOT a per-hero classname; `m_nSubclassID` is 0 for
  player pawns).
- `CCitadelPlayerController` offsets (server-plugins-7554a944,
  server-plugins-4f2af8b9):
  - `+0x708` = `m_steamID` (SteamID64, individual-account range
    `76561190000000000..76561210000000000`)
  - `+0x984` = `m_hHeroPawn` (CHandle — low 15 bits is entity index)
  - `+0x9C8` = `m_PlayerDataGlobal` sub-struct
  - `+0x9E4` = `m_nHeroID` (inside the sub-struct at sub-offset `0x1C`)
  - `+0xEB8` = `m_nLevel` (on pawn, not controller, per -4f2af8b9)
- Pawn health lives directly on `CBaseEntity` as `m_iHealth = 0x2D0`,
  `m_iMaxHealth = 0x2D4`. **Health can exceed `m_iMaxHealth`** in Deadlock
  (items grant bonus HP), so `health <= max_health` sanity checks drop
  valid hero entities.
- `m_iTeamNum = 0x33C` (CBaseEntity).
- Max HP is not a settable schema field in practice — the effective cap is
  computed via a native call. `NativeInterop.cs:98-99` exposes
  `GetMaxHealth(void*) -> int` and `Heal(void*, float) -> int` as the
  authoritative path (deathmatch-73f32122).
- `GetMaxHealth()` returns **0 for several ticks** after `player_respawned`
  / `player_hero_changed`. Writing Health at event-fire time leaves the
  player at 0 HP. Fix: poll up to 20 ticks (`Timer.Once(1.Ticks(), ...)`)
  before writing.

## Heroes

- `Heroes` enum values mostly match Valve hero IDs but enum **names are
  codenames**, not display names (server-plugins-65d13a2e):
  - `Inferno = 1` → displayed as "Infernus"
  - `Hornet = 3` → "Vindicta"
  - `Orion = 17` → "Grey Talon"
  - `Krill = 18` → "Mo & Krill"
- `HeroTypeExtensions._displayNames` is authoritative; enum→`hero_*` string
  is done by camelCase regex split.
- `CitadelHeroData.AvailableInGame` is a composite:
  `PlayerSelectable && !Disabled && !InDevelopment && !NeedsTesting && !PrereleaseOnly && !LimitedTesting`.
  Filter matchable heroes through this to exclude dev/disabled/prerelease.
- Hero assignment: `controller.SelectHero(Heroes.Warden)` on a
  `CCitadelPlayerController`. Under the hood it marshals to a native cdecl
  callback at `NativeInterop.cs:72`. Real usage in
  `TagPlugin`/`DeathmatchPlugin` (deadworks-88df5d67). Fires
  `player_hero_changed` event.

## NPC / walker / building classnames

Stripped by [[deathmatch]] when deathmatch mode loads (deathmatch-3636296d):

- `npc_boss_tier1` = Guardian
- `npc_boss_tier2` = **Walker** (Tier 2 tower, per-team, carries
  `m_eLaneColor`)
- `npc_boss_tier3` = Base Guardian / Shrine
- `npc_barrack_boss` = Patron
- `npc_base_defense_sentry`
- `npc_trooper_boss`

Walker (`npc_boss_tier2`) is the most useful fixed landmark for lane-based
plugins; positions can be captured at startup loop and from
`OnEntitySpawned`. `ent.TeamNum` identifies side. `m_eLaneColor` identifies
lane, with bearing-angle fallback when the schema read races the spawn.

Seen on idle `dl_midtown` — useful for plugin smoke tests
(server-plugins-4327d1b2, server-plugins-d63499d3):

- `combine_watcher_blue` — readable 4000/4000 HP
- `combine_t2_boss_purple`
- `960_box`, `bounce_pad_sound`, `fog_blinded`

## Flex slot mechanics

Flex-slot unlock requires BOTH (deathmatch-e6b640b7):

- `m_bFlexSlotsForcedUnlocked` (bool) on `CCitadelGameRules`
- `m_nFlexSlotsUnlocked` (short bitmask) on **every** `CCitadelTeam` entity

Bits in the team bitmask:

- `Kill2Tier1 = 0x1`
- `Kill1Tier2 = 0x2`
- `Kill2Tier2 = 0x4`
- `BaseGuardians = 0x8`

Set `0xF` on every `CCitadelTeam` to force all four. Writes must be
re-applied at several lifecycle points (startup-after-delay,
`OnClientFullConnect`, `OnPlayerHeroChanged`, `OnPlayerRespawned`) — writing
once at startup is insufficient because the team may not have networked
its initial state yet.

## Team change, auto-balancer

- `controller.ChangeTeam(int)` is server-initiated and bypasses the
  client-side team-picker prompt AND the `changeteam`/`jointeam` console
  command hook path (deathmatch-e6b640b7). So blocking those concommands in
  `OnClientConCommand` does not disable the server-side auto-balancer.
- The engine's auto-balancer can run before `OnClientFullConnect` completes
  full placement; `controller.TeamNum` inside that hook reflects whatever
  team the engine assigned at join time.
- `friendly_fire`: `mp_friendlyfire = 0` is default; stock behaviour blocks
  same-team bullet damage, but ability AoE/grenades/debuff pulses are NOT
  guaranteed to respect team — plugins must add explicit checks.

## Player disconnect cleanup

**`citadel_kick_disconnected_players`** is the engine's native concommand
for clearing out player slots whose clients are no longer connected.
`server.dll` help text (verbatim):

> Clear out all players who aren't connected, removing them from any teams

It is a verb/concommand, not a standing convar — invoke it imperatively
via `Server.ExecuteCommand`. Flag category unconfirmed; lives adjacent
to cheat/development concommands in the string table, so bracket with
`sv_cheats 1/0` in the same style as `FlexSlotUnlock.cs:29-31`:

```csharp
Server.ExecuteCommand("sv_cheats 1");
Server.ExecuteCommand("citadel_kick_disconnected_players");
Server.ExecuteCommand("sv_cheats 0");
```

Current repo pattern does this work manually inside plugin
`OnClientDisconnect` handlers — [[deathmatch|Deathmatch]]
(`Deathmatch.cs:983-985`) and [[trooper-invasion|TrooperInvasion]]
(`TrooperInvasion.cs:899-902`) both call `pawn.Remove()` +
`controller.Remove()` per disconnecting slot. The native convar is a
candidate replacement for just those two lines — the help text's "and
removing them from any teams" suggests it also touches the team roster
side, which the manual path does not do explicitly. Untested as of
2026-04-23.

[[lock-timer|LockTimer]] `OnClientDisconnect` only clears plugin-
internal dicts (engine per-slot state, HUD maps); the convar doesn't
apply there. [[status-poker]], FlexSlotUnlock, HealOnSpawn, HeroSelect,
Hostname, and TeamChangeBlock do not define `OnClientDisconnect` at all.

Other potential uses (not currently implemented):

- Periodic janitor (`Timer.Every(…)`) to catch any slot the engine
  marks disconnected but managed `OnClientDisconnect` missed.
- Round-reset sweep — e.g. in TrooperInvasion after
  `DisarmWaves("last player disconnected")` at `TrooperInvasion.cs:910`.
- `OnStartupServer` defensive call.

Caveats: it's a bulk sweep (scans all slots) so per-disconnect
invocation still does full work; FCVAR flags need verification on first
use; unknown whether it re-fires any `player_*` events that plugin
hooks might observe.

## Pause ConVars (server.dll)

Full list (custom-server-a6b83c6e):

- `citadel_toggle_server_pause` — the one you actually call
- `citadel_allow_pause_in_match`, `citadel_pause_count` (0=unlimited),
  `citadel_num_team_pauses_allowed`, `citadel_pause_cooldown_time`,
  `citadel_pause_countdown`, `citadel_pause_minimum_time`,
  `citadel_pause_force_unpause_time`, `citadel_unpause_countdown`,
  `citadel_pause_resume_time`, `citadel_pause_resume_time_disconnected`,
  `citadel_force_unpause_cooldown`, `citadel_pause_matchtime_before_allow`,
  `sv_pause_on_tick`

Bypass match-pause limits: `citadel_allow_pause_in_match 1; citadel_pause_count 0; citadel_pause_force_unpause_time 0; citadel_pause_countdown 0`.

Many pause ConVars are FCVAR_CHEAT: `citadel_pause_minimum_time`,
`citadel_pregame_wait_duration`, `citadel_match_intro_duration_*` — need
`sv_cheats 1` first (server-plugins-1b75db40).

`citadel_toggle_server_pause` requires a `PlayerId` arg (designed for
player-initiated pause). Debug string:
`CCitadelGameRules:Pause = true PlayerId=%d fDelay=%4.2f`.

Chat token strings visible in the binary: `CITADEL_CHAT_MESSAGE_CANTPAUSEYET`,
`NOPAUSESLEFT`, `AUTO_UNPAUSED`, `PAUSE_COUNTDOWN`, `UNPAUSE_COUNTDOWN`,
`NOTEAMPAUSESLEFT`, `CANTUNPAUSETEAM`.

## Match / bot-match ConVars

From `docs/rcon-commands.md` in the custom-server repo
(custom-server-2c3ccbd4, server-plugins-1b75db40):

- `citadel_solo_bot_match 1`, `citadel_one_on_one_match`
- `citadel_activate_cps_for_team`, `citadel_assume_pawn_control`
- `citadel_create_unit [hero_name]` (flagged `sv, vconsole_fuzzy`)
- `citadel_boss_tier_3_testing_reset`, `citadel_bot_give_team_gold`
- `citadel_disable_*` / `citadel_enable_*` toggles for duplicate heroes,
  fast cooldowns, fast stamina, no-hero-death, unlimited ammo
- `citadel_player_spawn_time_max_respawn_time` caps max respawn time but
  the engine still enforces a minimum respawn floor (deathmatch-980b8b28).

## RCON commands docs

`docs/rcon-commands.md` in the `deadlock-custom-server` repo has a 506-line
table of Deadlock RCON commands with flags (`sv`, `cheat`, `release`,
`vconsole_fuzzy`, `norecord`) — the reference list for everything the
engine exposes (custom-server-2c3ccbd4).

## Launch args (typical)

Canonical Deadlock dedicated server launch flags
(server-plugins-9df5d718, server-plugins-d63499d3, custom-server-9a7f664c):

```
-dedicated -console -usercon -condebug
+ip 0.0.0.0 -port <N> -netconport <N>
-allow_no_lobby_connect
-game citadel +map <map>
```

Deadworks' default (`startup.cpp:63`, deadworks-52a01b09,
deadworks-ddfface7): `-dedicated -console -dev -insecure
-allow_no_lobby_connect +tv_citadel_auto_record 0 +spec_replay_enable 0
+tv_enable 0 +citadel_upload_replay_enabled 0 +hostport 27015 +map dl_midtown`.

Without a GSLT, Valve's GC kicks the server after ~18s with
`SteamLearn: Invalid HMAC encoding`, exit code 5. Workaround: `-insecure`
skips VAC/GC auth — server stays up indefinitely
(server-plugins-d63499d3).

## Server config file

`game/citadel/cfg/server.cfg` is the startup cfg Deadlock loads (standard
Source layout under `game/<mod>/cfg/`). Values set here are seen by the
server browser before `OnStartupServer` fires in plugins — set authoritative
things like `hostname` there or via `+hostname` launch arg, not via
in-plugin `ConVar.Find("hostname")?.SetString(...)` which displays briefly
under the default before propagating (deathmatch-e6b640b7).

## Autoconnect URL

`steam://run/1422450//-connect 127.0.0.1:27016/` (invoked via `xdg-open`
in mise tasks; server-plugins-34752d6a).
