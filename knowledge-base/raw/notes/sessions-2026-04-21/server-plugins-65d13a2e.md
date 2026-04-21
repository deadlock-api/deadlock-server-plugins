---
date: 2026-04-21
task: session extract — server-plugins 65d13a2e
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-server-plugins/65d13a2e-344d-4a87-9384-d51a3be4b473.jsonl]
---

## Source 2 engine

- Game event `player_hero_changed` is declared in `deadworks/game_exported/game.gameevents:414` (also `client_player_hero_changed:561`). No generated `.g.cs` event class exists under `DeadworksManaged.Api/Events/`; `PlayerHeroChangedEvent` is hand-written.
- `GameEvent.GetEHandle()` decodes eHandle fields: handle `0xFFFFFFFF` is the null sentinel, resolved via `NativeInterop.GetEntityFromHandle` (`Events/GameEvent.cs:108-114`).
- `m_hGroundEntity` uses `0xFFFFFFFF` for "no ground" — `IsOnGround` is implemented as a not-equal check, not a non-zero check (`Entities/CBaseEntity.cs:342`).
- `CBaseEntity.SubclassVData` is read from `m_nSubclassID + 4` (the `CUtlStringToken` is 4 bytes; VData pointer sits immediately after) — `Entities/CBaseEntity.cs:356-362`.

## Deadlock game systems

- `Heroes` enum values mostly match Valve hero IDs but the enum *name* is a codename that differs from the player-facing display (e.g. `Heroes.Inferno=1` → "Infernus", `Orion=17` → "Grey Talon", `Hornet=3` → "Vindicta", `Krill=18` → "Mo & Krill"). `HeroTypeExtensions._displayNames` in `HeroTypeExtensions.cs:23-84` is the authoritative mapping; enum->`hero_*` string is done by camelCase regex split (`HeroTypeExtensions.cs:13-14`), display name needs the separate dict.
- `CitadelHeroData.AvailableInGame` is a composite boolean: `PlayerSelectable && !Disabled && !InDevelopment && !NeedsTesting && !PrereleaseOnly && !LimitedTesting` (`HeroData.cs:93`). Filter matchable heroes through this to exclude dev/disabled/prerelease.
- `controller.SelectHero(hero)` fires `player_hero_changed` which in Deathmatch triggers `ApplySpawnRitual` (max sig abilities, spawn protection, heal, gold, unlock flex) — so caller doesn't need to re-apply; just call `SelectHero` (`DeathmatchPlugin.cs:357-362`).
- Spawn-protection `OnTakeDamage` zeroes damage for entities in `_invulnerableUntil`, including suicide. To force suicide via `!stuck`, plugin must first `_invulnerableUntil.Remove(idx)` and clear `EModifierState.Invulnerable` + `BulletInvulnerable` before `pawn.Hurt(999_999f)` (otherwise self-kill silently absorbed) — `DeathmatchPlugin.cs:319-329` vs stuck handler.
- `CBaseEntity.Hurt` auto-sets `TakeDamageFlags.AllowSuicide = 0x40000` so same-entity attacker/victim kills work without extra setup (`Entities/CBaseEntity.cs:368`, `Enums/Combat.cs:45`).
- `Chat.PrintToChat(controller, …)` maps controller → slot via `EntityIndex - 1` (`Chat.cs:19-22`); `PrintToChatAll` iterates `Players.GetAll()` with that same mapping.

## Deadworks runtime

- `[ChatCommand(...)]` attribute string *includes* the `!` prefix by convention (see `Events/ChatCommandAttribute.cs:7` doc example `"!mycommand"` and Deathmatch uses `"!help"`/`"!hero"`/`"!stuck"`). LockTimer inconsistently registers bare names (`[ChatCommand("zones")]`, `"reset"`, `"pos"`, `"speed"`) at `LockTimer/LockTimerPlugin.cs:216-252`. This divergence was observed but not reconciled — LockTimer's plan docs (`LockTimer/docs/plan.md:1616`) also use the `!`-prefixed form, so bare-name registration there is likely broken/latent.
- `ChatCommandContext` exposes `ctx.Message.SenderSlot` (int), `ctx.Controller` (nullable — goes through `NativeInterop.GetPlayerController`), and whitespace-split `Args[]` (`Events/ChatCommandContext.cs:1-24`).
- `DeadworksPluginBase` provides a virtual `OnChatMessage` distinct from the `[ChatCommand]` attribute path — the attribute path is scanned/dispatched by the plugin loader reflectively (base class at `DeadworksPluginBase.cs:20`).
- `PlayerHeroChangedEvent.Userid` is a pawn-like handle that needs `.As<CCitadelPlayerPawn>()`; `.Controller` on the resulting pawn is the path to the controller's EntityIndex (used to clear `_heroSwapUntil` cache on successful swap, `DeathmatchPlugin.cs:358-362`).
- Bug pattern fixed in session: Deathmatch kept `_heroSwapUntil[ctrl.EntityIndex]` entries until `OnClientDisconnect`. Cleanup now happens on successful `player_hero_changed` too so the dict doesn't linger between reconnects with reused entity indices.

## Plugin build & deployment

- `DeathmatchPlugin.csproj:12-13` resolves DLL references via `DeadlockDir` (default `$(DEADLOCK_GAME_DIR)` env var) → `DeadlockBin=$(DeadlockDir)\game\bin\win64`. References `DeadworksManaged.Api.dll` and `Google.Protobuf.dll` from `$(DeadlockBin)\managed\` (`Private=false`, `ExcludeAssets=runtime`).
- `AfterTargets="Build"` copies `*.dll`/`*.pdb` to `$(DeadlockBin)\managed\plugins` — so local builds deploy straight into the game dir (`DeathmatchPlugin.csproj:28-37`).
- Building without a real game dir works with a synthetic stub: create `/tmp/dm-stub/managed/` containing both `DeadworksManaged.Api.dll` (from `deadworks/managed/DeadworksManaged.Api/bin/Debug/net10.0/`) and `Google.Protobuf.dll`, then `dotnet build -p:DeadlockBin=/tmp/dm-stub`. **Protobuf is not shipped alongside `DeadworksManaged.Api.dll`** in that bin folder — it had to be copied from a separate nuget cache (`deadlock-deathmatch/C:/nuget/google.protobuf/3.29.3/lib/net5.0/Google.Protobuf.dll`). Missing Protobuf produces a cascade of `CS0246` errors including `PluginConfigAttribute`, `GameEventHandler`, `ChatCommand`, `Heroes`, `CCitadelPlayerPawn` — misleading because the real cause is the Protobuf reference failing to resolve, not the Api DLL.
- `DeadworksManaged.Api` targets `net10.0` (per `DeadworksManaged.Api.csproj`) and is published to `bin/Debug/net10.0/`.
