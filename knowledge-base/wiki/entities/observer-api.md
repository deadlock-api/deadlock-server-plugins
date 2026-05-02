---
title: Observer / Spectator API
type: entity
sources:
  - raw/articles/deadworks-0.4.7-release.md
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CPlayer_ObserverServices.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CBasePlayerPawn.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CCitadelPlayerController.cs
  - ../deadworks/managed/DeadworksManaged.Api/Enums/ObserverMode_t.cs
  - ../deadworks/src/Core/NativeCallbacks.cpp
  - ../deadworks/config/deadworks_mem.jsonc
related:
  - "[[plugin-api-surface]]"
  - "[[schema-accessors]]"
  - "[[events-surface]]"
  - "[[deadworks-0.4.7-release]]"
  - "[[deadlock-game]]"
created: 2026-05-02
updated: 2026-05-02
confidence: high
---

# Observer / Spectator API

Added in **v0.4.7** (`5c9a1e6`). Exposes the spectator/observer state component
attached to pawns, and a high-level helper to move a connected player into pure
spectator mode.

## `ObserverMode_t` enum

```csharp
public enum ObserverMode_t : uint {
    None    = 0,
    Fixed   = 1,   // fixed camera at current position
    InEye   = 2,   // first-person, inside observed entity
    Chase   = 3,   // third-person follow cam
    Roaming = 4,   // free-fly noclip cam
}
```

Stored in `CPlayer_ObserverServices.m_iObserverMode` as a networked `byte`.

## `CPlayer_ObserverServices`

Schema-backed component on `CBasePlayerPawn` accessed via `m_pObserverServices`.
Not every pawn has it — the pointer is null on hero pawns (combat-mode players)
and non-null on observer pawns.

### Members

| Member | Type | Description |
|--------|------|-------------|
| `ObserverMode` | `ObserverMode_t` (read) | Current observer mode, from schema `m_iObserverMode` |
| `ObserverLastMode` | `ObserverMode_t` (read/write) | Previous non-zero mode; restored on re-entry to observer state. Schema `m_iObserverLastMode` |
| `ObserverTarget` | `CBaseEntity?` (read) | Currently observed entity. Null if no target. Schema `m_hObserverTarget` (handle) |
| `IsValidObserverTarget(entity)` | `bool` | Managed validity check (see below) |
| `SetObserverMode(mode)` | `void` | vtable call idx 28 — `CPlayer_ObserverServices::SetObserverMode` |
| `SetObserverTarget(entity)` | `bool` | vtable call idx 25 — `CPlayer_ObserverServices::SetObserverTarget`; returns false on failure |

### `IsValidObserverTarget` logic

Managed validation (no native call):
```csharp
if (target is null || !target.IsValid || !target.IsAlive) return false;
if ((target.Flags & EntityFlags.Frozen) != 0) return false;
if (target.TeamNum == 3) return false;
return true;
```

**Gotcha for TrooperInvasion:** team 3 is the trooper team. This check means players
cannot spectate enemy troopers via the default validation path. It only affects the
managed `IsValidObserverTarget` helper — `SetObserverTarget` itself does not call it;
plugins can pass any target they choose.

## `CBasePlayerPawn` convenience properties

Pass-through delegation to `ObserverServices`:

```csharp
CPlayer_ObserverServices? pawn.ObserverServices  // null if no component allocated
ObserverMode_t            pawn.ObserverMode       // ObserverMode_t.None if null
CBaseEntity?              pawn.ObserverTarget     // null if no target / null services
void                      pawn.SetObserverMode(ObserverMode_t mode)
bool                      pawn.SetObserverTarget(CBaseEntity? target)
bool                      pawn.IsValidObserverTarget(CBaseEntity? target)
```

All methods silently no-op (or return `false`) if `ObserverServices` is null.

## `CCitadelPlayerController.MakeObserver()`

High-level helper to move a connected player into pure spectator mode:

```csharp
controller.MakeObserver();
// Equivalent to:
// pawn?.Remove();
// controller.SetPawn(null, retainOldPawnTeam: true);
// NativeInterop.SpawnObserverPawn(controller);
```

Removes the current hero pawn, spawns a new observer pawn via the native
`CCitadelPlayerController::SpawnObserverPawn` signature, and attaches it to the
controller. After this call, `controller.Pawn` resolves to an observer pawn with
`ObserverServices` available.

**Recommended use:** call once per player when your gamemode puts them in spectator
state (e.g., on death in a deathmatch mode with limited lives, or before a forced
hero swap to avoid a one-tick window with no pawn).

## Native signatures added (`deadworks_mem.jsonc`)

| Key | Use |
|-----|-----|
| `"CCitadelPlayerController::SpawnObserverPawn"` | `MakeObserver` |
| vtable `"CPlayer_ObserverServices::SetObserverTarget"` idx 25 | `SetObserverTarget` |
| vtable `"CPlayer_ObserverServices::SetObserverMode"` idx 28 | `SetObserverMode` |

`"CBasePlayerController::SetPawn"` signature also updated in v0.4.7 (minor
pattern refinement; existing usage unaffected).

## Current usage in this repo

No plugin in `deadlock-server-plugins/` currently uses the observer API.

**Candidate uses:**
- TrooperInvasion: place a defeated player in observer mode (spectating teammates)
  between respawns if a limited-lives mechanic is added.
- Deathmatch: spectator mode after round completion.
- Any mode that needs to differentiate "actively playing" vs "watching" per player.
