---
date: 2026-04-22
task: scan deadworks timer API
files:
  - ../deadworks/managed/DeadworksManaged.Api/Timer/ITimer.cs
  - ../deadworks/managed/DeadworksManaged.Api/Timer/IStep.cs
  - ../deadworks/managed/DeadworksManaged.Api/Timer/IHandle.cs
  - ../deadworks/managed/DeadworksManaged.Api/Timer/Duration.cs
  - ../deadworks/managed/DeadworksManaged.Api/Timer/Pace.cs
  - ../deadworks/managed/DeadworksManaged.Api/Timer/TimerResolver.cs
  - ../deadworks/examples/plugins/ExampleTimerPlugin/ExampleTimerPlugin.cs
  - ../deadworks/examples/plugins/AutoRestartPlugin/AutoRestartPlugin.cs
---

# Timer API

Per-plugin `ITimer` service resolved via `this.Timer` on `IDeadworksPlugin`
(default implementation calls `TimerResolver.Get(this)`) or `Timer` protected
property on `DeadworksPluginBase`. Four entry points:

- `IHandle Once(Duration delay, Action cb)` — single-shot
- `IHandle Every(Duration interval, Action cb)` — repeating
- `IHandle Sequence(Func<IStep, Pace> cb)` — stateful sequence
- `void NextTick(Action cb)` — next-frame deferred; thread-safe (explicitly
  marked in doc — call from any thread)

## Duration — tick vs wall-clock

`Duration` is a `readonly record struct` with an internal `DurationKind`
discriminator: `Ticks` vs `RealTime` (ms). Extension methods pick the kind:

- `64.Ticks()` → tick-based (integer)
- `3.Seconds()`, `1.5.Seconds()` → real-time (converted to ms internally)
- `500.Milliseconds()` → real-time
- Implicit conversion from `TimeSpan` → real-time ms

The underlying `TimerEngine` uses a dual-heap design: one heap for tick-based
tasks, one for real-time tasks (noted in earlier log). **If you want tick
precision, use `.Ticks()`; everything else becomes real-time ms.**

## IHandle

- `void Cancel()` — no-op if already finished
- `bool IsFinished { get; }` — completed or cancelled
- `IHandle CancelOnMapChange()` — fluent; auto-cancels when `OnStartupServer`
  fires (map change). Example:

```csharp
_restartSequence = Timer.Sequence(step => {...}).CancelOnMapChange();
```

Per `ExampleTimerPlugin.cs:54`, **`_heartbeat?.Cancel()` in `OnUnload` is
defensive — the framework cancels a plugin's timers automatically on unload**.
You only need to track a handle if you want to cancel before unload (e.g. in
`OnConfigReloaded` to stop and restart).

## IStep + Pace (for Sequence)

`IStep`:
- `int Run` — invocation count starting at 1 (not 0)
- `long ElapsedTicks` — ticks since sequence started
- `Pace Wait(Duration delay)` — reschedule
- `Pace Done()` — terminate

`Pace` is an abstract class with two internal concrete types: `WaitPace`
and `DonePace.Instance`. Not constructible from outside the library — the
public API forces you through `step.Wait(...)` / `step.Done()`.

## Canonical Sequence idiom

From `ExampleTimerPlugin.cs:24-28`:

```csharp
Timer.Sequence(step => {
    Console.WriteLine($"step {step.Run}/5 (elapsed: {step.ElapsedTicks})");
    return step.Run < 5 ? step.Wait(500.Milliseconds()) : step.Done();
});
```

From `AutoRestartPlugin.cs:70-102` — a sequence that walks a list of
countdown notifications, accumulating elapsed seconds and calling
`Chat.PrintToChatAll(message)` at each milestone, then `DoRestart()`:

```csharp
_restartSequence = Timer.Sequence(step => {
    if (notifIndex < notifications.Count) { ...return step.Wait(...); }
    DoRestart();
    return step.Done();
}).CancelOnMapChange();
```

Note the captured mutable state (`notifIndex`, `elapsedSeconds`) — sequence
closures are invoked repeatedly in the same scope, so locals accumulate
naturally.

## NextTick — only thread-safe timer method

`NextTick` is specifically marked thread-safe. `Once`/`Every`/`Sequence`
are not — they're meant to be called from engine-thread contexts
(plugin event handlers, command handlers). If you need to cross threads
(e.g. from a `System.Threading.Timer` in an async plugin), `NextTick`
is the bridge.

## No async HTTP support in timers

Timer callbacks are `Action` (sync). For async workflows (HTTP keepalive,
etc.) plugins use their own `System.Threading.Timer` + `CancellationToken`
and marshal results back with `NextTick`. This matches the StatusPoker
plugin's pattern in this repo.
