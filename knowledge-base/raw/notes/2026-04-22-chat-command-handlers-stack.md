---
date: 2026-04-22
task: make Deathmatch's !hero queue the swap for next respawn instead of applying immediately
files: [Deathmatch/Deathmatch.cs, HeroSelect/HeroSelect.cs, gamemodes.json, deadworks/managed/PluginLoader.ChatCommands.cs, deadworks/managed/Commands/CommandRegistration.cs]
---

Chat-command handlers **stack** across plugins, not override. `PluginLoader.ChatCommands.cs:28-46`
iterates every registered handler for a given command name and runs them all, taking the
max `HookResult`. `Commands/CommandRegistration.cs` registers each `[Command]` attribute
into the same `_chatCommandRegistry`, so two plugins both declaring `[Command("hero")]`
will both fire on `!hero` — HeroSelect's immediate `caller.SelectHero(hero)` ran in
parallel with Deathmatch's queue-for-respawn logic, defeating the defer.

Consequence: to *replace* a shared-plugin command's semantics in a gamemode, remove the
shared plugin from that gamemode's entry in `gamemodes.json` — you cannot override or
suppress it from another plugin. Done here: dropped `"HeroSelect"` from `"deathmatch"` in
`gamemodes.json` so Deathmatch's local `!hero` (which queues into `_pendingHeroSwap` and
applies on `player_death` via `SelectHero`) is the only handler. TrooperInvasion still
loads HeroSelect with the immediate-swap semantics.

Also: `controller.SelectHero()` called server-side goes through `NativeInterop.SelectHero`
and **bypasses** Deathmatch's `OnClientConCommand` gate on `selecthero`/`citadel_hero_pick`
— that gate only guards CLIENT concommands. So HeroSelect's immediate swap was never
blocked by `_heroSwapUntil`; only the in-game hero-pick UI is.

Timing: applying the queued hero in `OnPlayerDeath` (not `OnPlayerRespawned`) is correct —
the engine's respawn flow reads SelectHero during the dead window, so the player spawns
AS the queued hero. Calling SelectHero post-respawn would either swap mid-fight or apply
only to the *next* respawn.
