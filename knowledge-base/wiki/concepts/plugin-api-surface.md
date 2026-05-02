---
title: Plugin API Surface
type: concept
sources:
  - raw/notes/2026-04-22-deadworks-plugin-api-surface.md
  - raw/notes/2026-04-23-plugin-bus.md
  - raw/notes/2026-04-23-entity-io-api.md
  - raw/notes/2026-04-23-trace-api.md
  - raw/notes/2026-04-23-soundevent-builder.md
  - knowledge-base/raw/articles/deadworks-0.4.6-release.md
  - knowledge-base/raw/articles/deadworks-0.4.7-release.md
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
  - "[[plugin-bus]]"
  - "[[entity-io]]"
  - "[[trace-api]]"
  - "[[examples-index]]"
  - "[[deadworks-scan-2026-04-22]]"
  - "[[deadworks-scan-2026-04-23]]"
  - "[[deadworks-0.4.6-release]]"
  - "[[deadworks-0.4.7-release]]"
  - "[[observer-api]]"
created: 2026-04-22
updated: 2026-05-02
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
| `Precache` | `AddResource(path)`, `AddHero(Heroes.X)` — call from `OnPrecacheResources`. **v0.4.6**: the host no longer auto-precaches every `AvailableInGame` hero. Plugins that dynamically swap heroes on players must call `Precache.AddHero` explicitly from `OnPrecacheResources` for each hero they intend to grant. |
| `Server` | `MapName`, `ExecuteCommand`, `ClientCommand`, `EnumerateConVars`, `EnumerateConCommands`, `AddSearchPath`, `SetAddons`, `AddEngineLogListener` |
| `Sounds`, `SoundEvent` | `Sounds.Play`, `Sounds.PlayAt` helpers; **`SoundEvent` builder** (fluent `SetFloat/SetUInt32/SetFloat3/…` + `.Emit(RecipientFilter) → GUID`, `SetParams(guid, …)`, `SoundEvent.Stop(guid, …)`, `SoundEvent.StopByName(name, src, …)`). Wire format is SOS-packed-params; field names are MurmurHash2-lowercased. Added upstream `c0f977b` (2026-04-22). |
| `Utf8` | `Utf8.Encode(string, Span<byte>)`, `Utf8.Size(string)` — stackalloc-friendly UTF-8 |
| `MurmurHash2` | hash helper |
| `Damage` | `CTakeDamageInfo` struct; `TakeDamageFlags` |

## Subsystems (one subfolder per)

| Folder | Page |
|--------|------|
| `Bus/` | [[plugin-bus]] — plugin-to-plugin events + queries, `[EventHandler]` / `[QueryHandler]`, `dw_pluginbus` diagnostics |
| `Commands/` | [[command-attribute]] — unified `[Command]` attribute |
| `ConCommands/` | `[ConCommand]`, `[ConVar]` — deprecated; migrate to `[Command]` |
| `Config/` | [[plugin-config]] — `[PluginConfig]`, `IConfig.Validate`, hot-reload |
| `Entities/` | [[schema-accessors]] — entity wrappers, schema access, `Players`, `EntityData<T>` |
| `Enums/` | game constants (see enum reference below) — v0.4.7 adds **`ObserverMode_t`** |
| `Events/` | [[events-surface]] — event payload types + game-event typed classes; also [[entity-io]] (`EntityIO.HookOutput/HookInput` for mapper-wired entity I/O) |
| `NetMessages/` | [[netmessages-api]] — protobuf message send/hook |
| `Timer/` | [[timer-api]] — per-plugin timer service |
| `Trace/` | [[trace-api]] — VPhys2 raycast / shape cast (line, sphere, hull, capsule, mesh). Silent no-op when not ready; not currently used by any plugin |

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

## v0.4.7 API additions worth knowing

See [[deadworks-0.4.7-release]] for the full list + commit map. Summary:

- **`CBaseEntity.SetScale(float scale)`** — vtable call (offset 246); `1.0` = default.
  Works on any entity.
- **`CBaseEntity.ModelName`** — read-only property; returns the current model path
  (e.g. `"models/heroes_wip/werewolf/werewolf.vmdl"`) or `""`.
- **`IDeadworksPlugin.OnPawnHeroInitialized(CCitadelPlayerPawn)`** — new 24th hook
  (was 23 in v0.4.6). Fires after hero abilities/modifiers are populated server-side.
  Trigger paths: initial spawn, `SelectHero`, `ResetHero`, `resethero` command.
  See [[events-surface]].
- **`CCitadelPlayerPawn.SwapOrReset(Heroes, Action?)`** — high-level helper: calls
  `SelectHero` or `ResetHero` depending on current hero; `onReady` callback fires once
  abilities are ready (via `OnceHeroInitialized`).
- **`CCitadelPlayerPawn.OnceHeroInitialized(Action)`** — one-shot continuation, runs
  the next time `InitializeHeroOnPawn` fires for this pawn. Auto-cleaned on entity delete.
- **`CBasePlayerController.Pawn`** — new `m_hPawn` schema accessor (`CBasePlayerPawn?`).
- **Observer/Spectator API** — `CPlayer_ObserverServices`, `ObserverMode_t` enum,
  `CBasePlayerPawn.ObserverServices/Mode/Target/SetXxx`,
  `CCitadelPlayerController.MakeObserver()`. See [[observer-api]].

## v0.4.6 API additions worth knowing

See [[deadworks-0.4.6-release]] for the full list + commit map. Summary:

- **`Precache.AddHero` no longer auto-called for every hero.** The
  host's `OnPrecacheResources` auto-loop was removed; plugins that
  grant arbitrary heroes must precache manually.
- **`Entities.ByName(name)` / `Entities.ByName<T>(name)` /
  `Entities.FirstByName(name)` / `Entities.FirstByName<T>(name)`** —
  find entities by targetname (case-sensitive). Cursor-based native
  lookup against the engine's targetname index; faster than scanning
  `Entities.All` with a `.Where(...)`.
- **`CCitadelAbilityComponent.FindAbilityByName(string)`** — returns
  `CCitadelBaseAbility?` by internal ability name
  (e.g. `"citadel_ability_primary_dash"`).
- **`CCitadelPlayerPawn.RemoveAbility(CCitadelBaseAbility)`** — new
  overload that takes an ability entity directly (returns `bool`),
  alongside the existing name/index overloads.
- **`CCitadelPlayerPawn.GetStamina() / SetStamina(float)`** — one-call
  stamina read/write. `SetStamina` writes `CurrentValue`, `LatchValue`,
  and `LatchTime = GlobalVars.CurTime` in one step.
- **`EntityData<T>` is `IEnumerable<KeyValuePair<CBaseEntity, T>>`** and
  exposes `Count`. `foreach (var (ent, val) in _data)` now works;
  do not mutate during iteration. See [[schema-accessors]].
- **`CBaseEntity` equality is now handle-based.** `==` / `!=` /
  `Equals` / `GetHashCode` compare the packed `EntityHandle`
  (serial + index). Wrappers constructed twice for the same native
  entity are now equal.
- **`AbilityResource.LatchTime` / `LatchValue` setters now network
  properly** — prior versions bypassed `NotifyStateChanged` via raw
  pointer writes; v0.4.6 routes through `SchemaAccessor.Set` with the
  networked flag set. Coupled with `SetStamina` above.
