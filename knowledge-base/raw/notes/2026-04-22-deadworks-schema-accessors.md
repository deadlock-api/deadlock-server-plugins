---
date: 2026-04-22
task: scan deadworks entity/schema API
files:
  - ../deadworks/managed/DeadworksManaged.Api/Entities/SchemaAccessor.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/SchemaArrayAccessor.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/SchemaStringAccessor.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/Players.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/NativeEntityFactory.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/NativeClassAttribute.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/EntityData.cs
---

# Entity system and schema access

## SchemaAccessor<T> — the canonical pattern for reading/writing networked fields

`SchemaAccessor<T> where T : unmanaged` caches a field offset on first access.
Constructor takes **UTF-8 byte spans** (class/field names as `"..."u8` literals):

```csharp
private static readonly SchemaAccessor<int> HealthAccessor =
    new("CBaseEntity"u8, "m_iHealth"u8);
```

Why UTF-8 literals: the accessor copies bytes and null-terminates them
(`_className = new byte[className.Length + 1]`) for C interop. Allocation
happens once at construction; scan happens lazily on first `Get`/`Set`.

Methods:
- `int Offset { get; }` — resolves if needed
- `short ChainOffset { get; }` — for network state change chain (chain offset
  is resolved by `NativeInterop.GetSchemaField` and stored)
- `nint GetAddress(nint entity)` — raw field pointer for unsafe ops
- `T Get(nint entity)` — read (`*(T*)((byte*)entity + _offset)`)
- `T Set(nint entity, T value)` — write + auto-calls
  `NotifyStateChanged` if the field was resolved as networked

**Networked propagation is automatic in `Set`.** The old manual
`NotifyStateChanged` call pattern is not needed — the accessor tracks
the `_networked` bool from resolve-time and fires the notification itself.

Concurrency: `_offset` is `volatile int _offset = -1`. `Resolve()` writes
it **last** so a concurrent reader either sees `-1` and re-resolves
(idempotent) or sees a complete offset + chain offset pair.

## SchemaArrayAccessor<T>

Same pattern for fixed-size arrays. `Get(entity, index)` and
`Set(entity, index, value)` — offset arithmetic is
`_offset + index * sizeof(T)`.

## SchemaStringAccessor (write-only, UTF-16→UTF-8 conversion)

For `CUtlSymbolLarge` string fields. `Set(entity, string value)` encodes
via `Utf8.Encode(value, stackalloc byte[Utf8.Size(value)])` then calls
`NativeInterop.SetSchemaString`. **No `Get` path** — schema strings are
write-only from the managed side.

## Players — connected player enumeration

`public const int MaxSlot = 31;` — slot range is 0-30. Internal `bool[31]
_connected` array tracks fully-connected state.

- `Players.IsConnected(slot)` — is the slot marked connected
- `Players.GetAll()` — only connected controllers (skips lingering entities)
- `Players.GetAllControllers()` — all existing controllers (including
  mid-disconnect)
- `Players.GetAllPawns()` — hero pawns for connected players
- `Players.FromSlot(slot)` — `CCitadelPlayerController?` lookup

State is set by `SetConnected(slot, true)` on `ClientFullConnect` and
`ResetAll()` on map change — called from `EntryPoint.cs`.

**MaxSlot == 31, but `RecipientFilter.All` masks all 64 bits** and
`CheckTransmitEvent` bitmap iterates 64-bit words. The discrepancy is
intentional: 31 is the documented max player slots, but the underlying
infrastructure handles up to 64.

## NativeClassAttribute + NativeEntityFactory

Maps C# wrapper types to accepted C++ DLL class names. Used for
`CBaseEntity.As<T>()` type checks and factory construction.

```csharp
[NativeClass("CCitadelPlayerPawn", "CCitadelPlayerPawnCustomized")]
public sealed class CCitadelPlayerPawn : CBasePlayerPawn { ... }
```

`NativeEntityFactory.IsMatch<T>(dllClassName)` builds a set of accepted
names: either `[NativeClass]` class-names list OR the C# type name itself
as default. Also includes names from any loaded subclass.

`NativeEntityFactory.Create<T>(nint handle)` caches a constructor delegate
per type (`Dictionary<Type, Func<nint, CBaseEntity>>`) — wrapper
instantiation is near-zero cost after warmup.

## EntityData<T> — auto-cleanup per-entity storage

**Keyed by `uint EntityHandle`**, not `nint` pointer or entity reference.
This is important: entity pointers can be reused across map changes, but
handles carry a generation counter so stale handles don't collide.

API:
- `new EntityData<T>()` — auto-registers with `EntityDataRegistry` (weak ref)
- `this[entity] = value` / `this[entity]` indexer
- `TryGet(entity, out T)`
- `GetOrAdd(entity, defaultValue)` and `GetOrAdd(entity, Func<T>)` overloads
- `Has(entity)`, `Remove(entity)`, `Clear()`

Global cleanup: `EntityDataRegistry.OnEntityDeleted(uint handle)` iterates
all registered stores (weak references, pruned on iteration) and removes
the entry — **no plugin action needed to clean up per-entity state on
entity destroy**.

Canonical use (`ScourgePlugin.cs:27, 45-46`):

```csharp
private static readonly EntityData<IHandle> _dotTimers = new();

if (_dotTimers.TryGet(pawn, out var existing))
    existing.Cancel();
// ...
_dotTimers[pawn] = handle;
```

Pattern: store the timer handle keyed by the victim pawn; on re-application,
cancel the old one. When the pawn dies or the map ends, the store auto-purges.

## Schema-related cross-ref

The `scan-first-then-vtable` pattern documented in
`concepts/source-2-engine.md` and `entities/deadworks-mem-jsonc.md` is what
backs `NativeInterop.GetSchemaField` under the hood. Plugins don't see
that — the managed API only exposes `SchemaAccessor<T>` and the result is
just an offset + chain offset + networked bool. The native side handles
the binary scan.
