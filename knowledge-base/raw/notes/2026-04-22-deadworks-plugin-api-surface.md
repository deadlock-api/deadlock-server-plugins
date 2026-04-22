---
date: 2026-04-22
task: catalogue the plugin-facing API surface at DeadworksManaged.Api
files:
  - ../deadworks/managed/DeadworksManaged.Api/
---

# Plugin-facing API surface umbrella

Files in `managed/DeadworksManaged.Api/` as of v0.4.5. This is the
complete public surface a plugin csproj sees when referencing
`DeadworksManaged.Api`.

## Top-level helpers

| File | Purpose |
|------|---------|
| `IDeadworksPlugin.cs` | 23 lifecycle/event hook methods with default no-ops |
| `DeadworksPluginBase.cs` | Abstract base — virtuals + protected `Timer` property |
| `Chat.cs` | `Chat.PrintToChat(controller, msg)`, `Chat.PrintToChatAll(msg)` |
| `ConVar.cs` | `ConVar.Find(name)?.SetInt/SetFloat/SetString(...)` — runtime cvar mutation |
| `Damage.cs` | `CTakeDamageInfo` wrapper struct; `TakeDamageFlags` |
| `GameRules.cs` | `GameRules.TotalPausedTicks`, `ServerPaused`, other state |
| `GlobalVars.cs` | `GlobalVars.CurTime` (float), `TickCount` (int), `IntervalPerTick` |
| `HeroData.cs` | `HeroData` static — hero metadata accessor |
| `HeroTypeExtensions.cs` | Extension methods on `Heroes` enum |
| `KeyValues3.cs` | `new KeyValues3(); kv.SetFloat(key, val)` — modifier-param builder; `IDisposable` |
| `MurmurHash2.cs` | Hash helpers (used for sound event IDs / symbol table) |
| `NativeCallbacks.cs` | Internal — callbacks fired by native into managed |
| `NativeInterop.cs` | Internal — managed-to-native function pointer table |
| `ParticleSystem.cs` | `CParticleSystem.Create(path).AtPosition(v).StartActive(true).Spawn()` fluent builder |
| `PluginRegistry.cs` | Internal — plugin instance registry |
| `Precache.cs` | `Precache.AddResource(path)`, `Precache.AddHero(Heroes.X)` |
| `SchemaFieldResult.cs` | Internal struct returned by `NativeInterop.GetSchemaField` |
| `Server.cs` | `Server.MapName`, `ExecuteCommand`, `ClientCommand`, `EnumerateConVars`, `EnumerateConCommands`, `AddSearchPath`, `SetAddons`, `AddEngineLogListener` |
| `Utf8.cs` | `Utf8.Encode(string, Span<byte>)`, `Utf8.Size(string)` — stackalloc-friendly UTF-8 |

## Subdirectories

| Dir | Contains | Purpose |
|-----|----------|---------|
| `Commands/` | `CommandAttribute`, `CommandConverters`, `CommandException` | Unified `[Command]` attribute surface (see command-attribute note) |
| `ConCommands/` | `ConCommandAttribute`, `ConCommandContext`, `ConVarAttribute`, `FCVar` | Legacy console concommand registration (deprecated — use `[Command]`) |
| `Config/` | `IConfig`, `PluginConfigAttribute`, `ConfigResolver`, `ConfigExtensions` | Plugin config loading/hot-reload (see plugin-config note) |
| `Entities/` | `CBaseEntity`, `CBaseModifier`, `CBodyComponent`, `CEntityKeyValues`, `CGameSceneNode`, `CPointWorldText`, wrapper classes + `SchemaAccessor` family + `Players` + `EntityData` | Entity wrappers + schema access (see schema-accessors note) |
| `Enums/` | `AbilitySlot`, `Combat`, `Currency`, `EntityFlags`, `GameRulesEnums`, `Heroes`, `HookResult`, `InputButton`, `Items`, `LaneColor`, `ModifierState`, `WorldText` | Strongly-typed game constants |
| `Events/` | `AbilityAttemptEvent`, `AddModifierEvent`, `ChatMessage`, `CheckTransmitEvent`, `ClientConCommandEvent`, `Client*ConnectEvent`, `Entity*Event`, `GameEvent`, `GameEventHandlerAttribute`, `ModifyCurrencyEvent`, `ProcessUsercmdsEvent`, `TakeDamageEvent`, `ChatCommandAttribute` (obsolete), `ChatCommandContext` (obsolete), `EntityIO`, `GameEventWriter`, `GameEvents` | Event payload types + the generated `*Event` classes |
| `NetMessages/` | `NetMessages`, `NetMessageRegistry`, `NetMessageHandlerAttribute`, `NetMessageDirection`, `RecipientFilter`, `IncomingMessageContext<T>`, `OutgoingMessageContext<T>` | Protobuf message send/hook (see netmessages-api note) |
| `Sounds/` | `Sounds`, `SoundEvent`, `SoundEventField` | Sound emission; v0.4.5 adds single-player target path |
| `Timer/` | `ITimer`, `IStep`, `IHandle`, `Duration`, `Pace`, `TimerResolver` | Per-plugin timer service (see timer-api note) |
| `Trace/` | `TraceSystem`, `TraceResults`, `TraceShapes`, `TraceEnums` | VPhys2 raycast / shape cast |

## Enums — reference bundle

Useful to know what exists (all under `Enums/`):

- **`AbilitySlot`** — Q, W, E, R, X, C, V ability bindings
- **`ECurrencyType`**, **`ECurrencySource`** — currency API args
  (`ECurrencyType.EGold`, `ECurrencySource.ECheats` seen in TagPlugin)
- **`EntityFlags`** — `ENeedsThink`, `EDormant`, etc.
- **`InputButton`** — bit flags: individual ability/item buttons,
  `AllAbilities`, `AllItems`, movement (`Walk`, `Jump`, `Crouch`), etc.
- **`EModifierState`** — `UnlimitedAirJumps`, `UnlimitedAirDashes`,
  `Silenced`, etc. (used via `pawn.ModifierProp.SetModifierState(...)`)
- **`Heroes`** — hero ID constants for `Precache.AddHero` and
  `controller.SelectHero`
- **`Items`** — item ID constants
- **`HookResult`** — `Continue = 0 < Stop = 1 < Handled = 2`
- **`LaneColor`** — chat lane color indicator
- **`GameRulesEnums`** — team IDs (2 = Radiant Amber, 3 = Dire Sapphire
  — seen as `SeekerTeam = 2, HiderTeam = 3` in TagPlugin), game states

## Internal plumbing (plugins don't touch, but worth knowing)

- `NativeInterop.cs` — static delegate fields like `GetPlayerController`,
  `GetSchemaField`, `NotifyStateChanged`, `TraceShapeFn`. Populated by
  the native side on startup.
- `PluginRegistry.cs` — tracks plugin instances
- `TimerResolver.cs` — indirection layer; the host sets
  `TimerResolver.Resolve` during bootstrap so `this.Timer` can find its
  per-plugin instance
- `ConfigResolver.cs` — same pattern for config
- `NativeCallbacks.cs` — callbacks the native side invokes into managed
