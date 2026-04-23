---
title: Trace API (VPhys2)
type: entity
sources:
  - raw/notes/2026-04-23-trace-api.md
  - ../deadworks/managed/DeadworksManaged.Api/Trace/TraceSystem.cs
  - ../deadworks/managed/DeadworksManaged.Api/Trace/TraceShapes.cs
  - ../deadworks/managed/DeadworksManaged.Api/Trace/TraceResults.cs
  - ../deadworks/managed/DeadworksManaged.Api/Trace/TraceEnums.cs
related:
  - "[[plugin-api-surface]]"
  - "[[source-2-engine]]"
  - "[[schema-accessors]]"
  - "[[deadworks-scan-2026-04-23]]"
created: 2026-04-23
updated: 2026-04-23
confidence: high
---

# Trace API (VPhys2)

`DeadworksManaged.Api.Trace` exposes **VPhys2 ray and shape casting**
to plugins. Provides a simple `Trace.Ray(...)` entrypoint for line
casts plus lower-level `SimpleTrace` / `TraceShape` for sphere / hull
/ capsule / mesh queries.

Prior wiki coverage in `[[plugin-api-surface]]:74` summarized it as
*"not used in any example plugin"* — which is still true, but the API
is complete and documented here.

## Entrypoints

```csharp
// High-level: line raycast with sensible defaults
static TraceResult Ray(
    Vector3 start, Vector3 end,
    MaskTrace mask = MaskTrace.Solid | MaskTrace.Hitbox,
    CBaseEntity? ignore = null);

// Mid-level: constructs filter + ray struct for you
static void SimpleTrace(
    Vector3 start, Vector3 end,
    RayType_t rayKind, RnQueryObjectSet objectQuery,
    MaskTrace interactWith, MaskTrace interactExclude, MaskTrace interactAs,
    CollisionGroup collision, ref CGameTrace trace,
    CBaseEntity? filterEntity = null, CBaseEntity? filterSecondEntity = null);

// Mid-level: angles variant
static void SimpleTraceAngles(
    Vector3 start, Vector3 angles, …, float maxDistance = 8192f);

// Low-level: raw shape cast
static void TraceShape(
    Vector3 start, Vector3 end,
    Ray_t ray, CTraceFilter filter, ref CGameTrace trace);
```

## `TraceResult` vs `CGameTrace`

`Trace.Ray` returns `TraceResult`:

| Field | Type | Meaning |
|-------|------|---------|
| `HitPosition` | `Vector3` | `start + (end - start) * Fraction` |
| `Fraction` | `float` | `[0, 1]`; `1.0` = no hit before endpoint |
| `DidHit` | `bool` | hit occurred |
| `Trace` | `CGameTrace` | full 192-byte result for details |

`CGameTrace` is the full native result struct. Relevant fields:

- `HitPoint`, `HitNormal`, `StartPos`, `EndPos` (`Vector3`)
- `Fraction` (`float`), `Distance` (computed)
- `Direction` (computed unit vector)
- `pEntity` — native pointer to the hit entity; wrapped as
  `HitEntity => pEntity != 0 ? new CBaseEntity(pEntity) : null`
- `StartInSolid` (`bool`) — start position was inside geometry
- `HitEntityByDesignerName(name, out ent, NameMatchType matchType)` —
  classname-filtered hit test; `NameMatchType` ∈ `{Exact, StartsWith,
  EndsWith, Contains}`, default `StartsWith`, all ordinal-case-insensitive.

## Ray shapes

`Ray_t` is an explicit-layout union (48 bytes) discriminated by `Type`
(`RayType_t`). Variants:

- `LineTrace` — start offset + radius (radius = 0 for pure line)
- `SphereTrace` — center + radius (swept sphere)
- `HullTrace` — AABB mins/maxs (swept AABB)
- `CapsuleTrace` — two centers + radius
- `MeshTrace` — convex mesh (mins/maxs, vertex pointer, count)

`Ray_t.CreateLine(start)` is the usual factory. The `Init` overloads
on `Ray_t` pick the variant from the argument shape. For shape casts,
construct `Ray_t`, set fields, and call `TraceShape`.

## Silent no-op when not ready

`Trace.TraceShape` returns immediately if `NativeInterop.TraceShapeFn == 0`
or `PhysicsQueryPtr == null` / `*PhysicsQueryPtr == null`
(`TraceSystem.cs:7-9, :13`). `Trace.Ray` returns a default "no hit"
result in the not-ready path. **Safe to call from `OnLoad`** — will just
no-op until the physics query system is wired up during map start.

## Filter vtable gotcha

`CTraceFilter` embeds a native **function-pointer vtable** (2 slots:
destructor + `ShouldHitEntity`). The table is allocated once in the
static constructor of `CTraceFilterVTable` (two variants: `Simple` =
always-hit, `WithEntityFilter` = honours `EntityIdsToIgnore`).

A filter constructed via `default(CTraceFilter)` has `_vtable = 0` —
`EnsureValid()` is called internally before dispatch to paper over
this, but if you bypass that path and call the native trace with a
zero-vtable filter, the engine will crash dereferencing it.

Always use one of:

- `new CTraceFilter()` — `WithEntityFilter` vtable.
- `new CTraceFilter(checkIgnoredEntities: false)` — `Simple` vtable
  (ignores `EntityIdsToIgnore` — faster).

## Default masks for `Trace.Ray`

- Mask: `MaskTrace.Solid | MaskTrace.Hitbox` — world geometry + player
  hitboxes. For world-only use `MaskTrace.Solid`.
- Object set: `RnQueryObjectSet.All`.
- Collision group: `CollisionGroup.Always`.

## Use-cases

None currently. Reasonable candidates:

- **LOS check before particle/effect spawn** — ensure the spawn point
  is visible from a source, avoid through-wall spawns.
- **"What am I looking at" query** — from a player's eye position and
  view angles, `SimpleTraceAngles` → `HitEntity` / `HitEntityByDesignerName`.
- **Auto-aim prototypes** — sphere cast forward and snap to matching
  hitbox.
- **Geometry-snap teleport** — `Trace.Ray` downward from target
  position, teleport to `HitPosition + small offset`.

## Interop internals

Backed by:

- `NativeInterop.TraceShapeFn` — function pointer populated on
  bootstrap.
- `NativeInterop.PhysicsQueryPtr` — double-pointer into the game's
  physics query system (value may be null if physics not ready).

Both are `internal` — plugin code uses the `Trace.*` static API, not
these directly.

## Related pages

- [[plugin-api-surface]] — `Trace/` folder subsystem row.
- [[source-2-engine]] — VPhys2 sits in the Source 2 engine's collision
  system.
- [[schema-accessors]] — `CBaseEntity` wrapper used in `HitEntity`.
