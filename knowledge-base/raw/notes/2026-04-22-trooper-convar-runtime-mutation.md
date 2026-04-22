---
date: 2026-04-22
task: Diagnose native crashes when mutating trooper ConVars mid-game
files: [TrooperInvasion/TrooperInvasion.cs]
---

Calling `ConVar.Find("citadel_trooper_*")?.SetInt/SetFloat(v)` **from a chat-command
handler or Timer callback** (i.e. mid-frame, after the trooper subsystem is
live) produced native crashes with zero managed stack on a subset of trooper
convars — specifically the spawn-interval family
(`citadel_trooper_spawn_interval_early/late/very_late`),
`citadel_trooper_max_per_lane`, and re-toggling `citadel_trooper_spawn_enabled`
from `0` → `1`. The C# `try/catch` around each call caught nothing because the
crash was below the managed boundary.

**Fix**: route every runtime mutation through `Server.ExecuteCommand("name
value")` instead. That path goes through the engine's own `CCVar` dispatch —
the same surface used by the boot-time `hostname` write and by
`Deathmatch.cs`'s convar writes — and has been stable across every wave since
the switch. The TrooperInvasion plugin now uses `ConVar.Find().Set*` only
inside `OnStartupServer` (pre-trooper-subsystem-init, where it works) and
`Server.ExecuteCommand` everywhere else (`RunWave`, `HandleVictory`,
`HandleDefeat`, `!stopwaves`).

Generalises beyond TrooperInvasion: **any plugin that needs to mutate a
stateful engine convar at runtime should prefer `Server.ExecuteCommand` over
direct schema/`ConVar.Find().Set*`**, especially when the target convar's
write triggers engine-side reinitialisation of pools, tables, or interval
schedulers.

Open question: is this a general rule for *all* citadel_* convars, or only
those that re-seed internal data structures on write? The `hostname` /
`citadel_allow_*` / scalar-tuning convars appear safe either way. Worth
capturing on [[source-2-engine]] if it holds up across more plugins.
