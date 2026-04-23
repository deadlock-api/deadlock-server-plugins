---
title: Entity I/O API
type: entity
sources:
  - raw/notes/2026-04-23-entity-io-api.md
  - ../deadworks/managed/DeadworksManaged.Api/Events/EntityIO.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/EntityOutputEvent.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/EntityInputEvent.cs
  - ../deadworks/managed/PluginLoader.EntityIO.cs
related:
  - "[[plugin-api-surface]]"
  - "[[events-surface]]"
  - "[[schema-accessors]]"
  - "[[deadworks-plugin-loader]]"
  - "[[deadworks-scan-2026-04-23]]"
created: 2026-04-23
updated: 2026-04-23
confidence: high
---

# Entity I/O API

`DeadworksManaged.Api.EntityIO` lets plugins hook Source 2's **entity
I/O system** — the mapper-authored "when X fires Y, do Z" graph edges
encoded in `trigger_*`, `func_button`, `logic_relay`, `math_counter`
and similar. Documented here because `[[plugin-api-surface]]` only
references it as a partial-class filename.

## Surface

```csharp
public static class EntityIO {
    public static IHandle HookOutput(string designerName, string outputName, Action<EntityOutputEvent> handler);
    public static IHandle HookInput (string designerName, string inputName,  Action<EntityInputEvent>  handler);
}
```

Both return an `IHandle`. Disposing the handle unregisters the hook
(`PluginLoader.EntityIO.cs:22-35` / `:49-62`).

## Event shapes

### `EntityOutputEvent`

| Field | Type | Meaning |
|-------|------|---------|
| `Entity` | `CBaseEntity` | entity that fired the output |
| `Activator` | `CBaseEntity?` | entity that triggered the firing (e.g., the player who touched a trigger) |
| `Caller` | `CBaseEntity?` | entity that passed the signal along — often same as `Entity`, sometimes a linked `logic_relay` |
| `OutputName` | `string` | e.g. `"OnTrigger"`, `"OnEndTouch"`, `"OnTimer"` |

### `EntityInputEvent`

Same first three fields plus:

| Field | Type | Meaning |
|-------|------|---------|
| `InputName` | `string` | e.g. `"Kill"`, `"FireUser1"`, `"Enable"` |
| `Value` | `string?` | optional raw parameter string passed by mapper |

Input `Value` is an un-parsed string — if the mapper writes
`OnTrigger !player,FireUser1,42,0,-1` the `FireUser1` input handler
sees `Value = "42"`. Plugin has to `int.Parse` / `float.Parse` itself.

## Dispatch model

- Handler lists are keyed on the literal string `"{designerName}:{outputName}"`
  (ordinal-compared — **case-sensitive**).
- Host takes a snapshot of the list under lock and iterates lock-free
  (`PluginLoader.EntityIO.cs:73, :97`). Handlers can safely re-hook
  or unhook inside the callback without self-deadlocking.
- Exceptions thrown from a handler are caught and logged via
  `_logger.LogError(ex, "Entity output hook {Key} threw", key)` —
  one misbehaving hook does not abort dispatch to the rest.

## Gotchas

### No auto-cleanup on plugin unload

Unlike [[timer-api]] (which cancels all per-plugin handles on unload)
and [[plugin-bus]] (which stack-walks to record the caller and cleans
up), `PluginLoader.EntityIO.cs` has **no owner tracking**. If the
plugin does not dispose its `IHandle` in `OnUnload`, the dispatcher
will later invoke a delegate pointing into a released
`AssemblyLoadContext` — almost certainly a crash on the next matching
entity I/O event.

**Always keep the handles and dispose them in `OnUnload`.**

### Designer-name match is exact

Hooking `"trigger_multiple"` does not catch subclasses unless their
`DesignerName` is literally `trigger_multiple`. There is no wildcard
or prefix matching at hook-registration time — if you want broad
coverage, iterate and hook per distinct name, or use `OnEntitySpawned`
+ `entity.DesignerName` filtering instead.

### Ordinal case-sensitivity

`"Trigger_Multiple"` ≠ `"trigger_multiple"`. Always match exactly
what Source 2 reports for that entity class.

## Use-cases

Not currently used by any plugin in `deadlock-server-plugins/`. Candidates:

- [[lock-timer]] — hook trigger entities carrying lock-zone geometry
  directly instead of loading AABBs from YAML.
- [[trooper-invasion]] — wave-start on a mapper-placed `logic_relay`,
  decoupling from timer-based spawning.
- Chat command that fires `func_button:Press` inputs on walker doors
  or cinematic relays.

## Related pages

- [[events-surface]] — the 23-hook `IDeadworksPlugin` surface for
  engine-level events (`OnEntityCreated`, `OnEntityStartTouch`).
  EntityIO is orthogonal: it observes mapper-wired I/O, not engine
  lifecycle.
- [[plugin-api-surface]] — the full API index.
- [[deadworks-plugin-loader]] — subsystem init order (step 11 wires
  `EntityIO.OnHookOutput/OnHookInput`).
