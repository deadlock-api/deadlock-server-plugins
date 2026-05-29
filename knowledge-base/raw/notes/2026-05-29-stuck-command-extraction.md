---
date: 2026-05-29
task: extract TrooperInvasion's !stuck/!suicide into a standalone reusable plugin
files: [StuckCommand/StuckCommand.cs, StuckCommand/StuckCommand.csproj, TrooperInvasion/TrooperInvasion.Commands.cs, gamemodes.json, .github/workflows/build-plugins.yml, .github/workflows/docker-gamemodes.yml, Deathmatch/Deathmatch.cs]
---

`!stuck` / `!suicide` (kill own hero → respawn out of a stuck spot) was a
`[Command]` on `TrooperInvasionPlugin`. Extracted verbatim into a new standalone
plugin `StuckCommand` (`StuckCommandPlugin : DeadworksPluginBase`, csproj cloned
from HealOnSpawn's triple-mode pattern, message prefix `[Stuck]`). Removed from
TrooperInvasion; the TI help line is kept because StuckCommand is composed into
that gamemode. Wired into `gamemodes.json` for `normal`, `lock-timer`, and
`trooper-invasion`. Added paths-filter entries to both CI workflows.

**Gotcha — NOT added to deathmatch.** `Deathmatch.cs:852-870` has its own
`[Command("stuck"/"suicide")]` that first clears spawn protection
(`_invulnerableUntil.Remove(...)` + `mp.SetModifierState(Invulnerable/BulletInvulnerable, false)`)
before `pawn.Hurt(999999f)`, otherwise the suicide damage is absorbed. Loading the
generic StuckCommand alongside it would be a duplicate command registration AND the
generic version would be buggy in DM (no spawn-protection clear). So deathmatch keeps
its specialized variant; any gamemode that tracks spawn-protection invulnerability
needs its own stuck handler, not this plugin. No other plugin (StatusPoker, Hostname,
Feedback, LockTimer) registers stuck/suicide, so the other three modes are conflict-free.

Registration points for a new plugin in this repo: (1) plugin folder with `.csproj`,
(2) `gamemodes.json` entry per gamemode, (3) paths-filter stanza in both
`.github/workflows/build-plugins.yml` and `docker-gamemodes.yml`. The Makefile dev
loop and docker-gamemodes matrix both read `gamemodes.json` dynamically — no
hardcoded plugin list to update.
