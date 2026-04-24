---
title: Entity System & Schema Access
type: entity
sources:
  - raw/notes/2026-04-22-deadworks-schema-accessors.md
  - knowledge-base/raw/articles/deadworks-0.4.6-release.md
  - ../deadworks/managed/DeadworksManaged.Api/Entities/SchemaAccessor.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/SchemaArrayAccessor.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/SchemaStringAccessor.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/Players.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/NativeEntityFactory.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/NativeClassAttribute.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/EntityData.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CBaseEntity.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/Entities.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/AbilityResource.cs
related:
  - "[[plugin-api-surface]]"
  - "[[source-2-engine]]"
  - "[[deadworks-mem-jsonc]]"
  - "[[events-surface]]"
  - "[[deadworks-0.4.6-release]]"
created: 2026-04-22
updated: 2026-04-24
confidence: high
---

# Entity System & Schema Access

The plugin-facing entity surface covers three layers: **wrapper types**
(`CBaseEntity` and subclasses), **schema accessors** (field-level
read/write via cached offsets), and **registries** (`Players` for slot
enumeration, `EntityData<T>` for per-entity plugin state).

## `SchemaAccessor<T>` — read/write a networked field

`SchemaAccessor<T> where T : unmanaged` caches a field offset on first
access. Constructor takes **UTF-8 byte spans** (`"..."u8` literals):

```csharp
private static readonly SchemaAccessor<int> HealthAccessor =
    new("CBaseEntity"u8, "m_iHealth"u8);

// later:
int hp = HealthAccessor.Get(entity);
HealthAccessor.Set(entity, 100);  // auto-notifies network if networked
```

Why UTF-8 literals: the accessor copies bytes and null-terminates them
for C interop (`_className = new byte[className.Length + 1]`). Allocation
happens once at construction; the native scan happens lazily on first
`Get`/`Set`.

Members:
- `int Offset { get; }` — resolves if not yet resolved
- `short ChainOffset { get; }` — for network state change chains
- `nint GetAddress(nint entity)` — raw field pointer for unsafe ops
- `T Get(nint entity)` — reads `*(T*)((byte*)entity + _offset)`
- `T Set(nint entity, T value)` — writes + auto-calls
  `NativeInterop.NotifyStateChanged` if `_networked` was true at
  resolve-time

**Networked propagation is automatic.** You do not manually call
`NotifyStateChanged` after `Set` — the accessor tracks
whether the field was networked at resolve-time and fires the
notification itself.

Concurrency: `_offset` is `volatile int _offset = -1`, written **last**
in `Resolve()` so a concurrent reader either sees `-1` (re-resolves,
idempotent) or a fully-populated set of fields.

### `SchemaArrayAccessor<T>`

Same pattern for fixed-size arrays. `Get(entity, index)` and
`Set(entity, index, value)` — offset arithmetic is
`_offset + index * sizeof(T)`.

### `SchemaStringAccessor` (write-only)

For `CUtlSymbolLarge` string fields. `Set(entity, string value)` encodes
via `Utf8.Encode` and calls native `SetSchemaString`. **No `Get` path** —
managed side cannot read symbol table strings.

### Backing path

The native `NativeInterop.GetSchemaField` implementation uses the
[[source-2-engine|scan-first-then-vtable]] pattern — binary-scan
`server.dll` for the class name, find the `SchemaClassInfoData_t` by
pointer equality, iterate the fields array. The managed side is
unaware of this; it just gets an `(offset, chainOffset, networked)`
triple back. See [[deadworks-mem-jsonc]] for the memory signatures
involved.

## `Players` — connected-player enumeration

```csharp
public const int MaxSlot = 31;   // slot range 0-30
```

| Method | Returns |
|--------|---------|
| `IsConnected(slot)` | `bool` — fast check against internal `bool[31] _connected` |
| `GetAll()` | connected `CCitadelPlayerController` list |
| `GetAllControllers()` | ALL existing controllers (including mid-disconnect) |
| `GetAllPawns()` | hero pawns for connected players |
| `FromSlot(slot)` | `CCitadelPlayerController?` O(1) lookup |

State lifecycle:
- `SetConnected(slot, true)` fires on `ClientFullConnect` (from
  `EntryPoint.cs`)
- `ResetAll()` fires on map change / server startup

**Use `GetAll()` by default.** `GetAllControllers` includes lingering
entities that may not have networked state yet.

**`MaxSlot == 31` but `RecipientFilter.All` masks 64 bits** and
`CheckTransmitEvent` iterates 64-bit transmit bitmap words. The
underlying infrastructure handles 64; 31 is the documented max player
slot count.

## Slot mapping gotcha

Controller `EntityIndex - 1` == slot (NOT the entity index directly).
**v0.4.5 adds `CBasePlayerController.Slot` as the canonical
replacement** for the `-1` idiom — prefer `Slot` over `EntityIndex - 1`
in new code.

## `NativeClassAttribute` + `NativeEntityFactory`

Maps C# wrapper types to accepted C++ DLL class names. Used for
`CBaseEntity.As<T>()` type checks and factory construction.

```csharp
[NativeClass("CCitadelPlayerPawn", "CCitadelPlayerPawnCustomized")]
public sealed class CCitadelPlayerPawn : CBasePlayerPawn { ... }
```

Without `[NativeClass]`, the default accepted name is the C# type name
itself. `NativeEntityFactory.Create<T>(nint handle)` caches a constructor
delegate per type — wrapper instantiation is near-zero cost after warmup.

## `EntityData<T>` — auto-cleaning per-entity state

**Keyed by `uint EntityHandle`**, not `nint` pointer. Handles carry a
generation counter that invalidates across pointer reuse, so stale
handles don't collide with newly-spawned entities.

API:
- `new EntityData<T>()` — auto-registers with `EntityDataRegistry` (weak ref)
- `this[entity] = value` / `this[entity]` indexer
- `TryGet(entity, out T)`
- `GetOrAdd(entity, defaultValue)` and `GetOrAdd(entity, Func<T>)`
- `Has(entity)`, `Remove(entity)`, `Clear()`
- **(v0.4.6)** `Count` — number of entries currently stored
- **(v0.4.6)** `IEnumerable<KeyValuePair<CBaseEntity, T>>` — iterate via
  `foreach (var kvp in _data)`. Each yielded key is `new CBaseEntity(handle)`.
  **Do not add/remove entries during iteration** (standard dictionary
  enumeration caveat — doc comment on `GetEnumerator` calls this out
  explicitly).

**Global auto-cleanup on entity delete.** `EntityDataRegistry.OnEntityDeleted(uint handle)`
iterates all registered stores (weak references, pruned on iteration)
and removes the entry. No plugin action needed to clean up per-entity
state when an entity is destroyed.

Canonical pattern (`ScourgePlugin.cs`):

```csharp
private static readonly EntityData<IHandle> _dotTimers = new();

public override HookResult OnTakeDamage(TakeDamageEvent args) {
    var pawn = args.Entity.As<CCitadelPlayerPawn>();
    if (pawn == null) return HookResult.Continue;

    if (_dotTimers.TryGet(pawn, out var existing))
        existing.Cancel();   // replace any prior DOT

    var handle = Timer.Sequence(step => { /* ... */ });
    _dotTimers[pawn] = handle;
    return HookResult.Continue;
}
```

When the pawn dies or the map ends, `_dotTimers` auto-purges — no
`OnEntityDeleted` handling needed.

## Cast-and-null-check pattern

```csharp
var pawn = args.Entity.As<CCitadelPlayerPawn>();
if (pawn == null) return HookResult.Continue;
// use pawn safely
```

`As<T>()` does the `NativeEntityFactory.IsMatch<T>` class-name check and
returns typed wrapper or null. This is the idiomatic path instead of raw
`(CCitadelPlayerPawn)entity` casts.

## `CBaseEntity` equality (v0.4.6)

`CBaseEntity` implements `IEquatable<CBaseEntity>` with `==` / `!=`
operators, `Equals(object?)`, and `GetHashCode()` all derived from
`EntityHandle` (the packed serial + index `uint`). Two wrappers that
point at the same native entity compare equal regardless of wrapper
type or wrapper reference identity.

```csharp
var a = args.Entity;
var b = Players.FromSlot(0)?.Pawn;
if (a == b) { /* same native entity */ }

// Safe as dictionary key:
var state = new Dictionary<CBaseEntity, int>();
state[a] = 7;
```

Prior to v0.4.6, equality fell back to reference-equality on the
managed wrapper — two wrappers for the same entity could compare
unequal. Collections keyed by `CBaseEntity` worked by accident before
and work by design now. Commit `f1f83e6`.

> Commit subject says "EntityIndex-based equality" but the
> implementation compares full `EntityHandle` (serial + index), so
> wrappers for a re-used entity slot with a bumped serial compare
> unequal. That's the safer semantic.

## `Entities` — query helpers (v0.4.6)

Static class under `DeadworksManaged.Api` for querying the server's
entity list. Four enumerator-style methods plus three cursor-based
targetname lookups:

| Method | Returns |
|---|---|
| `Entities.All` | all valid entities (scans 32768-slot entity list) |
| `Entities.ByClass<T>()` | typed wrappers for every entity matching native class `T` |
| `Entities.ByDesignerName(name)` | every entity whose designer name equals `name` (ordinal) |
| **`Entities.FirstByName(name)` (v0.4.6)** | `CBaseEntity?` — first entity with matching targetname |
| **`Entities.FirstByName<T>(name)` (v0.4.6)** | `T?` — first match whose native class also satisfies `T` |
| **`Entities.ByName(name)` (v0.4.6)** | `IEnumerable<CBaseEntity>` — all matching targetname |
| **`Entities.ByName<T>(name)` (v0.4.6)** | `IEnumerable<T>` — all matching targetname + class |

Targetname lookups are **case-sensitive** per the XML doc comments.
They back onto a cursor-style `NativeInterop.FindEntityByName(cursor,
name)` callback that walks the engine's internal targetname index —
faster than `Entities.All.Where(e => e.GetTargetname() == name)` for
large entity counts. Candidate replacement for any mapper-wired
targetname scan.

Distinction:
- `ByClass<T>` matches by **native DLL class** (e.g.
  `npc_trooper_boss`) — what `CBaseEntity.As<T>()` checks.
- `ByDesignerName` matches by **designer name** (entity classname
  string used in `CreateByDesignerName`).
- `ByName`/`FirstByName` match by **targetname** (the `m_iName`
  identifier authored in Hammer / the KV block), not the classname.

## Entity type reference

Files under `Entities/`:
- `CBaseEntity` — root wrapper; v0.4.6 adds handle-based equality
  operators and `IEquatable<CBaseEntity>`
- `CBaseModifier`, `CBodyComponent`, `CGameSceneNode`, `CPointWorldText`
- `CCitadelModifierVData`, `CModifierVData`, `CEntitySubclassVDataBase`
  — modifier/subclass vdata wrappers
- `CEntityKeyValues` — kv3-backed entity properties
- `CallbackHandle` — unified handle impl for callback-style subscriptions
- `PlayerEntities.cs` — player-specific wrapper class groupings
- `AbilityResource` — wraps `AbilityResource_t` (stamina / ability
  resource with latch-based networking). v0.4.6 fix: `LatchTime` /
  `LatchValue` setters now fire `NotifyStateChanged` (previously did
  raw pointer writes that bypassed network notification, so
  client-side latch state drifted after plugin writes).
