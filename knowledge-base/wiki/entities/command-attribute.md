---
title: "[Command] Attribute"
type: entity
sources:
  - raw/notes/2026-04-22-deadworks-command-attribute.md
  - ../deadworks/managed/DeadworksManaged.Api/Commands/CommandAttribute.cs
  - ../deadworks/managed/DeadworksManaged.Api/Commands/CommandConverters.cs
  - ../deadworks/managed/DeadworksManaged.Api/Commands/CommandException.cs
  - ../deadworks/managed/Commands/CommandBinder.cs
  - ../deadworks/managed/Commands/CommandTokenizer.cs
  - ../deadworks/managed/PluginLoader.ChatCommands.cs
related:
  - "[[plugin-api-surface]]"
  - "[[deadworks-runtime]]"
  - "[[deadworks-0.4.5-release]]"
  - "[[examples-index]]"
created: 2026-04-22
updated: 2026-04-22
confidence: high
---

# `[Command]` attribute

The v0.4.5 unified command registration. A single
`[Command("name", aliases...)]` registers all three surfaces at once:
`/name` (chat slash), `!name` (chat bang), `dw_name` (server console).
Supersedes `[ChatCommand]` and `[ConCommand]` (both `[Obsolete]`).

## Attribute shape

`[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]` — a
method can carry multiple attributes.

| Property | Behaviour |
|----------|-----------|
| `Description` | Free text, shown in `dw_help` |
| `ServerOnly` | Refuse any player caller (handler silently skipped) |
| `ChatOnly` | Skip `dw_name` registration |
| `ConsoleOnly` | Skip `/name` and `!name` registration |
| `SuppressChat` | Hide `!name` invocation from chat broadcast |
| `Hidden` | Exclude from `dw_help` listing |

## Handler signature

Reflection over the method parameters builds a plan of four slot kinds
(`CommandBinder.Slot`):

| Slot | Parameter type | Behaviour |
|------|----------------|-----------|
| `Caller` | `CCitadelPlayerController` (non-nullable) | Player caller required; silently skipped if called from console |
| `Caller` | `CCitadelPlayerController?` (nullable) | Accepts null — called from server console works |
| `RawArgs` | `string[] rawArgs` (case-insensitive name) | Full token array, cursor not advanced |
| `Typed` | scalar/enum/registered-type | Single token; default values supported |
| `Params` | `params T[]` (must be last) | Consumes remaining tokens |

Built-in scalar converters: `int`, `long`, `float`, `double`, `bool`,
`string`, enums (parsed case-insensitive). Custom types via
`CommandConverters.Register<T>(Func<string, T>)` — register from
`OnLoad`; overwrites any prior registration.

## Tokenization

`CommandTokenizer` — whitespace-separated; double-quoted segments group.
Escape sequences inside quotes: `\"` → `"`, `\\` → `\`. Outside quotes,
backslashes are literal.

## Error handling

- **Wrong arg count / convert failure / excess tokens**: binder builds a
  usage string like `Usage: ir_start <slot:int> [target:string=all]` and
  sends it to the caller. Optional `[...]` brackets for defaults;
  `<...>` for required; `[name:T...]` for params.
- **`CommandException`**: throw from inside a handler to send
  `Exception.Message` back to the caller. Used throughout the example
  plugins for domain validation:
  ```csharp
  if (Config.ItemSets.Count == 0)
      throw new CommandException("[ItemRotation] No item sets configured.");
  ```

## Canonical examples (from [[examples-index]])

Console-only admin command with optional path:
```csharp
[Command("cvardump",
    Description = "Dump all ConVars and ConCommands to a JSON file",
    ServerOnly = true,
    ConsoleOnly = true)]
public void CmdCvarDump(string outputPath = "")
{ ... }
```

Player command with typed arg + default:
```csharp
[Command("additem", Description = "Give an item directly (no cost). Set enhanced=true for the upgraded version.")]
public void CmdAddItem(CCitadelPlayerController caller, string itemName, bool enhanced = false)
{ ... }
```

Params-style rcon forwarder with suppressed chat echo:
```csharp
[Command("rcon", Description = "Execute a server console command", SuppressChat = true)]
public void CmdRcon(CCitadelPlayerController? caller, params string[] commandParts)
{
    if (commandParts.Length == 0)
        throw new CommandException("Nothing to execute.");
    Server.ExecuteCommand(string.Join(' ', commandParts));
}
```

## Host dispatch

`PluginLoader.ChatCommands.cs:14-47` — `DispatchChatMessage` strips `/`
or `!` prefix, splits on whitespace, looks up handlers by bare command
name. **Both `/foo` and `!foo` dispatch to the same handler.**

> **Reconciliation**: a prior log entry (2026-04-21) flagged LockTimer's
> `[ChatCommand("zones")]` (no `!` prefix) as a latent bug inconsistent
> with the `!`-prefix convention. **That is not a bug.** The host strips
> the prefix before registry lookup; the bare name is the registration
> key. Conventions using `!` prefixes in the attribute name (as in
> Deathmatch's `[ChatCommand("!help")]`) work because the dispatcher
> strips the user's `!` before lookup — the registered key becomes
> literally `"!help"`, which matches `!help` but NOT `/help`. So the
> `!`-in-name convention actually **restricts** the command to the
> bang surface only.

## Migration from `[ChatCommand]` / `[ConCommand]`

Both are `[Obsolete]` with `CS0618` pragma suppressions at the scan
sites. Removal is planned. For this repo's three plugins, see
[[deadworks-0.4.5-release]] — LockTimer, StatusPoker, and DeathmatchPlugin
still use `[ChatCommand]` (LockTimer was migrated in commit `6ace83c`
to `[Command]`).

**Mechanical migration**:
- `[ChatCommand("!help")]` → `[Command("help", ChatOnly = true)]` (if you
  want to preserve the chat-only surface) or `[Command("help")]` (to
  also expose `dw_help` and `/help`)
- Handler signature: drop `ChatCommandContext ctx` parameter; drop
  `HookResult` return (now `void`); add typed args matching the args you
  were parsing manually from `ctx.Args`
- Return values replaced with `throw new CommandException(msg)` for
  validation errors

## Built-in `dw_` commands (not an exhaustive list)

- `dw_help` — lists all commands (except `Hidden`-marked)
- `dw_reloadconfig` — triggers [[plugin-config|config hot-reload]] for
  all plugins
- `dw_cvardump` (from DumperPlugin when loaded) — dumps convars to json
