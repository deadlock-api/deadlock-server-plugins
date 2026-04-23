---
date: 2026-04-23
task: scan ../deadworks for new knowledge — Entity I/O hook API
files:
  - ../deadworks/managed/DeadworksManaged.Api/Events/EntityIO.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/EntityOutputEvent.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/EntityInputEvent.cs
  - ../deadworks/managed/PluginLoader.EntityIO.cs
---

The `DeadworksManaged.Api.EntityIO` static class is a first-class
**plugin-facing** API but the wiki only mentions it indirectly — as a
partial-class filename (`PluginLoader.EntityIO.cs`) under runtime internals.
It belongs on `[[plugin-api-surface]]` / `[[events-surface]]`.

## Surface

```csharp
public static class EntityIO {
    public static IHandle HookOutput(string designerName, string outputName, Action<EntityOutputEvent> handler);
    public static IHandle HookInput(string designerName,  string inputName,  Action<EntityInputEvent>  handler);
}
```

Both return an `IHandle` that unregisters the hook when disposed. Handle
is plain `CallbackHandle`, not timer-style — no `CancelOnMapChange` on it.

### Event shapes

`EntityOutputEvent`: `Entity` (the firing entity), `Activator`, `Caller`
(all `CBaseEntity?`), `OutputName` (string).

`EntityInputEvent`: `Entity`, `Activator`, `Caller`, `InputName`,
`Value` (optional `string?` — the input's parameter).

## Dispatch model

Host keys hooks on `"{designerName}:{outputName}"` /
`"{designerName}:{inputName}"` (ordinal compare — case-sensitive).
Dispatcher takes a snapshot of the handler list under lock, then iterates
lock-free — handlers can safely re-hook or unhook inside the callback
without self-deadlock. Exceptions are swallowed and logged via
`_logger.LogError` (`PluginLoader.EntityIO.cs:84, :108`).

## Use-cases in Source 2 entity I/O

Lets plugins react to mapper-wired entity I/O:
- `trigger_multiple:OnTrigger` — zone entry events
- `func_button:Kill` — button kill
- `logic_relay:OnTrigger` — mapper-wired script sequences
- `math_counter:OnHitMax` — counter thresholds

Because designer name is matched exactly, hooking `trigger_multiple`
does NOT catch subclasses unless their `DesignerName` is literally
`trigger_multiple`. No wildcard / prefix matching at hook-registration.

## Gotchas

- **No cleanup on plugin unload.** Unlike timers, entity I/O hooks do
  NOT auto-cancel when the plugin unloads. The handle has to be tracked
  and disposed in `OnUnload`, or the dispatcher will later invoke a
  delegate pointing into a released `AssemblyLoadContext`. (Or: no-op
  if the host eventually filters — but `PluginLoader.EntityIO.cs` has
  no owner tracking at all; handle-based cleanup is the only path.)
- **Not wired to `IHandle.CancelOnMapChange`.** Timer-specific.
- **Input `Value` is a raw string.** Mapper-authored entity I/O passes
  parameters as text; plugin has to parse.

## Not currently used by any plugin in this repo

Searched `/home/manuel/deadlock/deadlock-server-plugins/` — zero
`EntityIO.Hook` call sites. Candidate uses:
- `[[lock-timer]]` could hook trigger entities that describe the lock
  zone instead of using YAML-loaded AABBs.
- `[[trooper-invasion]]` wave start could hook a `logic_relay` on the
  map instead of timer-based spawning.
