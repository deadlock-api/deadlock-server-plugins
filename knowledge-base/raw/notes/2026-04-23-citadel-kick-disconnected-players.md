---
date: 2026-04-23
task: catalogue citadel_kick_disconnected_players and evaluate whether any plugin could use it
files:
  - Deathmatch/Deathmatch.cs
  - TrooperInvasion/TrooperInvasion.cs
  - LockTimer/LockTimerPlugin.cs
  - FlexSlotUnlock/FlexSlotUnlock.cs
---

## The convar

Confirmed via `strings game/server.dll` (custom-server build):

```
Clear out all players who aren't connected, removing them from any teams
citadel_kick_disconnected_players
```

It is a concommand (verb), not a standing convar — imperative "run once to
clean up". The help string describes exactly what two of this repo's
plugins currently do by hand in `OnClientDisconnect`:
`pawn.Remove()` + `controller.Remove()`.

Flag category unknown from strings alone; it lives adjacent to
development/cheat-side concommands (`citadel_guide_bot_say`, cinematic
restart, drop debug) in the dumped string table, so safest invocation
idiom is the repo's existing `sv_cheats 1` bracket per
`FlexSlotUnlock.cs:29-31`:

```csharp
Server.ExecuteCommand("sv_cheats 1");
Server.ExecuteCommand("citadel_kick_disconnected_players");
Server.ExecuteCommand("sv_cheats 0");
```

Repo convention for mid-frame concommand invocation is
`Server.ExecuteCommand` (not `ConVar.Find().Set*`) — documented in the
2026-04-22 TrooperInvasion ingest; direct `Set*` crashes natively for
some convars.

## Where it could replace existing code

**Deathmatch/Deathmatch.cs:970-986** — `OnClientDisconnect` manually calls
`pawn.Remove()` + `controller.Remove()`. The native concommand does the
same cleanup plus "removing them from any teams" (i.e. also touches the
team roster side), which the manual path doesn't do explicitly.

**TrooperInvasion/TrooperInvasion.cs:867-916** — same manual
`pawn.Remove()` + `controller.Remove()` at the end of
`OnClientDisconnect`. Replaceable in principle; but the method does a lot
of other per-slot bookkeeping (`_starterGoldSeeded`, `_voteSkipSlots`,
`_playerJoinTimes`, PostHog `ti_player_left`, decrement `_humanCount`)
that must stay. The native call would only substitute the two `Remove`
lines.

**LockTimer/LockTimerPlugin.cs:132-146** — `OnClientDisconnect` only
clears plugin-internal dictionaries (`_engine.Remove(slot)`, HUD per-slot
state, SteamID map). No entity `Remove()` calls. The native convar is
not relevant here.

**StatusPoker, FlexSlotUnlock, HealOnSpawn, HeroSelect, Hostname,
TeamChangeBlock** — none define `OnClientDisconnect`. Not applicable.

## Possible new uses (speculative — not currently implemented)

- **Periodic janitor** (`Timer.Every(60.Seconds())`) to sweep any slot
  the engine tracked as "disconnected but still resident". Useful if
  there's ever a suspicion that the per-plugin `OnClientDisconnect` can
  miss a path (e.g. engine disconnect that skips the managed hook).
- **Round reset** in TrooperInvasion — after `DisarmWaves` at last-player
  disconnect (`TrooperInvasion.cs:904-915`), a defensive call would
  guarantee no roster ghosts survive into a fresh join.
- **Map start** (`OnStartupServer`) — already clean at that point in
  practice, but zero-cost insurance.

## Caveats / unknowns

- Exact FCVAR flags unconfirmed — could be `FCVAR_CHEAT`,
  `FCVAR_DEVELOPMENTONLY`, `FCVAR_SERVER_CAN_EXECUTE`, or combos.
  `sv_cheats` bracketing is the safe default.
- It is a bulk op — calling it per-disconnect still performs a full
  "scan all slots" sweep. Cost probably negligible (32 slots max) but
  noted.
- Unknown whether it fires any game events (`player_disconnect` etc.)
  that plugin managed hooks might re-observe. Current code path
  (`pawn.Remove() + controller.Remove()`) runs *after*
  `OnClientDisconnect` is already dispatched, so event re-entrancy is
  unlikely to matter in that callsite. For a periodic janitor or
  round-reset sweep, this is worth verifying on first use.
- Not tested in this session — this note is a catalogue entry only.
