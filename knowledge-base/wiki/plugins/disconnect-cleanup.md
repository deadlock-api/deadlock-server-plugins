---
title: DisconnectCleanup plugin
type: plugin
sources:
  - ../DisconnectCleanup/DisconnectCleanup.cs
  - "[[disconnect-cleanup-managed-refactor]]"
related:
  - "[[deadlock-game]]"
  - "[[events-surface]]"
  - "[[deathmatch]]"
  - "[[trooper-invasion]]"
  - "[[lock-timer]]"
created: 2026-05-29
updated: 2026-05-29
confidence: high
---

# DisconnectCleanup plugin

A minimal standalone plugin whose sole job is to remove a disconnecting
player's entities in `OnClientDisconnect`, so dead/orphaned slots don't linger.
It carries no config, no timers, and no per-slot state — just the one hook.

## Behaviour

```csharp
public override void OnClientDisconnect(ClientDisconnectedEvent args)
{
    var controller = args.Controller;
    if (controller == null) return;
    controller.GetHeroPawn()?.Remove();
    controller.Remove();
}
```

- `args.Controller` resolves the `CCitadelPlayerController` for the slot at call
  time and may be null (returns early).
- Removes the hero pawn first (if any), then the controller, via
  `CBaseEntity.Remove()`.

## History — moved off the cheat-flagged concommand

The plugin originally invoked the engine's native
`citadel_kick_disconnected_players` concommand bracketed with `sv_cheats 1/0`
(the pattern recommended in [[deadlock-game]] as of 2026-04-23). Commit
`1f7ae19` (2026-05-29) replaced that with the direct managed-API removal above.
Rationale:

- **No `sv_cheats` toggle.** The concommand sits adjacent to cheat-flagged
  commands; the managed path avoids touching cheat state.
- **Scoped to one client.** `citadel_kick_disconnected_players` sweeps *all*
  disconnected slots server-wide; this removes only the slot that fired the
  event.

The exact `controller.GetHeroPawn()?.Remove();` idiom matches the upstream
`TagPlugin` example. See [[disconnect-cleanup-managed-refactor]] for the full
before/after and API verification.

> Gap: the concommand's help text ("removing them from any teams") implies a
> team-roster cleanup the manual pawn+controller `Remove()` does not explicitly
> perform. Whether roster state lingers after managed removal is untested.

## Relationship to other plugins

[[deathmatch|Deathmatch]] and [[trooper-invasion|TrooperInvasion]] handle
`OnClientDisconnect` for their own session-state bookkeeping (human counts,
stats, per-slot dicts) but — as of current source — no longer call
`pawn.Remove()`/`controller.Remove()` themselves; DisconnectCleanup is the
dedicated entity-removal plugin. [[lock-timer|LockTimer]]'s disconnect handler
only clears plugin-internal state. Whether DisconnectCleanup ships in the same
gamemode profile(s) as those plugins depends on `gamemodes.json` (see
[[plugin-build-pipeline]]).
