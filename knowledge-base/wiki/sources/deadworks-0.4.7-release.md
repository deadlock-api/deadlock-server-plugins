---
title: "Source: Deadworks v0.4.7 release notes"
type: source-summary
sources:
  - raw/articles/deadworks-0.4.7-release.md
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CBaseEntity.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CBasePlayerController.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CBasePlayerPawn.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CCitadelPlayerController.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CCitadelPlayerPawn.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CPlayer_ObserverServices.cs
  - ../deadworks/managed/DeadworksManaged.Api/Enums/ObserverMode_t.cs
  - ../deadworks/managed/DeadworksManaged.Api/IDeadworksPlugin.cs
  - ../deadworks/managed/EntryPoint.cs
  - ../deadworks/managed/PluginLoader.cs
  - ../deadworks/src/Core/NativeCallbacks.hpp
  - ../deadworks/config/deadworks_mem.jsonc
related:
  - "[[plugin-api-surface]]"
  - "[[events-surface]]"
  - "[[schema-accessors]]"
  - "[[deadworks-runtime]]"
  - "[[observer-api]]"
  - "[[deadworks-0.4.6-release]]"
created: 2026-05-02
updated: 2026-05-02
confidence: high
---

# Source: Deadworks v0.4.7 Release Notes

Commit window: **12 commits** between tags `v0.4.6` and `v0.4.7`,
dates 2026-04-25 through 2026-05-01.

```
2ef15c5  don't count bots when counting players
d0bf986  enforce signature presence and remove unused hook
073f443  remove unnecessary logging in RemoveAbility
9d876bd  comment out more logs
76bd1e4  add CBaseEntity::SetScale
05f2ccd  add CBaseEntity.ModelName
03f7f87  fix launcher detecting old install dirs
a6cd98e  add hero initialization callback and SwapOrReset helper
0cfdf44  launcher version bump
5c9a1e6  expose access to ObserverServices
300f0fa  add missing files (InitializeHeroOnPawn.cpp/.hpp)
```

---

## New API additions

### `CBaseEntity.SetScale(float scale)` — `76bd1e4`

Vtable dispatch at offset 246. No new memory signature required.
`1.0` = default scale. Works on any entity.

### `CBaseEntity.ModelName` (read-only property) — `05f2ccd`

Returns the current model path string (e.g.
`"models/heroes_wip/werewolf/werewolf.vmdl"`) or `""` if unset.
New signature in `deadworks_mem.jsonc`: `"CBaseModelEntity::GetModelName"`.
Complement to the existing `SetModel` method.

### `IDeadworksPlugin.OnPawnHeroInitialized(CCitadelPlayerPawn)` — `a6cd98e`, `300f0fa`

**24th hook** on `IDeadworksPlugin` (was 23 before this release). Fires after
`CCitadelPlayerPawn::InitializeHeroOnPawn` returns server-side, meaning after
hero abilities and modifiers are (re)populated. Trigger paths:
- Initial pawn spawn
- `CCitadelPlayerController.SelectHero` (async hero swap)
- `CCitadelPlayerPawn.ResetHero` (sync same-hero reset)
- `resethero` console command

New signature: `"CCitadelPlayerPawn::InitializeHeroOnPawn"` in `deadworks_mem.jsonc`.

**Related helpers added on `CCitadelPlayerPawn`:**

- `SwapOrReset(Heroes hero, Action? onReady = null)` — calls `SelectHero` or
  `ResetHero` depending on current hero; `onReady` fires once abilities are ready
  via `OnceHeroInitialized`.
- `OnceHeroInitialized(Action action)` — one-shot continuation; runs inside the
  next `InitializeHeroOnPawn` return path. Multiple registrations queued in order.
  Auto-cleaned in `PluginLoader.DispatchEntityDeleted` when the pawn is destroyed.

**`CBasePlayerController.Pawn` property** — new `m_hPawn` schema accessor;
returns `CBasePlayerPawn?` (null if handle is `0xFFFFFFFF`).

### Observer/Spectator API — `5c9a1e6`

Full spectator control surface. See [[observer-api]] for the detailed page.

**New class `CPlayer_ObserverServices`:**
- `ObserverMode` (read), `ObserverLastMode` (read/write)
- `ObserverTarget` (read), `IsValidObserverTarget(entity)`
- `SetObserverMode(ObserverMode_t)`, `SetObserverTarget(entity) → bool`

**`ObserverMode_t` enum:** `None=0`, `Fixed=1`, `InEye=2`, `Chase=3`, `Roaming=4`

**`CBasePlayerPawn` additions:** `ObserverServices`, `ObserverMode`, `ObserverTarget`,
`SetObserverMode`, `SetObserverTarget`, `IsValidObserverTarget` (all pass-throughs).

**`CCitadelPlayerController.MakeObserver()`:** removes hero pawn, spawns observer pawn
via native `SpawnObserverPawn`, attaches it.

New signatures in `deadworks_mem.jsonc`:
- `"CCitadelPlayerController::SpawnObserverPawn"`
- `"CPlayer_ObserverServices::SetObserverTarget"` (vtable idx 25)
- `"CPlayer_ObserverServices::SetObserverMode"` (vtable idx 28)

---

## Bug fixes

| Commit | Fix |
|--------|-----|
| `2ef15c5` | `ServerBrowser` now skips `controller.IsBot` — bots no longer inflate server browser player count |
| `d0bf986` | A2S patch signature changed from optional → required (`.value()`); missing sig aborts boot instead of silently skipping the patch. Also removes an unused nil-detour inline hook. |
| `073f443`, `9d876bd` | `RemoveAbility` debug log noise removed from `NativeAbility.cpp` |
| `03f7f87` | Launcher: fix old install directory detection (Tauri app — not plugin-author facing) |
| `0cfdf44` | Launcher version bump |

---

## Deprecations

None. No existing API removed or changed.

---

## Impact on `deadlock-server-plugins`

**No breaking changes.** All four plugins compile and run without modification.

**Opportunities:**
- TrooperInvasion uses a `Timer.Once(1.Seconds())` delay after `SelectHero` to wait
  for abilities to be ready. This can be replaced with
  `pawn.SwapOrReset(hero, onReady: () => SetupAbilities(pawn))`.
- `CBaseEntity.ModelName` / `SetScale` are additive; no current plugin uses them.
- Observer API is additive; useful for a future spectator placement feature.

---

## Key findings / surprises

1. **`OnPawnHeroInitialized` fires for *every* hero init path** — not just
   player-triggered swaps. It will fire on the initial `OnClientFullConnect` pawn
   spawn too. Plugins that subscribe must guard against calling setup code twice
   (or rely on the one-shot `OnceHeroInitialized` which auto-dequeues).
2. **`OnceHeroInitialized` uses raw pawn `Handle` (not `EntityHandle`).** The pawn
   pointer is stable across hero swaps (only its contents change), so this is
   intentional and safe. The stale-handle cleanup in `OnEntityDeleted` uses the same
   `Handle` as the key.
3. **`IsValidObserverTarget` has a hardcoded team-3 exclusion** (return false if
   `target.TeamNum == 3`). In TrooperInvasion where troopers are team 3, this means
   players can't spectate troopers with the default observer target validation.
4. **`CBasePlayerController::SetPawn` signature updated** (minor pattern tweak in
   `deadworks_mem.jsonc`) — if running an older `deadworks_mem.jsonc`, this may break
   on startup. No managed API change, just the sig update.
