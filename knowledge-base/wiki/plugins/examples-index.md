---
title: Deadworks Example Plugins — Index
type: plugin
sources:
  - raw/notes/2026-04-22-deadworks-examples-catalog.md
  - ../deadworks/examples/plugins/
related:
  - "[[plugin-api-surface]]"
  - "[[command-attribute]]"
  - "[[timer-api]]"
  - "[[events-surface]]"
  - "[[schema-accessors]]"
  - "[[netmessages-api]]"
  - "[[plugin-config]]"
  - "[[deathmatch]]"
  - "[[lock-timer]]"
  - "[[status-poker]]"
created: 2026-04-22
updated: 2026-04-22
confidence: high
---

# Deadworks Example Plugins — Index

11 example plugins under `../deadworks/examples/plugins/`. These are the
best teaching material for the plugin-facing API and cover subsystems
this repo's three plugins ([[deathmatch]], [[lock-timer]],
[[status-poker]]) don't touch.

If you're looking for a pattern — "how do I implement X?" — start here.

## Quick-find table

| Plugin | LOC | Pick when you need... |
|--------|-----|-----------------------|
| [AutoRestartPlugin](#autorestartplugin) | ~110 | Timer sequences, countdown milestones, `CancelOnMapChange`, hot-reload |
| [ChatRelayPlugin](#chatrelayplugin) | ~55 | Hooking outgoing net messages, re-entrance guard |
| [DumperPlugin](#dumperplugin) | ~55 | Admin-only commands, `EnumerateConVars/ConCommands` |
| [ExampleTimerPlugin](#exampletimerplugin) | ~60 | Canonical tour of every `ITimer` method |
| [ItemRotationPlugin](#itemrotationplugin) | ~360 | Complex nested config, game-mode state machine, `AddItem`/`RemoveItem` |
| [ItemTestPlugin](#itemtestplugin) | ~90 | v0.4.5 `enhanced` item API, `params string[]` commands, `PrintToConsole` |
| [RollTheDicePlugin](#rollthediceplugin) | ~90 | `CParticleSystem` builder, `KeyValues3` + `AddModifier`, nested timers, per-pawn state |
| [ScourgePlugin](#scourgeplugin) | ~75 | `OnTakeDamage` with ability filter, `EntityData<IHandle>`, DOT sequence |
| [SetModelPlugin](#setmodelplugin) | ~35 | Minimal precache + `pawn.SetModel` |
| [TagPlugin](#tagplugin) | ~450 | Full game-mode, batch ConVar setup, team assignment, `OnAbilityAttempt` masking |
| DeathmatchPlugin (examples/) | — | Fork-divergent; see note at bottom |

---

## AutoRestartPlugin

Scheduled map restart with countdown notifications.

Demonstrates:
- **Timer sequences with pre-computed milestone list** (10m / 5m / 1m / 10s→1s)
- `.CancelOnMapChange()` handle modifier
- **Hot-reload contract** — `OnConfigReloaded` cancels and restarts the sequence
- `Chat.PrintToChatAll(message)`
- `Server.MapName` + `Server.ExecuteCommand($"changelevel {map}")`

See: [[timer-api]], [[plugin-config]].

## ChatRelayPlugin

Rebroadcasts chat from slots 12+ so they're visible on the 12-portrait HUD.

Demonstrates:
- `[NetMessageHandler]` outgoing hook on `CCitadelUserMsg_ChatMsg`
- Per-recipient message rewrite: set `PlayerSlot` to recipient to make it
  appear to come from a visible portrait
- **`_rebroadcasting` re-entrance guard** (critical — the re-sent messages
  pass through the same hook)
- `CBaseEntity.FromIndex<CCitadelPlayerController>(slot + 1)` and
  `.PlayerName` lookup

See: [[netmessages-api]].

## DumperPlugin

Admin utility that dumps ConVars and ConCommands to JSON.

Demonstrates:
- `[Command(..., ServerOnly = true, ConsoleOnly = true)]` — console-only,
  refuses player invocation
- Optional argument with default: `public void CmdCvarDump(string outputPath = "")`
- `Server.EnumerateConVars()` and `Server.EnumerateConCommands()`
- Default output to `~/deadlock_dumps/cvardump_<timestamp>.json`

See: [[command-attribute]].

## ExampleTimerPlugin

Canonical showcase of every `ITimer` entry point in ~60 LOC:

```csharp
Timer.Once(3.Seconds(), cb);
Timer.Every(64.Ticks(), cb);
Timer.Sequence(step => step.Run < 5
    ? step.Wait(500.Milliseconds())
    : step.Done());
Timer.NextTick(cb);
var probe = Timer.Once(1.Seconds(), cb);
Console.WriteLine(probe.IsFinished);
handle.Cancel();
```

See: [[timer-api]].

## ItemRotationPlugin

Largest example. Game-mode that rotates item sets between players.

Demonstrates:
- **Complex nested config** — `List<ItemSet>` where
  `ItemSet { Name, List<string> Items }`
- `[Command]` with typed args and `CommandException` for validation
- `Players.GetAll()` iteration
- `pawn.AddItem(name)` / `RemoveItem(name)` / `SellItem(name, fullRefund)`
- `CCitadelUserMsg_HudGameAnnouncement` broadcast
- **Per-slot state dictionaries** (`_playerSetIndex`, `_activePlayers`) with
  explicit cleanup — no `EntityData<T>` because state is keyed by slot
  (int) not entity

See: [[plugin-config]], [[command-attribute]].

## ItemTestPlugin

Admin item-manipulation commands.

Demonstrates:
- **v0.4.5 `AddItem(enhanced: true)`** for upgraded variants
- `SellItem(itemName, fullRefund)`, `RemoveItem(itemName)`
- `pawn.AbilityComponent.Abilities` enumeration
- **`params string[] commandParts` for rcon-style commands**:
  ```csharp
  [Command("rcon", SuppressChat = true)]
  public void CmdRcon(CCitadelPlayerController? caller, params string[] commandParts)
  ```
- **`caller.PrintToConsole(message)`** — fixed in v0.4.5 (previously
  silently no-op'd)

See: [[command-attribute]], [[deadworks-0.4.5-release]].

## RollTheDicePlugin

Random effect roll on a command.

Demonstrates:
- `Precache.AddResource("particles/upgrades/mystical_piano_hit.vpcf")` in
  `OnPrecacheResources()`
- **`CParticleSystem` fluent builder**:
  ```csharp
  CParticleSystem.Create("particles/...")
      .AtPosition(pawn.Position + Vector3.UnitZ * 100)
      .StartActive(true)
      .Spawn();
  ```
- **`KeyValues3` + `AddModifier`**:
  ```csharp
  using var kv = new KeyValues3();
  kv.SetFloat("duration", 3.0f);
  pawn.AddModifier("modifier_citadel_knockdown", kv);
  ```
- `pawn.EmitSound("Mystical.Piano.AOE.Warning")`
- **`EntityData<IHandle?>`** for per-pawn timer tracking
- **Nested timers:** `Timer.Once(5.Seconds(), () => particle.Destroy())`
  scheduled inside an outer `Timer.Once(1700.Milliseconds(), ...)`
- `pawn.ModifierProp.SetModifierState(EModifierState.UnlimitedAirJumps, true)`
- `pawn.AbilityComponent.ResourceStamina` direct field access
  (`LatchValue`, `CurrentValue`, `MaxValue`)

See: [[schema-accessors]], [[timer-api]].

## ScourgePlugin

Discord-item triggered damage-over-time.

Demonstrates:
- `OnTakeDamage` hook with ability-source filter:
  `args.Info.Ability?.SubclassVData?.Name != "upgrade_discord"`
- **`args.Entity.As<CCitadelPlayerPawn>()`** — safe cast + null check
- **Re-application replaces prior timer**: cancel old `IHandle` stored in
  `EntityData<IHandle>` before starting a new sequence
- `Timer.Sequence` with captured victim **handle** (not pointer) —
  stable across entity moves
- Per-tick `CBaseEntity.FromHandle(h)` + `IsAlive` guard
- `ent.Hurt(damage, attacker: attacker)` — direct damage call with
  attribution

See: [[events-surface]], [[schema-accessors]].

## SetModelPlugin

Minimal model-swap example (~35 LOC).

Demonstrates:
- `Precache.AddResource(modelPath)` before use
- `pawn.SetModel(modelPath)` — cosmetic only, hitbox unchanged
- HUD announcement send-to-single-player via `RecipientFilter.Single`

## TagPlugin

Full tag / hide-and-seek game mode (~450 LOC, longest example).

Demonstrates (beyond the lifecycle basics):
- **Batch ConVar manipulation** in `EnsureConVars()`:
  ```csharp
  ConVar.Find("citadel_trooper_spawn_enabled")?.SetInt(0);
  ConVar.Find("citadel_npc_spawn_enabled")?.SetInt(0);
  ConVar.Find("citadel_start_players_on_zipline")?.SetInt(0);
  ConVar.Find("citadel_allow_duplicate_heroes")?.SetInt(1);
  ConVar.Find("citadel_voice_all_talk")?.SetInt(1);
  ConVar.Find("citadel_player_starting_gold")?.SetInt(0);
  ConVar.Find("citadel_player_spawn_time_max_respawn_time")?.SetInt(3);
  ConVar.Find("citadel_active_lane")?.SetInt(255);
  ConVar.Find("citadel_rapid_stamina_regen")?.SetInt(1);
  ```
  Useful ConVar reference for game-mode plugins.
- **Per-team currency**: `pawn.ModifyCurrency(ECurrencyType.EGold, 1, ECurrencySource.ECheats)`
- Team assignment on `OnClientFullConnect` by counting existing team members
- **`OnAbilityAttempt` global block** (e.g., hiders can't use abilities)
- `controller.ChangeTeam(teamId)` to reassign
- JSON config with `[JsonPropertyName]` (unnecessary given
  `PropertyNameCaseInsensitive = true` — author used it for clarity)
- Spawn point management keyed by map name
  (`Dictionary<string, List<SpawnPoint>>`)

See: [[events-surface]] (OnAbilityAttempt mask-based blocking),
[[plugin-config]].

## `examples/plugins/DeathmatchPlugin/` — divergent copy

A fork-divergent copy of Deathmatch exists under `examples/plugins/`,
distinct from this repo's [[deathmatch]] plugin. Likely predates or
parallels our version. Not diffed here — if relevant, compare
`../deadworks/examples/plugins/DeathmatchPlugin/` vs
`./Deathmatch/Deathmatch.cs`.

---

## Patterns NOT demonstrated by any example

(Gap list for anyone extending these examples)

- `OnCheckTransmit` — per-player entity visibility editing
- `Trace` — VPhys2 raycast / shape cast
- `GameEvents.AddListener` manual registration (all examples use
  `[GameEventHandler]`)
- `NetMessageRegistry.RegisterManual<T>` for custom/overridden message IDs
