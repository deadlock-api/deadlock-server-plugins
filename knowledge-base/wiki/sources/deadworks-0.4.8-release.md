---
title: "Deadworks v0.4.8 release notes"
type: source-summary
sources:
  - knowledge-base/raw/notes/2026-05-13-deadworks-0.4.8-release.md
related:
  - "[[deadworks-runtime]]"
  - "[[plugin-api-surface]]"
  - "[[schema-accessors]]"
  - "[[deadworks-0.4.7-release]]"
  - "[[deadworks-mem-jsonc]]"
created: 2026-05-13
updated: 2026-05-13
confidence: high
---

# Deadworks v0.4.8 Release Notes

Source: GitHub release tag `v0.4.8` + 5 commits between `v0.4.7` and
`v0.4.8` on `Deadworks-net/deadworks`, verified by `git log v0.4.7..v0.4.8 --oneline`.

## Commit map

| SHA | Date | Subject | Plugin impact |
|-----|------|---------|---------------|
| `1c3554a` | 2026-05-02 | expose CBaseEntity.Friction, CCitadelPlayerPawn.RespawnTime | new schema properties |
| `67ac6e9` | 2026-05-06 | expose entity collision | new `CCollisionProperty` + `BoundingBox` |
| `11cff37` | 2026-05-09 | add linux path detection for the launcher (#17) | none — launcher only |
| `93a3dd2` | 2026-05-09 | hook and force false CServerSideClientBase::IsReservedSlot | native fix; no managed API |
| `36d481a` | 2026-05-09 | Merge branch 'main' of …/deadworks | merge commit; no delta |

## What changed — managed API

### `CBaseEntity.Friction` (commit `1c3554a`)

New read/write `float` property on `CBaseEntity`:

```csharp
private static readonly SchemaAccessor<float> _flFriction =
    new("CBaseEntity"u8, "m_flFriction"u8);
public float Friction { get => _flFriction.Get(Handle); set => _flFriction.Set(Handle, value); }
```

Standard schema accessor — `Set` auto-notifies network if the field is
networked at resolve-time. No special coupling.

### `CCitadelPlayerPawn.RespawnTime` (commit `1c3554a`)

New read/write `float` property on `CCitadelPlayerPawn`:

```csharp
private static readonly SchemaAccessor<float> _flRespawnTime =
    new("CCitadelPlayerPawn"u8, "m_flRespawnTime"u8);
public float RespawnTime { get => _flRespawnTime.Get(Handle); set => _flRespawnTime.Set(Handle, value); }
```

Lets plugins read or override how long a player stays dead before
respawning. Direct schema write — ensure the game state allows writes
(e.g. don't write during `OnEntitySpawned` before the pawn is fully
initialised).

### `CBaseEntity.Collision` + `CCollisionProperty` + `BoundingBox` (commit `67ac6e9`)

New property on `CBaseEntity`:

```csharp
public CCollisionProperty? Collision { get; }
```

Returns `null` when the entity's `m_pCollision` pointer is zero (entities
without a collision representation). **Always null-check.**

`CCollisionProperty` (new class, `Entities/CCollisionProperty.cs`):

| Member | Type | Notes |
|--------|------|-------|
| `Owner` | `CBaseEntity` | The entity this collision belongs to |
| `Mins` | `Vector3` | Lower OBB corner in **local space** — read/write |
| `Maxs` | `Vector3` | Upper OBB corner in **local space** — read/write |
| `BoundingRadius` | `float` | Schema `m_flBoundingRadius` — read-only |
| `WorldMins` | `Vector3` | `Mins + Owner.Position` — **assumes identity rotation** |
| `WorldMaxs` | `Vector3` | `Maxs + Owner.Position` — **assumes identity rotation** |
| `Contains(Vector3)` | `bool` | Inclusive AABB test in world space (uses `WorldMins`/`WorldMaxs`) |
| `IsValid` | `bool` | `Handle != 0 && Owner.IsValid` |

**Rotation caveat:** `WorldMins`/`WorldMaxs` are computed as
`local OBB corner + AbsOrigin`. They are accurate for entities with
identity rotation (many map geometry entities, most NPCs in standing
pose). For rotated entities (doors, angled props, ragdolls) the AABB
is incorrect — the local OBB corners need to be multiplied by the
entity's rotation matrix for a correct world-space AABB.

`BoundingBox` static utility (new class, `Math/BoundingBox.cs`):

```csharp
BoundingBox.Contains(mins, maxs, point)  // inclusive per-axis
```

Used internally by `CCollisionProperty.Contains`. Plugins can use it
directly for custom bounding-box checks with arbitrary mins/maxs.

## What changed — native

### `CServerSideClientBase::IsReservedSlot` hook (commit `93a3dd2`)

A new safetyhook inline trampoline in `PostInit` unconditionally returns
`false` from `CServerSideClientBase::IsReservedSlot`:

```cpp
bool __fastcall Hook_CServerSideClientBase_IsReservedSlot(CServerSideClientBase *thisptr) {
    return false;
}
```

New signature entry in `deadworks_mem.jsonc`:

```jsonc
"CServerSideClientBase::IsReservedSlot": {
    "library": "engine2.dll",
    "windows": "40 53 48 83 EC ?? 83 B9 ?? ?? ?? ?? ?? 48 8B D9 0F 84"
}
```

The engine's slot-reservation system allowed servers to keep certain
slots open for "reserved" accounts. When all unreserved slots were
full, non-reserved clients received a "server full" reject even if
reserved slots were vacant. This caused servers to appear full and
block new connections.

**This hook is not registered in `deadworks_mem.jsonc` as a required
signature** — the startup `ValidateSignatures` crash-on-miss check only
covers the entries in the required list. The `IsReservedSlot` sig is
present but if it ever goes stale the hook will silently not install
(no startup crash), and slot reservation will resume blocking joins.

**No managed API.** There is no `[Command]` or plugin hook to toggle
this behaviour from plugin code.

## Impact on plugins in this repo

- **No breaking changes.** No plugin needs modification.
- **No deprecations** added or removed.
- New APIs are purely additive. None of the four plugins currently use
  `Collision`, `Friction`, or `RespawnTime`.
- **Candidate future uses:**
  - `RespawnTime`: TrooperInvasion could extend/reduce respawn timers
    as a wave-difficulty mechanic.
  - `Collision.Contains`: zone containment checks (LockTimer already
    uses custom zone logic; `CCollisionProperty.Contains` could
    simplify volume-intersection tests if LockTimer zones were entity-
    backed rather than YAML-defined).
  - `Friction`: slide-based ability mods or environmental effects.

## Key finding / gotcha

**`CCollisionProperty.WorldMins/WorldMaxs` assume identity rotation.**
The source comment is explicit. Any plugin using `Contains` on rotated
entities (not uncommon in Deadlock's map geometry — e.g. angled
corridors, rotating doors) will get wrong results. Use the raw `Mins`/
`Maxs` + entity rotation matrix for correct world-space transforms.
