---
title: Plugin API Surface
type: concept
sources:
  - raw/notes/2026-04-22-deadworks-plugin-api-surface.md
  - ../deadworks/managed/DeadworksManaged.Api/
related:
  - "[[deadworks-runtime]]"
  - "[[command-attribute]]"
  - "[[timer-api]]"
  - "[[events-surface]]"
  - "[[schema-accessors]]"
  - "[[netmessages-api]]"
  - "[[plugin-config]]"
  - "[[gameevent-source-generator]]"
  - "[[examples-index]]"
  - "[[deadworks-scan-2026-04-22]]"
created: 2026-04-22
updated: 2026-04-22
confidence: high
---

# Plugin API Surface

Umbrella map of everything a plugin csproj sees when it references
`DeadworksManaged.Api`. Each subsystem has its own detailed page; this
page is the index.

## The two-file minimum

A functioning plugin needs:

1. A class implementing `IDeadworksPlugin` (or extending
   [[deadworks-runtime|DeadworksPluginBase]])
2. A `.csproj` with a reference to `DeadworksManaged.Api` (see
   [[plugin-build-pipeline]] for the three reference modes)

All hook methods on `IDeadworksPlugin` have default no-op implementations
— override only what you use.

## Top-level helpers (flat under `DeadworksManaged.Api/`)

| Surface | Key entry points |
|---------|------------------|
| `Chat` | `PrintToChat(controller, msg)`, `PrintToChatAll(msg)` |
| `ConVar` | `ConVar.Find(name)?.SetInt/SetFloat/SetString(...)` |
| `GameRules` | `TotalPausedTicks`, `ServerPaused`, other game-state getters |
| `GlobalVars` | `CurTime` (float), `TickCount` (int), `IntervalPerTick` |
| `HeroData` | static — hero metadata lookup |
| `KeyValues3` | `new KeyValues3(); kv.SetFloat(k,v); pawn.AddModifier(name, kv)`; `IDisposable` |
| `ParticleSystem` | `CParticleSystem.Create(path).AtPosition(v).StartActive(true).Spawn()` |
| `Precache` | `AddResource(path)`, `AddHero(Heroes.X)` — call from `OnPrecacheResources` |
| `Server` | `MapName`, `ExecuteCommand`, `ClientCommand`, `EnumerateConVars`, `EnumerateConCommands`, `AddSearchPath`, `SetAddons`, `AddEngineLogListener` |
| `Sounds`, `SoundEvent` | `Sounds.Play`, `Sounds.PlayAt`; v0.4.5 adds single-player target path |
| `Utf8` | `Utf8.Encode(string, Span<byte>)`, `Utf8.Size(string)` — stackalloc-friendly UTF-8 |
| `MurmurHash2` | hash helper |
| `Damage` | `CTakeDamageInfo` struct; `TakeDamageFlags` |

## Subsystems (one subfolder per)

| Folder | Page |
|--------|------|
| `Commands/` | [[command-attribute]] — unified `[Command]` attribute |
| `ConCommands/` | `[ConCommand]`, `[ConVar]` — deprecated; migrate to `[Command]` |
| `Config/` | [[plugin-config]] — `[PluginConfig]`, `IConfig.Validate`, hot-reload |
| `Entities/` | [[schema-accessors]] — entity wrappers, schema access, `Players`, `EntityData<T>` |
| `Enums/` | game constants (see enum reference below) |
| `Events/` | [[events-surface]] — event payload types + game-event typed classes |
| `NetMessages/` | [[netmessages-api]] — protobuf message send/hook |
| `Timer/` | [[timer-api]] — per-plugin timer service |
| `Trace/` | `TraceSystem` — VPhys2 raycast / shape cast (not used in any example plugin) |

## Enum reference bundle

Under `Enums/`:

- **`AbilitySlot`** — Q, W, E, R, X, C, V
- **`ECurrencyType`**, **`ECurrencySource`** — for
  `pawn.ModifyCurrency(type, amount, source, silent?, forceGain?)`.
  Example: `(ECurrencyType.EGold, 50000, ECurrencySource.ECheats)`
- **`EntityFlags`** — `ENeedsThink`, `EDormant`, etc.
- **`EModifierState`** — `UnlimitedAirJumps`, `UnlimitedAirDashes`,
  `Silenced`, etc. Applied via `pawn.ModifierProp.SetModifierState(state, bool)`
- **`Heroes`** — hero ID constants; used with `Precache.AddHero` and
  `controller.SelectHero`
- **`Items`** — item ID constants
- **`InputButton`** — bit flags: individual ability/item buttons,
  `AllAbilities`, `AllItems`, movement (`Walk`, `Jump`, `Crouch`). Used
  in `AbilityAttemptEvent.Block(...)` and `.Force(...)`.
- **`HookResult`** — `Continue = 0 < Stop = 1 < Handled = 2`. Max-wins
  across plugins.
- **`LaneColor`** — chat lane color indicator
- **`GameRulesEnums`** — team IDs (`2` = Amber, `3` = Sapphire), game states

## Canonical idioms (seen across example plugins)

- **`private static readonly EntityData<T> _xxx = new();`** — auto-cleaning
  per-entity state. See [[schema-accessors]].
- **Cancel-then-rearm in `OnConfigReloaded`** — cancel old handles, call
  the same setup routine. See [[plugin-config]].
- **Reply helper**: route to console for players, `Console.WriteLine`
  for server callers:
  ```csharp
  private static void Reply(CCitadelPlayerController? to, string message) {
      if (to != null) to.PrintToConsole(message);
      else Console.WriteLine(message);
  }
  ```
- **Precache in `OnPrecacheResources`** — not in `OnLoad` (too early)
  and not in `OnStartupServer` (too late).
- **Config validation clamps in-place** rather than throwing.
- **`_rebroadcasting` re-entrance guard** — needed if you hook an
  outgoing message and `Send<T>()` the same type inside the hook.

## What's internal (plugins do NOT touch)

- `NativeInterop.cs` — static delegates populated by the native side
  on bootstrap (`GetPlayerController`, `GetSchemaField`, `TraceShapeFn`,
  …). Plugin code uses `Players.FromSlot` / `SchemaAccessor<T>`, not
  these directly.
- `PluginRegistry.cs` — plugin-instance tracking
- `TimerResolver.cs`, `ConfigResolver.cs` — indirection layers the host
  fills during bootstrap so `this.Timer` / `this.ReloadConfig()` can
  resolve to per-plugin instances
- `NativeCallbacks.cs` (managed side) — registrations for callbacks
  fired by native into managed

## v0.4.5 API additions worth knowing

See [[deadworks-0.4.5-release]] for the full list. Summary:

- `[Command]` unified attribute — see [[command-attribute]]
- `CCitadelPlayerPawn.AddItem(name, enhanced: false)` — `enhanced=true`
  grants upgraded item variants
- `CCitadelPlayerPawn.HeroID` — read directly on pawn (previously
  required schema accessor)
- `CBasePlayerController.Slot` — canonical replacement for the
  `controller.EntityIndex - 1` idiom
- `CBasePlayerController.PrintToConsole` — **fixed** in v0.4.5; prior
  versions silently no-op'd
- Single-player targeted sound emission path (`Sounds/`)
