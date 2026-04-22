---
title: GameEvent Source Generator
type: entity
sources:
  - raw/notes/2026-04-22-deadworks-gameevent-source-generator.md
  - ../deadworks/managed/DeadworksManaged.Generators/GameEventSourceGenerator.cs
  - ../deadworks/managed/DeadworksManaged.Generators/GameEventParser.cs
  - ../deadworks/managed/DeadworksManaged.Api/Events/GameEvent.cs
related:
  - "[[plugin-api-surface]]"
  - "[[events-surface]]"
  - "[[deadworks-sourcesdk]]"
created: 2026-04-22
updated: 2026-04-22
confidence: high
---

# GameEventSourceGenerator

Compile-time C# source generator in `DeadworksManaged.Generators`.
`[Generator(LanguageNames.CSharp)] IIncrementalGenerator`. Reads
`.gameevents` files from `AdditionalTextsProvider` and emits two files
into the `DeadworksManaged.Api` namespace:

1. **`GameEvents.g.cs`** — one `<Name>Event : GameEvent` class per event,
   with typed properties
2. **`GameEventFactory.g.cs`** — `public static partial class
   GameEventFactory` with a `Create(string name, nint handle)` that
   `switch`-maps event names to their generated classes

## Integration

Only `DeadworksManaged.Api` references the generator with
`OutputItemType="Analyzer"` — the generated classes live in the Api
assembly. Plugins reference the Api and get the typed classes for free.
`.gameevents` source files come from the [[deadworks-sourcesdk|sourcesdk]]
submodule; plugin csprojs don't supply their own.

## File ordering: alphabetical, last-seen wins

Files are sorted by filename before processing:

```csharp
// Sort by filename so core.gameevents (c) is processed before game.gameevents (g).
// Last-seen wins, so game.gameevents overrides core.gameevents for duplicate events.
var sorted = allFiles.OrderBy(f => f.fileName, StringComparer.OrdinalIgnoreCase).ToList();
```

Implicit ordering invariant: a `.gameevents` file starting alphabetically
before `core.gameevents` (`c`) would be overridden by core. Stick to
filenames that sort after the SDK's.

## Field type → C# property mapping

From `EmitProperty`:

| `.gameevents` type | C# type | Accessor |
|--------------------|---------|----------|
| `string` | `string` | `GetString(name)` |
| `bool` | `bool` | `GetBool(name)` |
| `byte`, `short`, `int`, `long` | `int` | `GetInt(name)` — **narrowed** |
| `float` | `float` | `GetFloat(name)` |
| `uint64` | `ulong` | `GetUint64(name)` |
| `player_controller` | `CBasePlayerController?` | `GetPlayerController(name)` |
| `player_pawn` | `CBasePlayerPawn?` | `GetPlayerPawn(name)` |
| `player_controller_and_pawn` | `<Name>Controller` + `<Name>Pawn` | two properties |
| `ehandle` | `CBaseEntity?` | `GetEHandle(name)` |

**Gotcha: `long`, `short`, `byte` all map to `int`.** A 64-bit field
declared as `long` gets silently narrowed — there's no `GetLong` in the
generator. For 64-bit values declare the field as `uint64` → `ulong`.

## Naming: snake_case → PascalCase

`ToPascalCase("player_death") → "PlayerDeath"`. Event `player_death` with
field `attacker:player_controller` generates:

```csharp
public sealed class PlayerDeathEvent : GameEvent {
    internal PlayerDeathEvent(nint handle) : base(handle) { }
    public CBasePlayerController? Attacker => GetPlayerController("attacker");
}
```

## Name-collision `new` modifier

`GameEvent` has `Name` and `Handle` properties. Event fields named
`name` or `handle` would collide; the generator emits `new` to hide
the base member:

```csharp
private static readonly HashSet<string> BasePropertyNames =
    new HashSet<string> { "Name", "Handle" };
var newModifier = BasePropertyNames.Contains(propName) ? "new " : "";
```

## Zero-field events skipped

`if (evt.Fields.Count == 0) continue;` — events without fields don't
get a typed class. The factory falls back to `new GameEvent(handle)`
for those, and `[GameEventHandler]` methods using the generic signature
`(GameEvent e)` still receive them.

## Usage from plugins

```csharp
[GameEventHandler("player_death")]
public HookResult OnDeath(PlayerDeathEvent e) {
    var killer = e.Attacker;      // typed CBasePlayerController?
    return HookResult.Continue;
}
```

Host dispatch (in `PluginLoader.Events.cs`) creates a typed delegate
`Func<T, HookResult>` and wraps it in a generic `GameEventHandler`
that does `eventType.IsInstanceOfType(e)` — wrong-typed events
return `Continue` and are skipped. So multiple typed handlers for
different event subclasses sharing the same event name can coexist;
only the matching one runs.

## Generator CSPROJ snippet

```xml
<ItemGroup>
  <ProjectReference Include="..\DeadworksManaged.Generators\DeadworksManaged.Generators.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false" />
</ItemGroup>

<ItemGroup>
  <AdditionalFiles Include="..\..\sourcesdk\game\shared\core.gameevents" />
  <AdditionalFiles Include="..\..\sourcesdk\game\shared\game.gameevents" />
</ItemGroup>
```

`OutputItemType="Analyzer"` + `ReferenceOutputAssembly="false"` are the
critical flags that make this a source-generator dependency rather than
a runtime one.
