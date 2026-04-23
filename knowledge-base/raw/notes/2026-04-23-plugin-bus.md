---
date: 2026-04-23
task: Document the new PluginBus plugin-to-plugin communication surface added upstream in `../deadworks/`
files:
  - ../deadworks/managed/DeadworksManaged.Api/Bus/PluginBus.cs
  - ../deadworks/managed/DeadworksManaged.Api/Bus/EventContext.cs
  - ../deadworks/managed/DeadworksManaged.Api/Bus/QueryContext.cs
  - ../deadworks/managed/DeadworksManaged.Api/Bus/EventHandlerAttribute.cs
  - ../deadworks/managed/DeadworksManaged.Api/Bus/QueryHandlerAttribute.cs
  - ../deadworks/managed/PluginLoader.PluginBus.cs
  - ../deadworks/managed/HandlerRegistry.cs
  - ../deadworks/managed/ConCommandManager.cs (dw_pluginbus)
  - ../deadworks/managed/DeadworksManaged.Tests/PluginBusTests.cs
---

# PluginBus (upstream deadworks, new)

Plugin-to-plugin communication lives on one static class:
`DeadworksManaged.Api.PluginBus`. It supports two interaction styles:

- **Events** — fire-and-forget fan-out. Every matching subscriber receives
  the event.
- **Queries** — request/response, collect-all. Every matching handler
  contributes one response; the caller gets the list.

Both are synchronous (handlers run on the calling thread), both
auto-clean up when the plugin that registered them unloads, and both use
the same naming convention: `plugin_name:event_or_query_name` in
snake_case, compared ordinally/case-sensitively.

---

## Events

### Subscribing

Four delegate shapes are supported:

```csharp
// Context handler, can influence the aggregate HookResult
PluginBus.Subscribe("my_plugin:round_started", (EventContext ctx) =>
{
    Console.WriteLine($"{ctx.SenderPluginName} fired {ctx.Name} with {ctx.Payload}");
    return HookResult.Continue;
});

// Fire-and-forget context handler (implicitly Continue)
PluginBus.Subscribe("my_plugin:round_started", ctx => Log(ctx.Payload));

// Typed payload — fires only when payload is-a T
PluginBus.Subscribe<RoundStartedPayload>("my_plugin:round_started", p =>
{
    Console.WriteLine($"round {p.RoundNumber}");
});

// Typed payload, returns HookResult
PluginBus.Subscribe<RoundStartedPayload>("my_plugin:round_started",
    p => p.IsWarmup ? HookResult.Stop : HookResult.Continue);
```

Each `Subscribe` returns an `IHandle`. Call `.Cancel()` to remove the
subscription manually. You don't have to — handlers registered by a
plugin are auto-removed when the plugin unloads.

Attribute form:

```csharp
public class MyPlugin : IDeadworksPlugin
{
    [EventHandler("my_plugin:round_started")]
    public void OnRoundStarted() { /* 0 params is valid */ }

    [EventHandler("my_plugin:round_started")]
    public HookResult OnRoundStartedCtx(EventContext ctx) => HookResult.Continue;

    [EventHandler("my_plugin:round_started")]
    public void OnRoundStartedTyped(RoundStartedPayload p) { /* typed */ }
}
```

Attribute handler signature rules: 0 params, or one `EventContext`, or
one reference-type payload. Return type `void` or `HookResult`. Malformed
signatures are skipped at load time with a console warning; they don't
break the plugin. One method can carry multiple `[EventHandler("...")]`
attributes if you want it to listen to several names.

### Publishing

```csharp
PluginBus.Publish("my_plugin:round_started");                           // no payload

PluginBus.Publish("my_plugin:round_started",                            // with payload
    new RoundStartedPayload { RoundNumber = 3, IsWarmup = false });

if (PluginBus.HasSubscribers("my_plugin:debug_snapshot"))               // gate expensive work
    PluginBus.Publish("my_plugin:debug_snapshot", BuildExpensiveSnapshot());
```

`Publish` returns the aggregated `HookResult` across subscribers using a
**max-wins** rule: `Continue < Stop < Handled`. Publishers can use this
to decide whether to proceed with a default action.

Example — a damage-filter event:

```csharp
// producer (perhaps the damage system)
var verdict = PluginBus.Publish("dmg:incoming", evt);
if (verdict == HookResult.Handled) return;      // a subscriber fully handled it
if (verdict == HookResult.Stop)    evt.Damage = 0;

// consumer
PluginBus.Subscribe<DamageEvent>("dmg:incoming", evt =>
    evt.Attacker.IsBot ? HookResult.Stop : HookResult.Continue);
```

All subscribers run even if one returns `Handled` — aggregation is
across the full fan-out, and an exception in one handler is caught,
logged as `[PluginBus] Event handler for '<name>' threw: ...`, and the
remaining subscribers still run.

### EventContext

```csharp
public sealed class EventContext {
    public const string HostSenderName = "<host>";

    public string  Name { get; }              // the event name
    public object? Payload { get; }           // the publisher's payload, or null
    public string  SenderPluginName { get; }  // plugin name or "<host>"
    public T? PayloadAs<T>() where T : class; // convenience cast, null on mismatch
}
```

`SenderPluginName` is resolved by walking the managed stack to the first
frame whose `AssemblyLoadContext` maps to a loaded plugin, so publishers
don't have to identify themselves manually. Calls made from host code
show up as `"<host>"`.

---

## Queries

Queries look a lot like events, but handlers return data and the caller
gets every response as a list.

### Registering a responder

Three delegate shapes:

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

Returns `IHandle`, same auto-cancellation on unload.

Attribute form:

```csharp
public class EconomyPlugin : IDeadworksPlugin
{
    [QueryHandler("stats:online_players")]
    public int OnlinePlayerCount() => _players.Count;

    [QueryHandler("debug:who_are_you")]
    public string Who(QueryContext ctx) => $"EconomyPlugin answering {ctx.SenderPluginName}";

    [QueryHandler("economy:price")]
    public int PriceOf(GetItemPrice req) => _table[req.ItemId];
}
```

Attribute rules: 0 params, or one `QueryContext`, or one reference-type
request. The return type **is** the response — non-void is required.
Multiple methods — even across different plugins — can carry the same
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
    PluginBus.Query<ItemList>("inventory:dump",
        new DumpRequest { Filter = ... });
```

### Query semantics

- **Collect-all.** Every handler whose declared `TResponse == typeof(TResponse)`
  of the caller contributes exactly one entry to the result list.
- **Response-type mismatch is silent.** A handler declared as
  `HandleQuery<long>` does not contribute to `Query<int>`. Pick a shared
  response type both sides know.
- **Typed-request mismatch is silent.** A handler declared as
  `Func<GetItemPrice, int>` won't fire when the caller passes null or a
  different type. The handler simply doesn't appear in the result list —
  no throw.
- **Exceptions are isolated.** If a handler throws, it's logged
  (`[PluginBus] Query handler for '<name>' threw: ...`) and the remaining
  handlers still run. The thrower's slot is omitted from the list.
- **No matching handler → empty list.** `Query<T>` returns
  `Array.Empty<T>()`, never null, and never throws.
- **Order is registration order** among matching handlers. Don't rely on
  it across reloads.
- **Synchronous.** Handlers run on the calling thread before `Query<T>`
  returns. Handlers that need to do I/O should fork their own task.

### Response-type matching, in detail

The match is on the handler's generic `TResponse`, not on the runtime
`GetType()` of the returned value:

```csharp
PluginBus.HandleQuery<int>("x", () => 5);       // declares TResponse = int
PluginBus.HandleQuery<long>("x", () => 5L);     // declares TResponse = long
PluginBus.HandleQuery<object>("x", () => 5);    // declares TResponse = object

PluginBus.Query<int>("x");      // [5]           — only the int handler
PluginBus.Query<long>("x");     // [5]           — only the long handler
PluginBus.Query<object>("x");   // [5 (boxed)]   — only the object handler
PluginBus.Query<string>("x");   // []            — no string handler
```

This keeps dispatch cheap (single reference-equality compare per handler
at query time) and avoids surprise coercions. The flip side: both ends
of a cross-plugin query need to agree on the exact response type.

### QueryContext

```csharp
public sealed class QueryContext {
    public const string HostSenderName = "<host>";

    public string  Name { get; }              // the query name
    public object? Request { get; }           // the caller's request, or null
    public string  SenderPluginName { get; }  // plugin name or "<host>"
    public T? RequestAs<T>() where T : class; // convenience cast, null on mismatch
}
```

Same sender resolution as `EventContext`.

---

## Type identity across plugins

Each plugin loads in its own collectible `AssemblyLoadContext`. That
means a `MyPayload` class defined in plugin A's own DLL has a distinct
`Type` identity from the "same" class defined in plugin B's DLL, and
typed `Subscribe<T>` / `HandleQuery<…>` / typed-request handlers would
never match across them.

Options, in order of preference:

1. **Framework types.** `string`, primitives, `Guid`, `List<int>`, etc.
   — always safe.
2. **Types in `DeadworksManaged.Api`.** The API assembly is loaded with
   shared identity across every plugin. If you own a feature and want
   multiple plugins to exchange typed data, put the contract there.
3. **A dedicated shared-contract DLL** that every participating plugin
   references. You'll need to make sure it gets resolved from a single
   instance — realistically this means putting it in the API or next to
   it.
4. **Untyped `object?` payloads/requests/responses.** Always works but
   gives up compile-time type checks. Useful for string-based contracts
   (`Publish("x:hello", "world")` → `Subscribe("x:hello", ctx => (string)ctx.Payload)`).

Objects pass by reference within the process — no serialization, no deep
copy. If you publish a mutable object, the handler sees the same
instance; don't mutate it after publishing unless you mean to.

---

## Diagnostics

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

- Event subscriptions and query handlers list active registrations with
  subscriber/handler counts and the plugins that own them.
- Recent sections cover the last 60 seconds; each ring buffer holds 64
  entries. `(subs: N)` / `(handlers: N, responses: M)` show the counts
  at the time of the last publish/query.
- A `0 responses` query with handlers registered is a strong hint that a
  typed-request mismatch is silently skipping them — check the request
  type.
- When a published name matches no subscribers and a very similar name
  is subscribed, the command suggests it (`← did you mean '…'?`).

---

## Performance notes

- Dispatch path is direct delegate invocation — no `DynamicInvoke`.
  Typed wrappers are built once at `Subscribe`/`HandleQuery` time using
  cached `MethodInfo` + `MakeGenericMethod`.
- Handler lists are snapshot-copied under the bus lock, then iterated
  lock-free, so handlers can call back into `Publish`/`Query` without
  self-deadlocking.
- Stack-walk sender resolution (`ResolveCallingPluginName`) only runs
  when there is at least one handler; zero-handler `Publish`/`Query`
  returns immediately after recording the diagnostic entry.
- Events and queries use separate handler registries, but share
  per-plugin tracking so a single `HandlerRegistry.UnregisterPlugin`
  call cleans up both when a plugin unloads.

---

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
