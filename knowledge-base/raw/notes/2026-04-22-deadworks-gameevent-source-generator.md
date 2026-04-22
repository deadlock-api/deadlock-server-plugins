---
date: 2026-04-22
task: scan GameEventSourceGenerator
files:
  - ../deadworks/managed/DeadworksManaged.Generators/GameEventSourceGenerator.cs
  - ../deadworks/managed/DeadworksManaged.Generators/GameEventParser.cs
  - ../deadworks/managed/DeadworksManaged.Generators/DeadworksManaged.Generators.csproj
  - ../deadworks/managed/DeadworksManaged.Api/Events/GameEvent.cs
---

# GameEventSourceGenerator

Compile-time C# source generator (`[Generator(LanguageNames.CSharp)]`,
`IIncrementalGenerator`) that reads `.gameevents` files from
`AdditionalTextsProvider` and emits two files into the
`DeadworksManaged.Api` namespace:

1. `GameEvents.g.cs` — one `<Name>Event : GameEvent` class per event
   with typed properties
2. `GameEventFactory.g.cs` — `public static partial class GameEventFactory`
   with a single `Create(string name, nint handle) → GameEvent` method
   that `switch`-maps event names to their generated classes

## File ordering — core.gameevents < game.gameevents

Critical: files are sorted by filename before processing. From the
generator source:

```csharp
// Sort by filename so core.gameevents (c) is processed before game.gameevents (g).
// Last-seen wins, so game.gameevents overrides core.gameevents for duplicate events.
var sorted = allFiles.OrderBy(f => f.fileName, StringComparer.OrdinalIgnoreCase).ToList();
```

If a plugin ever supplies a `.gameevents` file with a name starting with
'a' or 'b' (alphabetically before 'c'), it would be **overridden** by
`core.gameevents`. This is an implicit ordering invariant.

## Field type → C# property mapping

From `EmitProperty`:

| `.gameevents` type | C# property type | Accessor |
|--------------------|------------------|----------|
| `string` | `string` | `GetString(name)` |
| `bool` | `bool` | `GetBool(name)` |
| `byte`, `short`, `long`, `int` | `int` | `GetInt(name)` (**narrowed to int**) |
| `float` | `float` | `GetFloat(name)` |
| `uint64` | `ulong` | `GetUint64(name)` |
| `player_controller` | `CBasePlayerController?` | `GetPlayerController(name)` |
| `player_pawn` | `CBasePlayerPawn?` | `GetPlayerPawn(name)` |
| `player_controller_and_pawn` | `CBasePlayerController? <Name>Controller` + `CBasePlayerPawn? <Name>Pawn` | two properties |
| `ehandle` | `CBaseEntity?` | `GetEHandle(name)` |

**Surprise: `long`, `short`, `byte` all map to `int`.** A `long` field in
the `.gameevents` file is silently narrowed — there's no `GetLong` in the
generator. For 64-bit values use `uint64` (→ `ulong`).

## Naming: snake_case → PascalCase

`ToPascalCase("player_death") → "PlayerDeath"`, `"player_death"` →
`PlayerDeathEvent`. An event named `on_event` with fields `player` and
`target` produces:

```csharp
public sealed class OnEventEvent : GameEvent {
    internal OnEventEvent(nint handle) : base(handle) { }
    public CBasePlayerController? Player => GetPlayerController("player");
    public CBaseEntity? Target => GetEHandle("target");
}
```

## `new` modifier for name collisions

The base `GameEvent` has `Name` and `Handle` properties. If a
`.gameevents` field collides with those names, the generator emits
`new` to hide the base member:

```csharp
private static readonly HashSet<string> BasePropertyNames =
    new HashSet<string> { "Name", "Handle" };
// ...
var newModifier = BasePropertyNames.Contains(propName) ? "new " : "";
```

## Zero-field events skipped

`if (evt.Fields.Count == 0) continue;` — events without fields don't get
a typed class (they'd just inherit `GameEvent` unchanged). The factory
falls back to `new GameEvent(handle)` for those.

## Usage from plugin code

```csharp
[GameEventHandler("player_death")]
public HookResult OnDeath(PlayerDeathEvent e) {
    var killer = e.Attacker;      // typed CBasePlayerController?
    var weapon = e.Weapon;        // typed string
    return HookResult.Continue;
}
```

Host dispatch is in `PluginLoader.Events.cs:30-50` — typed handlers go
through a `Type.IsInstanceOfType(e)` check, so you can have multiple
typed handlers on different event subclasses with the same event name
and only matching ones fire.

## Integration

Consumer: only `DeadworksManaged.Api` has the generator as a
`ProjectReference` with `OutputItemType="Analyzer"` — the generated
classes end up in the `Api` assembly. Plugins just reference the Api and
get the typed classes for free.

The `.gameevents` files themselves come from the Source SDK (`sourcesdk/`
submodule); plugin csprojs do not generally provide their own.
