---
title: PluginBus — plugin-to-plugin events and queries
type: entity
sources:
  - raw/notes/2026-04-23-plugin-bus.md
  - ../deadworks/managed/DeadworksManaged.Api/Bus/PluginBus.cs
  - ../deadworks/managed/DeadworksManaged.Api/Bus/EventContext.cs
  - ../deadworks/managed/DeadworksManaged.Api/Bus/QueryContext.cs
  - ../deadworks/managed/DeadworksManaged.Api/Bus/EventHandlerAttribute.cs
  - ../deadworks/managed/DeadworksManaged.Api/Bus/QueryHandlerAttribute.cs
  - ../deadworks/managed/PluginLoader.PluginBus.cs
  - ../deadworks/managed/HandlerRegistry.cs
  - ../deadworks/managed/ConCommandManager.cs
related:
  - "[[plugin-api-surface]]"
  - "[[deadworks-runtime]]"
  - "[[deadworks-plugin-loader]]"
  - "[[events-surface]]"
  - "[[command-attribute]]"
created: 2026-04-23
updated: 2026-04-23
confidence: high
---

# PluginBus

A static plugin-to-plugin communication surface living at
`DeadworksManaged.Api.PluginBus`, added in upstream `../deadworks/`.
Orthogonal to [[events-surface]] (which hooks engine lifecycle /
gameplay events) — `PluginBus` is purely for cross-plugin or host↔plugin
messaging inside the managed host.

Two interaction styles:

- **Events** — fire-and-forget fan-out; every matching subscriber
  receives the event.
- **Queries** — request/response, collect-all; every matching handler
  contributes one entry to a response list.

Both are **synchronous** (handlers run on the calling thread), both
**auto-clean up** when the registering plugin unloads, and both use the
naming convention `plugin_name:event_or_query_name` in `snake_case`.
Names are compared **ordinally and case-sensitively** — the host wires
both registries with `StringComparer.Ordinal`
(`PluginLoader.cs:66-67`).

## Files

| File | Role |
|---|---|
| `DeadworksManaged.Api/Bus/PluginBus.cs` | Public static surface; plumbing delegates (`OnSubscribe`, `OnPublish`, `OnHandleQuery`, `OnQuery`, `On*Count`) filled by the host at init |
| `DeadworksManaged.Api/Bus/EventContext.cs` | `Name`, `Payload`, `SenderPluginName`, `PayloadAs<T>()`; `HostSenderName = "<host>"` |
| `DeadworksManaged.Api/Bus/QueryContext.cs` | `Name`, `Request`, `SenderPluginName`, `RequestAs<T>()` |
| `DeadworksManaged.Api/Bus/EventHandlerAttribute.cs` | `[EventHandler("name")]`, `AllowMultiple = true` |
| `DeadworksManaged.Api/Bus/QueryHandlerAttribute.cs` | `[QueryHandler("name")]`, `AllowMultiple = true` |
| `PluginLoader.PluginBus.cs` | Host-side dispatcher: delegate normalization, registries, stack-walk sender resolution, diagnostic ring buffers |
| `HandlerRegistry.cs` | Generic `HandlerRegistry<TKey,THandler>` shared with other subsystems; supports `AddForPlugin` / `UnregisterPlugin` so unload cleans up both events and queries in one call |
| `ConCommandManager.cs` | Registers the `dw_pluginbus` built-in console command |
| `DeadworksManaged.Tests/PluginBusTests.cs` | Test suite; `PluginLoader.InitializePluginBusForTests()` / `ResetPluginBusForTests()` wire the dispatcher for unit tests |

## Events

### Subscribing

Four delegate shapes (all return `IHandle`; `.Cancel()` removes
manually, but unload auto-removes):

```csharp
// Context handler, can influence the aggregate HookResult
PluginBus.Subscribe("my_plugin:round_started", (EventContext ctx) => {
    Console.WriteLine($"{ctx.SenderPluginName} fired {ctx.Name} with {ctx.Payload}");
    return HookResult.Continue;
});

// Fire-and-forget context handler (implicitly Continue)
PluginBus.Subscribe("my_plugin:round_started", ctx => Log(ctx.Payload));

// Typed payload — fires only when payload is-a T
PluginBus.Subscribe<RoundStartedPayload>("my_plugin:round_started",
    p => Console.WriteLine($"round {p.RoundNumber}"));

// Typed payload, returns HookResult
PluginBus.Subscribe<RoundStartedPayload>("my_plugin:round_started",
    p => p.IsWarmup ? HookResult.Stop : HookResult.Continue);
```

### Attribute form

```csharp
public class MyPlugin : IDeadworksPlugin {
    [EventHandler("my_plugin:round_started")]
    public void OnRoundStarted() { /* 0 params is valid */ }

    [EventHandler("my_plugin:round_started")]
    public HookResult OnRoundStartedCtx(EventContext ctx) => HookResult.Continue;

    [EventHandler("my_plugin:round_started")]
    public void OnRoundStartedTyped(RoundStartedPayload p) { /* typed */ }
}
```

Signature rules: 0 params, or one `EventContext`, or one reference-type
payload. Return `void` or `HookResult`. Malformed signatures are skipped
at load with a console warning — they don't break the plugin. One
method may carry multiple `[EventHandler("...")]` attributes.

### Publishing

```csharp
PluginBus.Publish("my_plugin:round_started");                           // no payload
PluginBus.Publish("my_plugin:round_started",
    new RoundStartedPayload { RoundNumber = 3, IsWarmup = false });

if (PluginBus.HasSubscribers("my_plugin:debug_snapshot"))               // gate expensive work
    PluginBus.Publish("my_plugin:debug_snapshot", BuildExpensiveSnapshot());
```

`Publish` returns the **aggregated** `HookResult` across subscribers
using a **max-wins** rule: `Continue (0) < Stop (1) < Handled (2)`
(same as [[events-surface]] convention — see `Enums/HookResult.cs`).
Publishers can use this to decide whether to proceed with a default
action.

All subscribers run even if one returns `Handled`. An exception in one
handler is caught, logged as `[PluginBus] Event handler for '<name>'
threw: ...`, and the remaining subscribers still run.

### `EventContext`

```csharp
public sealed class EventContext {
    public const string HostSenderName = "<host>";
    public string  Name { get; }              // the event name
    public object? Payload { get; }           // publisher's payload, or null
    public string  SenderPluginName { get; }  // plugin name or "<host>"
    public T? PayloadAs<T>() where T : class; // convenience cast, null on mismatch
}
```

`SenderPluginName` is resolved by walking the managed stack to the first
frame whose `AssemblyLoadContext` maps to a loaded plugin
(`ResolveCallingPluginPath` in `PluginLoader.PluginBus.cs:567`), so
publishers don't identify themselves manually. Calls made from host
code show up as `"<host>"`.

## Queries

### Registering a responder

Three delegate shapes (all return `IHandle`):

```csharp
// No-arg handler
PluginBus.HandleQuery<int>("stats:online_players", () => GetPlayerCount());

// Context-aware handler
PluginBus.HandleQuery<string>("debug:who_are_you",
    (QueryContext ctx) => $"hello {ctx.SenderPluginName}");

// Typed request — handler skipped if request isn't a GetItemPrice
PluginBus.HandleQuery<GetItemPrice, int>("economy:price",
    req => _table[req.ItemId]);
```

### Attribute form

```csharp
public class EconomyPlugin : IDeadworksPlugin {
    [QueryHandler("stats:online_players")]
    public int OnlinePlayerCount() => _players.Count;

    [QueryHandler("debug:who_are_you")]
    public string Who(QueryContext ctx) => $"EconomyPlugin answering {ctx.SenderPluginName}";

    [QueryHandler("economy:price")]
    public int PriceOf(GetItemPrice req) => _table[req.ItemId];
}
```

Rules: 0 params, or one `QueryContext`, or one reference-type request.
Non-void return required — the return type **is** the response type.
Multiple methods — even across different plugins — may carry the same
`[QueryHandler("name")]`; every matching one contributes a response.

### Issuing a query

```csharp
IReadOnlyList<int> counts = PluginBus.Query<int>("stats:online_players");
// e.g. [12] — or [] if no handler is registered

IReadOnlyList<int> prices = PluginBus.Query<int>("economy:price",
    new GetItemPrice { ItemId = "medkit" });

// Multiple responders
PluginBus.HandleQuery<int>("stats:online_players", () => 12);   // plugin A
PluginBus.HandleQuery<int>("stats:online_players", () => 8);    // plugin B
var both = PluginBus.Query<int>("stats:online_players");        // [12, 8]

if (PluginBus.HasQueryHandlers("inventory:dump"))               // gate expensive request
    PluginBus.Query<ItemList>("inventory:dump", new DumpRequest { /*…*/ });
```

### Query semantics

- **Collect-all.** Every handler whose declared `TResponse` equals the
  caller's `TResponse` contributes exactly one entry.
- **Response-type mismatch is silent.** `HandleQuery<long>` does not
  contribute to `Query<int>`. Both sides must agree on the exact type.
- **Typed-request mismatch is silent.** `Func<GetItemPrice,int>` won't
  fire when the caller passes `null` or a different type; the handler
  is omitted from the list — no throw.
- **Exceptions are isolated.** Logged as `[PluginBus] Query handler for
  '<name>' threw: ...`; remaining handlers still run; the thrower's slot
  is omitted.
- **No matching handler → empty list.** `Query<T>` returns
  `Array.Empty<T>()`, never `null`, never throws.
- **Order is registration order** among matching handlers. Don't rely on
  it across reloads.
- **Synchronous.** Handlers run on the calling thread before `Query<T>`
  returns. Handlers needing I/O should fork their own task.

### Response-type matching, in detail

The match is on the handler's generic `TResponse`, **not** on the
runtime type of the returned value:

```csharp
PluginBus.HandleQuery<int>("x",    () => 5);       // declares TResponse = int
PluginBus.HandleQuery<long>("x",   () => 5L);      // declares TResponse = long
PluginBus.HandleQuery<object>("x", () => 5);       // declares TResponse = object

PluginBus.Query<int>("x");      // [5]           — only the int handler
PluginBus.Query<long>("x");     // [5]           — only the long handler
PluginBus.Query<object>("x");   // [5 (boxed)]   — only the object handler
PluginBus.Query<string>("x");   // []            — no string handler
```

Dispatch is one reference-equality compare per handler at query time —
cheap, no surprise coercions — but both ends of a cross-plugin query
must agree on the exact response type.

### `QueryContext`

```csharp
public sealed class QueryContext {
    public const string HostSenderName = "<host>";
    public string  Name { get; }
    public object? Request { get; }
    public string  SenderPluginName { get; }
    public T? RequestAs<T>() where T : class;
}
```

Same stack-walk sender resolution as `EventContext`.

## Type identity across plugins — a live constraint

> **Gotcha that will bite you.** Each plugin loads in its own
> collectible `AssemblyLoadContext` (see [[deadworks-plugin-loader]]).
> A `MyPayload` class defined in plugin A's own DLL has a **distinct
> `Type` identity** from the "same" class defined in plugin B's DLL,
> and typed `Subscribe<T>` / `HandleQuery<…>` / typed-request handlers
> will never match across them.

Options, in order of preference:

1. **Framework types** (`string`, primitives, `Guid`, `List<int>`, …) —
   always safe.
2. **Types in `DeadworksManaged.Api`** — the API assembly is loaded
   with shared identity across every plugin. If you own a feature and
   want multiple plugins to exchange typed data, put the contract
   there.
3. **A dedicated shared-contract DLL** every participating plugin
   references — needs to resolve from a single instance, which
   realistically means putting it in the API or next to it.
4. **Untyped `object?` payloads/requests/responses** — always works but
   gives up compile-time checks. Useful for string-based contracts
   (`Publish("x:hello", "world")` →
   `Subscribe("x:hello", ctx => (string)ctx.Payload)`).

Objects pass by reference within the process — no serialization, no
deep copy. If you publish a mutable object, handlers see the same
instance; don't mutate it after publishing unless that's intentional.

## Diagnostics — `dw_pluginbus`

The host registers a built-in console command
(`ConCommandManager.cs:26`) with
[[command-attribute|the built-in command surface]]:

```
] dw_pluginbus
[PluginBus] Event subscriptions:
  my_plugin:round_started       2  plugin_a, plugin_b
[PluginBus] Query handlers:
  economy:price                 1  economy_plugin
  stats:online_players          2  census_plugin, economy_plugin
[PluginBus] Recently published events (last 60s):
  my_plugin:round_started       x3  (subs: 2)
  my_plugin:typo                x1  (subs: 0)  ← no subscribers — did you mean 'my_plugin:round_started'?
[PluginBus] Recently issued queries (last 60s):
  stats:online_players          x5  (handlers: 2, responses: 2)
  economy:price                 x1  (handlers: 1, responses: 0)
```

- **Event subscriptions / Query handlers** list active registrations
  with subscriber/handler counts and the owning plugin names.
- **Recent** sections cover the last 60 seconds; each ring buffer holds
  64 entries (`RecentHistoryCapacity` in
  `PluginLoader.PluginBus.cs:34`). `(subs: N)` / `(handlers: N,
  responses: M)` show counts at the time of the last publish/query.
- **`0 responses` with handlers registered** is a strong hint that a
  **typed-request mismatch** is silently skipping them — check the
  request type.
- When a published name matches no subscribers and a very similar name
  is subscribed, the command prints a **"did you mean"** suggestion.

## Performance notes

- Dispatch is **direct delegate invocation** — no `DynamicInvoke`. Typed
  wrappers are built once at `Subscribe` / `HandleQuery` time from
  cached `MethodInfo` + `MakeGenericMethod`
  (`BuildTypedEventFunc`, `BuildQueryTypedFunc`, … in
  `PluginLoader.PluginBus.cs:44-73`).
- Handler lists are **snapshot-copied under the bus lock** and then
  iterated lock-free, so handlers may call back into
  `Publish`/`Query` without self-deadlocking.
- **Stack-walk sender resolution** only runs when a handler actually
  matches; zero-handler `Publish`/`Query` returns immediately after
  recording the diagnostic entry.
- Events and queries use **separate registries** but **shared per-plugin
  tracking**, so one `HandlerRegistry.UnregisterPlugin` call cleans up
  both when a plugin unloads.

## Auto-cleanup on unload

Handlers registered by a plugin method body (via `PluginBus.Subscribe`
/ `PluginBus.HandleQuery`) are routed through the host dispatcher,
which resolves the calling plugin's path and records it in the
registry. When the plugin unloads,
[[deadworks-plugin-loader|the loader]] calls
`HandlerRegistry.UnregisterPlugin`, removing every subscription and
query handler that plugin owns — events and queries in one pass. No
`OnUnload` bookkeeping is required.

Manual `handle.Cancel()` on the returned `IHandle` is only necessary
for **scoped** registrations (e.g. register during one round, cancel
at the end) or **host-side** registrations that outlive any single
plugin.

## Quick reference

| Capability | Call |
|---|---|
| Fire an event (optional payload) | `PluginBus.Publish(name, payload)` |
| Subscribe (context, fire-and-forget) | `PluginBus.Subscribe(name, ctx => ...)` |
| Subscribe (context, returns `HookResult`) | `PluginBus.Subscribe(name, ctx => HookResult.Stop)` |
| Subscribe (typed payload) | `PluginBus.Subscribe<T>(name, t => ...)` |
| Check before constructing payload | `PluginBus.HasSubscribers(name)` |
| Declare event subscriber via attribute | `[EventHandler("name")]` on 0/`EventContext`/T method |
| Issue a query (collect all responses) | `PluginBus.Query<TResponse>(name, request)` |
| Register a responder (no request) | `PluginBus.HandleQuery<TRes>(name, () => ...)` |
| Register a responder (with `QueryContext`) | `PluginBus.HandleQuery<TRes>(name, ctx => ...)` |
| Register a responder (typed request) | `PluginBus.HandleQuery<TReq, TRes>(name, req => ...)` |
| Check before constructing request | `PluginBus.HasQueryHandlers(name)` |
| Declare query handler via attribute | `[QueryHandler("name")]` on non-void method |
| Cancel a subscription/handler manually | `handle.Cancel()` on the returned `IHandle` |
| Inspect state / recent activity | `dw_pluginbus` console command |

## Current usage in this repo

> None yet. No plugin under this repo (`Deathmatch`, `LockTimer`,
> `StatusPoker`, `TrooperInvasion`, `FlexSlotUnlock`, `HealOnSpawn`,
> `HeroSelect`, `Hostname`, `TeamChangeBlock`) currently publishes or
> subscribes via `PluginBus`. Candidate use-cases: TrooperInvasion
> exposing wave / patron state for a future HUD plugin; Deathmatch
> exposing round boundaries; a cross-plugin `stats:online_players`
> query. See [[plugin-api-surface]] for where it fits in the API map.
