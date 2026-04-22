---
title: Timer API
type: entity
sources:
  - raw/notes/2026-04-22-deadworks-timer-api.md
  - ../deadworks/managed/DeadworksManaged.Api/Timer/ITimer.cs
  - ../deadworks/managed/DeadworksManaged.Api/Timer/IStep.cs
  - ../deadworks/managed/DeadworksManaged.Api/Timer/IHandle.cs
  - ../deadworks/managed/DeadworksManaged.Api/Timer/Duration.cs
  - ../deadworks/managed/DeadworksManaged.Api/Timer/Pace.cs
  - ../deadworks/managed/DeadworksManaged.Api/Timer/TimerResolver.cs
  - ../deadworks/examples/plugins/ExampleTimerPlugin/ExampleTimerPlugin.cs
  - ../deadworks/examples/plugins/AutoRestartPlugin/AutoRestartPlugin.cs
related:
  - "[[plugin-api-surface]]"
  - "[[deadworks-runtime]]"
  - "[[examples-index]]"
  - "[[status-poker]]"
created: 2026-04-22
updated: 2026-04-22
confidence: high
---

# Timer API

Per-plugin `ITimer` service. Access via `this.Timer` in any
`IDeadworksPlugin` or the protected `Timer` property on
`DeadworksPluginBase`. The host resolves to a per-plugin instance via
`TimerResolver.Resolve` during bootstrap — don't cache `ITimer` in
static fields, and don't call `this.Timer` from `static` helpers (→
CS0120).

## Four entry points (`ITimer`)

```csharp
IHandle Once(Duration delay, Action cb);
IHandle Every(Duration interval, Action cb);
IHandle Sequence(Func<IStep, Pace> cb);
void    NextTick(Action cb);   // thread-safe
```

Only `NextTick` is documented thread-safe. The other three must be
called from engine-thread contexts (event handlers, command handlers).
If you need to bridge from another thread (an HTTP callback, say),
hop via `NextTick`.

## `Duration` — tick vs wall-clock

Duration is discriminated between tick-based and real-time (ms) at
construction. Use the extension method that matches your intent:

| Extension | Kind |
|-----------|------|
| `64.Ticks()` | tick-based (integer) |
| `3.Seconds()`, `1.5.Seconds()` | real-time (double → ms) |
| `500.Milliseconds()`, `500L.Milliseconds()` | real-time |
| implicit from `TimeSpan` | real-time ms |

Use `.Ticks()` for frame-precise work (every tick, N frames from now).
Everything else is real-time ms. The underlying `TimerEngine` has
separate heaps for each kind.

## `IHandle`

```csharp
void Cancel();              // no-op if already finished
bool IsFinished { get; }    // completed OR cancelled
IHandle CancelOnMapChange(); // fluent chaining
```

Fluent use:
```csharp
_restartSequence = Timer.Sequence(step => { ... }).CancelOnMapChange();
```

`CancelOnMapChange` hooks the next `OnStartupServer` and cancels the
timer — useful when you want a running sequence to end with the map.

**Framework auto-cancels a plugin's timers on unload.** So
`_heartbeat?.Cancel()` in `OnUnload` is defensive; only strictly
necessary if you want to cancel before unload (e.g., inside
`OnConfigReloaded`).

## `IStep` and `Pace` — for `Sequence`

`IStep`:
- `int Run` — invocation count starting at **1** (not 0)
- `long ElapsedTicks` — ticks since the sequence started
- `Pace Wait(Duration delay)` — reschedule after delay
- `Pace Done()` — terminate

`Pace` is an abstract class with internal `WaitPace` and `DonePace`
concrete types — plugins can't construct it directly. You always return
`step.Wait(...)` or `step.Done()`.

## Canonical Sequence idiom

```csharp
Timer.Sequence(step => {
    Console.WriteLine($"step {step.Run}/5 (elapsed: {step.ElapsedTicks})");
    return step.Run < 5 ? step.Wait(500.Milliseconds()) : step.Done();
});
```

With captured mutable state (`AutoRestartPlugin` walks a countdown list):

```csharp
int notifIndex = 0, elapsedSeconds = 0;
_restartSequence = Timer.Sequence(step => {
    if (notifIndex < notifications.Count) {
        var (secondsRemaining, message) = notifications[notifIndex];
        // …
        notifIndex++;
        return step.Wait(waitSeconds.Seconds());
    }
    DoRestart();
    return step.Done();
}).CancelOnMapChange();
```

Sequences are invoked repeatedly in the same closure scope, so locals
persist naturally.

## `NextTick` — thread bridge

```csharp
Timer.NextTick(() => Console.WriteLine("next frame"));
```

Callable from any thread. The StatusPoker plugin uses a
`System.Threading.Timer` + `CancellationToken` for its HTTP poller
and funnels UI-thread work back through `NextTick`.

## Internals pointer

`TimerResolver.Resolve: Func<IDeadworksPlugin, ITimer>` is set by the
host during bootstrap. Under it: a dual-heap `TimerEngine` called
once per frame (before managed `DispatchGameFrame`), with per-frame
throttling (256 tasks + 128 NextTicks max) and lazy cancellation
(cancelled tasks stay in the heap until popped and then skipped).
These are runtime-engine details — plugin authors don't touch them.
