---
title: Plugin Event Hook Surface
type: entity
sources:
  - raw/notes/2026-04-22-deadworks-events-surface.md
  - ../deadworks/managed/DeadworksManaged.Api/IDeadworksPlugin.cs
  - ../deadworks/managed/DeadworksManaged.Api/DeadworksPluginBase.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/
  - ../deadworks/managed/DeadworksManaged.Api/Enums/HookResult.cs
  - ../deadworks/managed/PluginLoader.Events.cs
related:
  - "[[plugin-api-surface]]"
  - "[[gameevent-source-generator]]"
  - "[[deadworks-runtime]]"
  - "[[schema-accessors]]"
  - "[[examples-index]]"
created: 2026-04-22
updated: 2026-04-22
confidence: high
---

# Plugin Event Hook Surface

The full list of override points on `IDeadworksPlugin`. All methods have
default no-op implementations — override only what you use.

## Lifecycle / frame

| Hook | Signature | When |
|------|-----------|------|
| `OnLoad(bool isReload)` | `void` | On first load and on hot-reload; flag distinguishes |
| `OnUnload()` | `void` | Before unload; the framework has already cancelled this plugin's timers |
| `OnPrecacheResources()` | `void` | Map precache phase — use `Precache.AddResource/AddHero` here |
| `OnStartupServer()` | `void` | New map load (also triggers `IHandle.CancelOnMapChange`) |
| `OnGameFrame(bool simulating, bool firstTick, bool lastTick)` | `void` | Every engine tick |

## Client connection (strict order per connect)

| Hook | Returns | Notes |
|------|---------|-------|
| `OnClientConnect(ClientConnectEvent)` | `bool` | Reject with `false`. **All plugins see the event regardless of any individual result** — no short-circuit |
| `OnClientPutInServer(ClientPutInServerEvent)` | `void` | Initial put-in |
| `OnClientFullConnect(ClientFullConnectEvent)` | `void` | **In-game ready.** This is when `Players.SetConnected(slot, true)` fires — `Players.GetAll()` starts including the player from this hook onward |
| `OnClientDisconnect(ClientDisconnectedEvent)` | `void` | Leaving; also reset any per-slot plugin state here |

## Gameplay hooks (block via `HookResult`)

| Hook | Payload | Stop blocks |
|------|---------|-------------|
| `OnTakeDamage(TakeDamageEvent)` | `Entity`, `Info : CTakeDamageInfo` (attacker, ability, flags) | damage applied |
| `OnModifyCurrency(ModifyCurrencyEvent)` | slot, type, amount, source | currency change |
| `OnChatMessage(ChatMessage)` | full chat payload | chat propagation. **Chat commands run FIRST** — `OnChatMessage` only fires if no command matched (or the command returned `Continue`) |
| `OnClientConCommand(ClientConCommandEvent)` | the client concommand | server-side execution. Intercepts `changeteam`, `jointeam`, `respawn`, etc. |
| `OnAddModifier(AddModifierEvent)` | target, modifier name, data | modifier apply |

## Entity lifecycle

- `OnEntityCreated(EntityCreatedEvent)` — instantiated
- `OnEntitySpawned(EntitySpawnedEvent)` — fully spawned, safe to modify
- `OnEntityDeleted(EntityDeletedEvent)` — destroy (also fires
  `EntityDataRegistry.OnEntityDeleted` — see [[schema-accessors]])
- `OnEntityStartTouch(EntityTouchEvent)` / `OnEntityEndTouch(EntityTouchEvent)`
  — trigger entry/exit. `EntityTouchEvent` has `Entity` + `Other`.

## Player input / transmit

- `OnAbilityAttempt(AbilityAttemptEvent)` — **return-less**; plugin mutates
  `args.BlockedButtons` / `args.ForcedButtons` masks. See
  "AbilityAttemptEvent" below.
- `OnProcessUsercmds(ProcessUsercmdsEvent)` — raw usercmds
- `OnCheckTransmit(CheckTransmitEvent)` — per-player PVS list editing. See
  "CheckTransmitEvent" below.

## Config

- `OnConfigReloaded()` — fires after `dw_reloadconfig`. Plugin
  re-applies config values (usually: cancel/restart timers with
  new intervals). See [[plugin-config]].

## `HookResult` semantics

```csharp
public enum HookResult { Continue = 0, Stop = 1, Handled = 2 }
```

Host aggregates with **max-wins**: `if (hr > result) result = hr;`. All
plugins always see the event — no plugin can short-circuit dispatch to
others. The aggregate value decides whether the native engine path
continues.

`Stop` blocks; `Handled` signals "I consumed this" (semantic marker, used
by a few paths like net messages where the engine would otherwise
process a suppressed message).

## `AbilityAttemptEvent` — mask-based block/force

Not boolean. The event carries button-state bitmasks
(`InputButton` flag enum):

- `HeldButtons`, `ChangedButtons`, `ScrollButtons` (read-only from engine)
- `BlockedButtons`, `ForcedButtons` (plugin writes)

Cross-plugin: blocked/forced masks are **OR'd** across all plugins — any
plugin can veto any button, and any plugin can inject a forced press.

Helpers on the event:
- `Block(InputButton)`, `BlockAllAbilities()`, `BlockAllItems()`, `BlockAll()`
- `Force(InputButton)`, `ForceAllAbilities()`, `ForceAllItems()`
- Queries: `IsHeld(b)`, `IsChanged(b)`, `IsAnyAbilityHeld`, `IsAnyItemHeld`
- `Controller` property lazy-resolves the player controller from `PlayerSlot`

## `CheckTransmitEvent` — per-player entity visibility

Constructor takes a pointer into the native transmit bitmap
(`ulong*`). `Hide(entity)` clears bit `index` via
`_transmitBits[index >> 6] &= ~(1UL << (index & 63))`. `IsTransmitting(entity)`
queries the same.

**Must be re-applied every tick** — the engine rebuilds the default
transmit list fresh each tick before firing this hook.

No example plugin uses this hook; it's for advanced per-player entity
hiding (e.g., one-way mirrors, invisible roles in game modes).

## Source 2 `GameEvent` — separate pathway

Native engine game events (e.g., `player_death`, `round_start`) are
**separate** from the `IDeadworksPlugin` hooks above. Register via
attribute:

```csharp
[GameEventHandler("player_death")]
public HookResult OnDeath(PlayerDeathEvent e) { ... }
```

Handler signature can be either:
- `HookResult h(GameEvent e)` — generic, receives any event
- `HookResult h(TypedEvent e)` — typed subclass, generated by
  [[gameevent-source-generator]]

Typed dispatch runs through a runtime `IsInstanceOfType(e)` check —
wrong-typed events return `Continue` (skipped).

Manual registration path: `GameEvents.AddListener("name", handler)` →
`IHandle`. Unregistered on `handle.Cancel()` or on plugin unload.

Generic `GameEvent` accessors:
- `GetBool/Int/Float/String/Uint64(key, def)`
- `SetBool/Int/Float/String(key, val)`
- `GetPlayerController(key)` → `CBasePlayerController?`
- `GetPlayerPawn(key)` → `CBasePlayerPawn?`
- `GetEHandle(key)` → `CBaseEntity?` (null sentinel: raw `0xFFFFFFFF`)
