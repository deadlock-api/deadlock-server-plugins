---
date: 2026-05-29
task: assess whether deadworks usercmd-visitor speedup (commit 1b94487) applies to this repo
files: [LockTimer/LockTimerPlugin.cs, LockTimer/Hud/SpeedHud.cs]
---

Deadworks commit `1b94487` ("Add mounted usercmd visitor path") makes the native
`CBasePlayerController::ProcessUsercmds` hook opt-in. Default native mode is now
`MountedPolicy`; `PluginLoader.ReconcileUsercmdMounts()`
(deadworks/managed/PluginLoader.cs:428) mounts work only when a plugin *overrides*
`OnProcessUsercmds` (→ `FullProtobuf`), `OnFastProcessUsercmds` (→ `FastRead`), or
`OnUsercmdTrigger` (→ `ButtonTriggers`), detected by reflection in
`OverridesPluginMethod` (PluginLoader.cs:457). With no override, the native hook
skips serializing `CCitadelUserCmdPB` and reading nested fields every tick.

**No plugin in this repo touches usercmds at all** (grepped: zero `OnProcessUsercmds`
overrides, zero `Usercmd*` references). So the speedup applies **automatically with
zero code changes** the moment plugins run on this build — they never paid the
serialize cost via an override, and the native side now skips it by default. There is
no expensive call site to convert to the fast path; adding `OnFastProcessUsercmds`
would only *add* per-tick work.

LockTimer is the only movement/speedrun-oriented plugin, but it reads
`pawn.AbsVelocity`/`pawn.Position` (entity reads on a 100ms `Timer.Every`), not
usercmd input — so it doesn't benefit either. The fast path (buttons + view angles +
movement, no protobuf) would only matter if a *new* input-driven feature were added
(e.g. frame-accurate run start on first movement/jump input, or a bhop input HUD).
Offered 2026-05-29; user declined — leave as is.
