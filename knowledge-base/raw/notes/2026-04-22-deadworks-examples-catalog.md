---
date: 2026-04-22
task: catalogue deadworks example plugins
files:
  - ../deadworks/examples/plugins/AutoRestartPlugin/AutoRestartPlugin.cs
  - ../deadworks/examples/plugins/ChatRelayPlugin/ChatRelayPlugin.cs
  - ../deadworks/examples/plugins/DumperPlugin/DumperPlugin.cs
  - ../deadworks/examples/plugins/ExampleTimerPlugin/ExampleTimerPlugin.cs
  - ../deadworks/examples/plugins/ItemRotationPlugin/ItemRotationPlugin.cs
  - ../deadworks/examples/plugins/ItemTestPlugin/ItemTestPlugin.cs
  - ../deadworks/examples/plugins/RollTheDicePlugin/RollTheDicePlugin.cs
  - ../deadworks/examples/plugins/ScourgePlugin/ScourgePlugin.cs
  - ../deadworks/examples/plugins/SetModelPlugin/SetModelPlugin.cs
  - ../deadworks/examples/plugins/TagPlugin/TagPlugin.cs
  - ../deadworks/examples/plugins/DeathmatchPlugin/
---

# Deadworks example plugins — what each one teaches

11 example plugins under `deadworks/examples/plugins/`. These are the
best API teaching material available and explore subsystems the three
plugins in this repo don't touch.

## AutoRestartPlugin (~110 LOC)

Scheduled map restart with countdown notifications. Demonstrates:
- **Timer sequences with pre-computed milestone list** (10m/5m/1m/10s→1s)
- `.CancelOnMapChange()` handle modifier
- **Hot-reload contract in practice** — `OnConfigReloaded` cancels and
  restarts the sequence
- `Chat.PrintToChatAll(message)` for broadcast
- `Server.MapName` + `Server.ExecuteCommand($"changelevel {map}")`

## ChatRelayPlugin (~55 LOC)

Rebroadcasts chat messages from slots 12+ so they appear on the HUD.
Demonstrates:
- `[NetMessageHandler]` outgoing hook on `CCitadelUserMsg_ChatMsg`
- Per-recipient message rewrite: set `PlayerSlot` to recipient's slot (makes
  the client think the message came from a visible portrait)
- **Re-entrance guard** — `_rebroadcasting` bool prevents hook loop when
  the re-sent messages pass through the same outgoing hook
- `CBaseEntity.FromIndex<CCitadelPlayerController>(senderSlot + 1)` and
  `.PlayerName` for name lookup

## DumperPlugin (~55 LOC)

Admin utility — dumps all ConVars and ConCommands to a JSON file.
Demonstrates:
- `[Command(..., ServerOnly = true, ConsoleOnly = true)]` — console-only,
  refuses player invocation
- Optional argument with default: `public void CmdCvarDump(string outputPath = "")`
- `Server.EnumerateConVars()` and `Server.EnumerateConCommands()` — the
  iteration APIs for introspection
- Default output path: `~/deadlock_dumps/cvardump_<timestamp>.json`

## ExampleTimerPlugin (~60 LOC)

Canonical showcase of every `ITimer` entry point:
- `Timer.Once(3.Seconds(), cb)`
- `Timer.Every(64.Ticks(), cb)` — tick-based!
- `Timer.Sequence(step => step.Run < 5 ? step.Wait(500.Milliseconds()) : step.Done())`
- `Timer.NextTick(cb)`
- `IHandle.IsFinished` probe pattern
- `handle.Cancel()` in `OnUnload` (noted as defensive — framework would
  cancel anyway)

## ItemRotationPlugin (~360 LOC)

Largest example. Game-mode that periodically rotates item sets between
players. Demonstrates:
- **Complex config with nested POCO** — `List<ItemSet>` where
  `ItemSet { Name, List<string> Items }`
- `[Command]` with typed args and `CommandException` for validation
- `Players.GetAll()` iteration for connected-only players
- `pawn.AddItem(name)` / `RemoveItem(name)` / `SellItem(name, fullRefund)`
- `CCitadelUserMsg_HudGameAnnouncement` broadcast for user feedback
- Per-slot state dictionaries (`_playerSetIndex`, `_activePlayers`) with
  explicit cleanup on disconnect/stop — no `EntityData<T>` here because
  state is keyed by slot (int) not entity

## ItemTestPlugin (~90 LOC)

Admin item manipulation commands. Demonstrates:
- **v0.4.5 `AddItem(enhanced: true)` parameter** for upgraded variants
- `SellItem(itemName, fullRefund)`, `RemoveItem(itemName)`
- `pawn.AbilityComponent.Abilities` enumeration
- **`params string[] commandParts` for rcon-style commands**:
  ```csharp
  [Command("rcon", SuppressChat = true)]
  public void CmdRcon(CCitadelPlayerController? caller, params string[] commandParts)
  ```
- **`caller.PrintToConsole(message)` — fixed in v0.4.5**. Previously
  silently no-op; now works.

## RollTheDicePlugin (~90 LOC)

Random effect roll on player command. Demonstrates:
- `Precache.AddResource("particles/upgrades/mystical_piano_hit.vpcf")` in
  `OnPrecacheResources()`
- `CParticleSystem` fluent builder:
  ```csharp
  CParticleSystem.Create("particles/...")
      .AtPosition(pawn.Position + Vector3.UnitZ * 100)
      .StartActive(true)
      .Spawn();
  ```
- `using var kv = new KeyValues3(); kv.SetFloat("duration", 3.0f);` +
  `pawn.AddModifier("modifier_citadel_knockdown", kv)`
- `pawn.EmitSound("Mystical.Piano.AOE.Warning")`
- `EntityData<IHandle?>` for per-pawn timer tracking
- **Nested timers:** outer `Timer.Once(5.Seconds(), () => particle.Destroy())`
  scheduled inside an inner `Timer.Once(1700.Milliseconds(), () => ...)` callback
- `pawn.ModifierProp.SetModifierState(EModifierState.UnlimitedAirJumps, true)`
- `pawn.AbilityComponent.ResourceStamina` direct field access
  (`LatchValue`, `CurrentValue`, `MaxValue`)

## ScourgePlugin (~75 LOC)

Discord-item triggered DoT. Demonstrates:
- `OnTakeDamage` hook with ability-source filter:
  `args.Info.Ability?.SubclassVData?.Name != "upgrade_discord"`
- `args.Entity.As<CCitadelPlayerPawn>()` — safe cast pattern
- **Re-application replaces prior timer:** cancels old `IHandle` stored in
  `EntityData<IHandle>` before starting new sequence
- Timer.Sequence with captured victim handle, checks `CBaseEntity.FromHandle(h)`
  + `IsAlive` each tick (handle stays stable across entity moves, raw
  pointer wouldn't)
- `pawn.Controller?.PlayerDataGlobal.HealthMax` — reading max health
  through controller side
- `ent.Hurt(damage, attacker: attacker)` — direct damage call with
  attribution

## SetModelPlugin (~35 LOC)

Minimal model swap example. Demonstrates:
- `Precache.AddResource(modelPath)` before use
- `pawn.SetModel(modelPath)` — cosmetic swap (hitbox unchanged)
- HUD announcement send-to-single-player

## TagPlugin (~450 LOC, longest)

Full tag/hide-and-seek game mode. Demonstrates (beyond the lifecycle
stuff):
- **Batch ConVar manipulation in `EnsureConVars()`** — `ConVar.Find("name")?.SetInt(val)`
  for `citadel_trooper_spawn_enabled`, `citadel_npc_spawn_enabled`,
  `citadel_start_players_on_zipline`, `citadel_allow_duplicate_heroes`,
  `citadel_voice_all_talk`, `citadel_player_starting_gold`,
  `citadel_player_spawn_time_max_respawn_time`, `citadel_active_lane`,
  `citadel_rapid_stamina_regen`. Useful ConVar reference.
- Per-team currency distribution: hiders earn 1 gold/sec via
  `pawn.ModifyCurrency(ECurrencyType.EGold, 1, ECurrencySource.ECheats)`
- Team assignment on `OnClientFullConnect`, counting existing team members
- `OnAbilityAttempt` global block for specific teams
- `controller.ChangeTeam(teamId)` to reassign
- **JSON config with `[JsonPropertyName]`** (though unnecessary given
  `PropertyNameCaseInsensitive = true` — author used it for clarity)
- Spawn point management keyed by map name (`Dictionary<string, List<SpawnPoint>>`)

## DeathmatchPlugin (in examples/)

A fork-divergent copy of the Deathmatch plugin. **Different from this repo's
`Deathmatch/` plugin** — likely predates or parallels this repo's version.
Investigation needed if we care to diff (not this scan's scope).

---

## Cross-cutting patterns (common idioms across examples)

1. **`private static readonly EntityData<T> _xxx = new();`** — per-entity
   state with auto-cleanup
2. **Cancel-then-rearm** in `OnConfigReloaded`: cancel old handles,
   call the same setup routine
3. **Caller reply helper** — small `Reply(caller, msg)` method that routes
   to `caller.PrintToConsole` if player, `Console.WriteLine` otherwise:
   ```csharp
   private static void Reply(CCitadelPlayerController? to, string message) {
       if (to != null) to.PrintToConsole(message);
       else Console.WriteLine(message);
   }
   ```
4. **Precache in `OnPrecacheResources`** — not in `OnLoad` (too early)
   and not in `OnStartupServer` (too late, after precache phase)
5. **Config validation clamps in-place** rather than throwing — a broken
   config becomes a corrected-in-memory config
6. **`_rebroadcasting`-style re-entrance guards** — any plugin that
   sends a message while hooking the same message type needs one

## Patterns NOT seen in examples

- No examples use `OnCheckTransmit` — plugin-side entity hiding is
  uncommon
- No examples use the Trace API — it's niche
- No examples use `GameEvents.AddListener` manual registration — all
  go through `[GameEventHandler]`
- No examples use source-side `RegisterManual` on NetMessageRegistry
