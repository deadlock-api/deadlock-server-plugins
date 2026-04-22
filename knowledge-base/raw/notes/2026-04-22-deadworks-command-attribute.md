---
date: 2026-04-22
task: scan deadworks for [Command] attribute machinery
files:
  - ../deadworks/managed/DeadworksManaged.Api/Commands/CommandAttribute.cs
  - ../deadworks/managed/DeadworksManaged.Api/Commands/CommandConverters.cs
  - ../deadworks/managed/DeadworksManaged.Api/Commands/CommandException.cs
  - ../deadworks/managed/Commands/CommandBinder.cs
  - ../deadworks/managed/Commands/CommandTokenizer.cs
  - ../deadworks/managed/PluginLoader.ChatCommands.cs
---

# [Command] attribute surface (v0.4.5+)

Single `[Command("name", aliases...)]` registers `/name`, `!name`, and `dw_name`
at once with typed argument binding. `AllowMultiple = true` — a method can carry
multiple attributes.

**Attribute properties** (CommandAttribute.cs):
- `Description` — free text, shown in `dw_help`
- `ServerOnly` — refuse any player caller (host silently skips if caller is not null)
- `ChatOnly` — skip the `dw_name` console concommand registration
- `ConsoleOnly` — skip the `/name` / `!name` chat registration
- `SuppressChat` — hide `!name` invocation from chat broadcast
- `Hidden` — exclude from `dw_help` listing

## Tokenization (CommandTokenizer.Tokenize, 65 LOC)

Whitespace-separated, double-quoted segments group. Escape sequences inside
quotes: `\"` → `"`, `\\` → `\`. Outside quotes, backslashes are literal.
Empty input returns `[]`.

## Argument binding (CommandBinder.Build + TryBind)

Method signature is reflected into a `Plan` of `Slot`s, one per parameter. Four
slot kinds:

- `Caller` — parameter of type `CCitadelPlayerController`. At most one per method.
  Nullability annotation matters: `CCitadelPlayerController?` accepts a null
  caller (e.g. invocation from server console); non-nullable with null caller
  causes `silentSkip = true` — the binder drops the call silently. This is
  how `ServerOnly` effectively behaves when a player tries to invoke it.
- `RawArgs` — `string[] rawArgs` (case-insensitive name match). Handed the
  full tokens array without advancing the cursor.
- `Typed` — scalar. Supported built-ins: `int`, `long`, `float`, `double`,
  `bool`, `string`, enums (parsed case-insensitive). Custom types via
  `CommandConverters.Register<T>(Func<string,T>)`. Default values are
  honoured: `bool enhanced = false` makes `enhanced` optional.
- `Params` — `params T[]` — must be last parameter; consumes all remaining
  tokens. Example: `public void CmdRcon(CCitadelPlayerController? caller, params string[] commandParts)`.

Binding errors (wrong arg count, failed convert, excess tokens when no
`params`/`rawArgs` slot) produce an auto-generated usage string via
`BuildUsage(plan)` like `Usage: ir_start <slot:int> [target:string=all]`.

## CommandException

Thrown from a handler to send `Exception.Message` to the caller (per
attribute docstring). Used throughout the example plugins for validation:

```csharp
if (Config.ItemSets.Count == 0)
    throw new CommandException("[ItemRotation] No item sets configured.");
```

## CommandConverters

Concurrent dictionary keyed by `Type`. `Register<T>(parser)` from `OnLoad`.
Overwrites prior registration. `Unregister<T>()` available.

## Host dispatch

`PluginLoader.ChatCommands.cs` handles the chat side (see
`DispatchChatMessage`): strips `/` or `!` prefix, splits on whitespace,
looks up handlers keyed by bare command name. **Both `/foo` and `!foo`
dispatch to the same handler.** This reconciles the prior wiki note
claiming `[ChatCommand("zones")]` is a latent bug — it's not. The bare
name is the registration key; host strips the prefix before lookup.

`[ChatCommand]` and `[ConCommand]` are `[Obsolete]` — deprecation is
enforced with `#pragma warning disable CS0618` at the scan sites in
`PluginLoader.ChatCommands.cs`.
