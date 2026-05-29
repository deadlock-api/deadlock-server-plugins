---
title: StuckCommand plugin
type: plugin
sources:
  - ../StuckCommand/StuckCommand.cs
  - ../StuckCommand/StuckCommand.csproj
  - knowledge-base/raw/notes/2026-05-29-stuck-command-extraction.md
related:
  - "[[command-attribute]]"
  - "[[trooper-invasion]]"
  - "[[deathmatch]]"
  - "[[plugin-build-pipeline]]"
created: 2026-05-29
updated: 2026-05-29
confidence: high
---

# StuckCommand plugin

A minimal standalone plugin providing `!stuck` / `!suicide` — kill your own hero
to respawn out of a stuck spot. No config, no state, no hooks beyond the command.
Extracted 2026-05-29 from [[trooper-invasion|TrooperInvasion]] (raw note
`2026-05-29-stuck-command-extraction.md`) so it can be composed into any gamemode
rather than copied per-mode.

## Behaviour

```csharp
[Command("stuck", Description = "Kill yourself to respawn")]
[Command("suicide", Description = "Kill yourself to respawn")]
public void CmdStuck(CCitadelPlayerController caller)
{
    var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
    if (pawn == null || !pawn.IsAlive)
        throw new CommandException("[Stuck] Not alive.");
    pawn.Hurt(999_999f);
}
```

One method, two command aliases via stacked `[Command]` attributes (see
[[command-attribute]]). `pawn.Hurt(999999f)` applies guaranteed-lethal self-damage.

## Composition — which gamemodes load it

In `gamemodes.json`: **normal**, **lock-timer**, **trooper-invasion**. csproj is
the standard triple-mode reference pattern (DeadlockDir / sibling ProjectReference
/ Docker fallback), cloned from HealOnSpawn.

> **Not loaded in deathmatch — deliberate.** [[deathmatch|Deathmatch]] keeps its
> own `!stuck`/`!suicide` (`Deathmatch.cs:852`) that first clears spawn protection
> (`_invulnerableUntil.Remove(...)` + clearing the `Invulnerable` /
> `BulletInvulnerable` modifier bits) before `Hurt`, otherwise the self-damage is
> absorbed. Loading this generic plugin there would both duplicate the command
> registration and behave incorrectly (suicide absorbed). **Any gamemode that
> tracks spawn-protection invulnerability needs its own stuck handler, not this
> plugin.** The other three modes have no spawn protection and no competing
> registration, so they're conflict-free.

## Registration checklist (reference for new plugins)

1. Plugin folder with `.csproj` (assembly + root namespace = folder name).
2. `gamemodes.json` entry per gamemode that should load it.
3. paths-filter stanza in **both** `.github/workflows/build-plugins.yml` and
   `docker-gamemodes.yml` (see [[plugin-build-pipeline]]).

The `make dev` loop and the docker-gamemodes matrix both read `gamemodes.json`
dynamically, so there's no hardcoded plugin list to maintain beyond the above.
