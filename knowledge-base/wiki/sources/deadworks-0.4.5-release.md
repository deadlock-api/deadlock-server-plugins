---
title: "Source: Deadworks v0.4.5 release"
type: source-summary
sources:
  - knowledge-base/raw/articles/deadworks-0.4.5-release.md
related:
  - "[[deadworks-runtime]]"
  - "[[glossary]]"
created: 2026-04-22
updated: 2026-04-22
confidence: high
---

# Source: Deadworks v0.4.5 release

User-provided summary of the upcoming [Deadworks
v0.4.5](https://github.com/Deadworks-net/deadworks/releases) release. Captured
2026-04-22, before the GitHub release was published.

## Headline changes

1. **Default listen port reverted to `27067`** to avoid clashing with the
   Deadlock game client's default. Prior versions had changed away from this.
2. **`[Command]` attribute** introduced as the unified command API; replaces
   and **deprecates** both `[ChatCommand]` and `[ConCommand]`. A single
   `[Command("heal")]` registers `dw_heal` (console), `/heal` (chat slash),
   and `!heal` (chat bang) simultaneously. Handler signature is
   `(CCitadelPlayerController caller, <typed args>)` returning `void` —
   the host parses arg types from the method signature rather than the
   plugin doing `int.TryParse` on `ctx.Args[0]`.
3. Managed API additions/fixes:
   - `CCitadelPlayerPawn.AddItem(…, bool enhanced = false)` — pass `true`
     to grant an enhanced item.
   - `CCitadelPlayerPawn.HeroID` exposed.
   - `CBasePlayerController.Slot` exposed — canonical replacement for the
     `controller.EntityIndex - 1` idiom used by most existing plugins
     (see `Chat.PrintToChat` mapping noted in [[deadworks-runtime]]).
   - `CBasePlayerController.PrintToConsole` — previously broken, now fixed.
   - New API for sending soundevents directly to players.

## Impact on this repo

- **Port contradiction resolved on deadworks's side.** The previous log
  flagged a mismatch between README (27067) and the Docker flow (27015);
  this release re-aligns deadworks's *default* on 27067. The Docker
  compose flow in this repo still sets its own `SERVER_PORT` explicitly,
  so no action required unless we want to align defaults.
- **LockTimer, StatusPoker, DeathmatchPlugin all use `[ChatCommand]`** —
  this attribute is now deprecated. No immediate break (release says the
  old attributes are still registered, just marked for removal), but
  migration to `[Command]` will be needed before they are removed.
  LockTimer's already-flagged bare-name inconsistency (`[ChatCommand("zones")]`
  without `!`) can be retired during that migration since `[Command]`
  registers both `/` and `!` prefixes automatically.
- **Plugins still using `controller.EntityIndex - 1`** (e.g. the
  `Chat.PrintToChat` slot mapping noted on [[deadworks-runtime]]) can
  migrate to `controller.Slot`. This is a readability/correctness win,
  not required by the release.

## Feedback requested

The release notes explicitly ask for feedback on the new `[Command]`
attribute before the old ones are removed.
