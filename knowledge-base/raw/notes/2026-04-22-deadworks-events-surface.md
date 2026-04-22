---
date: 2026-04-22
task: scan deadworks event hook surface
files:
  - ../deadworks/managed/DeadworksManaged.Api/IDeadworksPlugin.cs
  - ../deadworks/managed/DeadworksManaged.Api/DeadworksPluginBase.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/AbilityAttemptEvent.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/TakeDamageEvent.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/CheckTransmitEvent.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/EntityTouchEvent.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/GameEvent.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/GameEventHandlerAttribute.cs
  - ../deadworks/managed/DeadworksManaged.Api/Enums/HookResult.cs
  - ../deadworks/managed/PluginLoader.Events.cs
---

# Event hook surface

## IDeadworksPlugin lifecycle hooks (23 methods, all with default no-ops)

Lifecycle/frame:
- `OnLoad(bool isReload)` — `isReload` flag distinguishes fresh load from hot-reload
- `OnUnload()` — cleanup; framework already cancels the plugin's timers
- `OnPrecacheResources()` — map load precache phase; use `Precache.AddResource`
- `OnStartupServer()` — fired on new map load (also triggers `CancelOnMapChange`)
- `OnGameFrame(bool simulating, bool firstTick, bool lastTick)` — every tick

Client connection (5 events, strict order per connect):
- `OnClientConnect(ClientConnectEvent) → bool` — reject with `false`.
  Doc says "All plugins see the event regardless of any individual result"
  — so returning `false` doesn't short-circuit dispatch to other plugins.
- `OnClientPutInServer(ClientPutInServerEvent)` — initial put-in
- `OnClientFullConnect(ClientFullConnectEvent)` — in-game (this is when
  `Players.SetConnected(slot, true)` fires, so `Players.GetAll()` starts
  including the player from this hook onward)
- `OnClientDisconnect(ClientDisconnectedEvent)` — leaving

Gameplay hooks (return `HookResult` to block):
- `OnTakeDamage(TakeDamageEvent) → HookResult` — `Stop` blocks damage.
  Event has `Entity` (target) and `Info` (`CTakeDamageInfo` — attacker,
  ability, flags). See `ScourgePlugin.cs:36` for `args.Info.Ability?.SubclassVData?.Name`
  check idiom.
- `OnModifyCurrency(ModifyCurrencyEvent) → HookResult`
- `OnChatMessage(ChatMessage) → HookResult` — **chat commands run first** in
  `PluginLoader.ChatCommands.cs:14-47`; `OnChatMessage` only fires if no
  command matched OR command handler returned `Continue`.
- `OnClientConCommand(ClientConCommandEvent) → HookResult` — intercept
  client-side concommands like `changeteam`/`jointeam`/`respawn`
- `OnAddModifier(AddModifierEvent) → HookResult` — block modifier apply

Entity lifecycle:
- `OnEntityCreated(EntityCreatedEvent)` — instantiated
- `OnEntitySpawned(EntitySpawnedEvent)` — fully spawned, safe to modify
- `OnEntityDeleted(EntityDeletedEvent)` — destroy (also fires `EntityDataRegistry.OnEntityDeleted`)
- `OnEntityStartTouch(EntityTouchEvent)` / `OnEntityEndTouch(EntityTouchEvent)` —
  trigger entry/exit. `EntityTouchEvent` has `Entity` + `Other`.

Player input/transmit:
- `OnAbilityAttempt(AbilityAttemptEvent)` — **return-less**; plugin mutates
  `args.BlockedButtons` / `args.ForcedButtons` masks. Across plugins, bits
  are **OR'd** — any plugin can veto a button. See "AbilityAttemptEvent"
  section below.
- `OnProcessUsercmds(ProcessUsercmdsEvent)` — raw usercmds
- `OnCheckTransmit(CheckTransmitEvent)` — per-player PVS editing.
  **Must be called every tick** the entity should remain hidden (idempotent
  bit clear in a `ulong[]` transmit bitmap).

Config:
- `OnConfigReloaded()` — fires after `dw_reloadconfig`; plugin re-applies
  config (cancel/restart timers, etc.)

## HookResult semantics

`Continue = 0 < Stop = 1 < Handled = 2`. Host aggregates with
**max-wins** (`if (hr > result) result = hr;` in `DispatchGameEvent`).
All plugins always see the event — no plugin can short-circuit dispatch
to others. The aggregate then decides whether the native engine path
continues.

## AbilityAttemptEvent — block/force abilities (not boolean, mask-based)

Shape (`AbilityAttemptEvent.cs`):
- `PlayerSlot`, `HeldButtons`, `ChangedButtons`, `ScrollButtons` (`InputButton` bitmask)
- `BlockedButtons { get; set; }` — set bits to suppress
- `ForcedButtons { get; set; }` — set bits to inject presses
- Helpers: `Block(InputButton)`, `BlockAllAbilities()`, `BlockAllItems()`,
  `BlockAll()`, `Force(InputButton)`, `ForceAllAbilities()`, `ForceAllItems()`
- Queries: `IsHeld(b)`, `IsChanged(b)`, `IsAnyAbilityHeld`, `IsAnyItemHeld`
- `Controller` property lazy-resolves controller from slot

Cross-plugin aggregation: blocked/forced masks from multiple plugins are
OR'd together (documented on the property). TagPlugin uses this to block
all abilities+items across both teams in tag mode.

## CheckTransmitEvent — per-player entity visibility

Constructor takes `ulong*` into the native transmit bitmap. `Hide(entity)`
clears bit `index` using `_transmitBits[index >> 6] &= ~(1UL << (index & 63))`
— standard 64-bit-word bitmap indexing. `IsTransmitting(entity)` queries the
same. Must be re-applied every tick (engine rebuilds the default list fresh
each time).

## GameEvent — Source 2 engine events

Separate from plugin hooks: these are engine-fired events like
`player_death`, `round_start`. Register a handler with
`[GameEventHandler("player_death")]` on a plugin method.

Handler signature options (`PluginLoader.Events.cs:30-50`):
- `HookResult OnDeath(GameEvent e)` — generic, receives any event
- `HookResult OnDeath(PlayerDeathEvent e)` — **typed**, host-generated
  subclass with strongly-typed properties

Typed classes are generated by `GameEventSourceGenerator` from `.gameevents`
files (see separate note). The host routes typed handlers through a
runtime `IsInstanceOfType` check — if the engine fires a differently-typed
event, a typed handler returns `HookResult.Continue` (skipped).

Generic `GameEvent` has accessors: `GetBool/Int/Float/String/Uint64(key, def)`,
`SetBool/Int/Float/String(key, val)`, `GetPlayerController(key)`,
`GetPlayerPawn(key)`, `GetEHandle(key)`. EHandle `0xFFFFFFFF` is the null
sentinel.

Per-event subscription also works without the attribute via
`GameEvents.AddListener("name", handler)` returning `IHandle`. (Manual
registration path — `PluginLoader.Events.cs:62-77`.)
