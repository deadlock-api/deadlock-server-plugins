---
title: "Source: DisconnectCleanup managed-API refactor (commit 1f7ae19)"
type: source-summary
sources:
  - ../DisconnectCleanup/DisconnectCleanup.cs
  - ../deadworks/examples/plugins/TagPlugin/TagPlugin.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/ClientDisconnectedEvent.cs
related:
  - "[[disconnect-cleanup]]"
  - "[[deadlock-game]]"
  - "[[events-surface]]"
created: 2026-05-29
updated: 2026-05-29
confidence: high
---

# Source: DisconnectCleanup managed-API refactor

Commit `1f7ae19` (2026-05-29) reworks `DisconnectCleanup/DisconnectCleanup.cs`
`OnClientDisconnect` to remove the disconnecting player directly via the managed
entity API instead of the cheat-flagged `citadel_kick_disconnected_players`
concommand.

## Before

```csharp
public override void OnClientDisconnect(ClientDisconnectedEvent args)
{
    Server.ExecuteCommand("sv_cheats 1");
    Server.ExecuteCommand("citadel_kick_disconnected_players");
    Server.ExecuteCommand("sv_cheats 0");
}
```

## After

```csharp
public override void OnClientDisconnect(ClientDisconnectedEvent args)
{
    var controller = args.Controller;
    if (controller == null) return;
    controller.GetHeroPawn()?.Remove();
    controller.Remove();
}
```

## Key findings

- **Drops the `sv_cheats` toggle entirely.** The prior approach bracketed the
  cheat-adjacent concommand with `sv_cheats 1/0`; the managed path needs no
  cheat state.
- **Scoped to one client, not a server-wide sweep.** `citadel_kick_disconnected_players`
  clears *all* disconnected slots; the new code removes only the pawn and
  controller of the slot that fired the event.
- **API verified against `../deadworks`:**
  - `ClientDisconnectedEvent.Controller` → `CCitadelPlayerController?` (nullable;
    resolved per-call from `Slot` via `NativeInterop.GetPlayerController`)
    — `ClientDisconnectedEvent.cs:9`.
  - `CCitadelPlayerController.GetHeroPawn()` → `CCitadelPlayerPawn?`
    — `CCitadelPlayerController.cs:12`.
  - `CBaseEntity.Remove()` → `NativeInterop.RemoveEntity` — `CBaseEntity.cs:163`
    (inherited by both pawn and controller).
- **Exact pattern is upstream precedent.** `TagPlugin.cs:137` uses
  `controller.GetHeroPawn()?.Remove();` verbatim.

> Gap: the engine help text for `citadel_kick_disconnected_players` claims it
> also removes players "from any teams" — a roster-side effect the manual
> pawn+controller `Remove()` does not explicitly replicate. Whether team-roster
> state lingers after the managed removal is untested. See [[deadlock-game]].
