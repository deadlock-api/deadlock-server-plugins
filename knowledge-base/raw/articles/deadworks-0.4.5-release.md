---
source: user-provided release summary (2026-04-22)
upstream: https://github.com/Deadworks-net/deadworks/releases
version: 0.4.5
captured: 2026-04-22
---

# Deadworks v0.4.5 — release notes

> Release page will appear at <https://github.com/Deadworks-net/deadworks/releases>.
> Content below is the user-provided summary of what ships in v0.4.5.

## Changes

- **Default port reverted to `27067`** — avoids conflicts with the game client.
- `CCitadelPlayerPawn.AddItem` now takes an additional `bool enhanced = false`
  parameter. Passing `true` gives an enhanced item.
- Exposed `CCitadelPlayerPawn.HeroID`.
- Added `CBasePlayerController.Slot` to replace the `CBasePlayerController.EntityIndex - 1`
  idiom for obtaining a player slot.
- Fixed `CBasePlayerController.PrintToConsole`.
- Added an API for sending soundevents directly to players.
- Added a new unified command API using the `[Command]` attribute, and
  **deprecated** `[ChatCommand]` and `[ConCommand]`. The latter two will be
  removed in a future release. Feedback on the new attribute is requested.

## `[Command]` migration example

Before:

```cs
[ChatCommand("heal", Description = "Heal yourself")]
public HookResult OnHealChat(ChatCommandContext ctx)
{
    if (ctx.Message.Controller == null) return HookResult.Continue;
    if (ctx.Args.Length < 1 || !int.TryParse(ctx.Args[0], out var amount))
    {
        ctx.Message.Controller.PrintToConsole("Usage: /heal <amount>");
        return HookResult.Handled;
    }
    ctx.Message.Controller.Heal(amount);
    return HookResult.Handled;
}
```

After:

```cs
// registers both dw_heal in console, /heal and !heal
[Command("heal", Description = "Heal yourself")]
public void Heal(CCitadelPlayerController caller, int amount)
{
    caller.Heal(amount);
}
```

Semantics of the new attribute (as stated in the release summary):

- A single `[Command("heal")]` registers **three surface forms** at once:
  - console concommand: `dw_heal`
  - chat command (slash): `/heal`
  - chat command (bang): `!heal`
- The handler method signature is simplified: first parameter is the caller
  (`CCitadelPlayerController`), subsequent parameters are typed command
  arguments (e.g. `int amount`) — the host parses `ctx.Args[0]` into the
  declared parameter type rather than the plugin doing `int.TryParse`
  itself.
- Return type changes from `HookResult` to `void` — no explicit `Continue`/
  `Handled` decision at the plugin level.
