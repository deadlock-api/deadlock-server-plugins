# Deadworks v0.4.7 Release Notes

Source: verified against upstream `../deadworks/` git log between tags `v0.4.6` and `v0.4.7`.
12 commits: `2ef15c5`, `d0bf986`, `073f443`, `9d876bd`, `76bd1e4`, `05f2ccd`, `03f7f87`,
`a6cd98e`, `0cfdf44`, `5c9a1e6`, `300f0fa` (+ `e9fa...` missing file supplement).

Commit dates: 2026-04-25 → 2026-05-01.

---

## New API — `CBaseEntity.SetScale`

**Commit:** `76bd1e4`

```csharp
entity.SetScale(float scale);   // 1.0 = default
```

Native vtable wrapper: `offsets::kVtblSetScale = 246` (C++ side). No new signature in
`deadworks_mem.jsonc` — pure vtable dispatch. Works on any `CBaseEntity`.

---

## New API — `CBaseEntity.ModelName`

**Commit:** `05f2ccd`

```csharp
string path = entity.ModelName;
// e.g. "models/heroes_wip/werewolf/werewolf.vmdl" or "" if unset
```

New signature in `deadworks_mem.jsonc`: `"CBaseModelEntity::GetModelName"`. Returns UTF-8
pointer; managed side marshals via `Marshal.PtrToStringUTF8` (returns `""` on null).
Read-only; to change use the existing `SetModel`.

---

## New hook — `OnPawnHeroInitialized`

**Commits:** `a6cd98e` + `300f0fa` (implementation files)

New `IDeadworksPlugin` hook (24th hook — was 23):

```csharp
void OnPawnHeroInitialized(CCitadelPlayerPawn pawn) { }
```

Fires after `CCitadelPlayerPawn::InitializeHeroOnPawn` returns server-side — i.e. after
hero abilities and modifiers have been (re)populated. Triggered by: initial spawn,
`SelectHero`, `ResetHero`, and the `resethero` console command.

New signature in `deadworks_mem.jsonc`: `"CCitadelPlayerPawn::InitializeHeroOnPawn"`.

### SwapOrReset helper

```csharp
pawn.SwapOrReset(Heroes hero, Action? onReady = null);
```

High-level helper: calls `SelectHero` if currently on a different hero (async), or
`ResetHero` if already on that hero (sync). If `onReady` is supplied it is queued via
`OnceHeroInitialized` and fires once ability slots are ready.

### OnceHeroInitialized

```csharp
pawn.OnceHeroInitialized(Action action);
```

One-shot continuation: runs the next time `InitializeHeroOnPawn` fires on this pawn.
Multiple registrations queued in order. Auto-cleaned when the pawn is deleted
(via `PluginLoader.DispatchEntityDeleted` → `CCitadelPlayerPawn.OnEntityDeleted`).
Keyed by raw pawn pointer (Handle), not EntityHandle — safe because pawn address is
stable across hero swaps.

### CBasePlayerController.Pawn

```csharp
CBasePlayerPawn? pawn = controller.Pawn;
```

New schema accessor for `m_hPawn` (handle-based, handle `0xFFFFFFFF` → null).

---

## New API — Observer / Spectator system

**Commit:** `5c9a1e6`

Full spectator control surface exposed for the first time.

### ObserverMode_t enum

```csharp
public enum ObserverMode_t : uint {
    None = 0, Fixed = 1, InEye = 2, Chase = 3, Roaming = 4,
}
```

Stored in `CPlayer_ObserverServices.m_iObserverMode` (networked byte).

### CPlayer_ObserverServices

New class (schema accessor wrapper):

| Member | Description |
|--------|-------------|
| `ObserverMode` | Current observer mode (read) |
| `ObserverLastMode` | Previous non-zero mode (read/write schema) |
| `ObserverTarget` | Currently observed entity, or null |
| `IsValidObserverTarget(entity)` | Returns false if target is dead, frozen, or on team 3 |
| `SetObserverMode(mode)` | vtable call — `CPlayer_ObserverServices::SetObserverMode` (idx 28) |
| `SetObserverTarget(entity)` | vtable call — `CPlayer_ObserverServices::SetObserverTarget` (idx 25), returns bool |

### CBasePlayerPawn additions

Convenience pass-throughs delegating to `ObserverServices`:

```csharp
CPlayer_ObserverServices? pawn.ObserverServices   // m_pObserverServices schema accessor
ObserverMode_t            pawn.ObserverMode
CBaseEntity?              pawn.ObserverTarget
void                      pawn.SetObserverMode(mode)
bool                      pawn.SetObserverTarget(entity)
bool                      pawn.IsValidObserverTarget(entity)
```

### CCitadelPlayerController.MakeObserver

```csharp
controller.MakeObserver();
```

Removes the hero pawn, spawns an observer pawn (native `SpawnObserverPawn`), and attaches
it. This is the correct path to put a connected player into pure spectator mode.

New signature: `"CCitadelPlayerController::SpawnObserverPawn"`.
Updated signature: `"CBasePlayerController::SetPawn"` (minor pattern adjustment).

---

## Bug fixes

### ServerBrowser — bots excluded from player count

**Commit:** `2ef15c5`

`ServerBrowser` now skips `controller.IsBot` entries when enumerating players for the
A2S/server browser player list. Previously bots inflated the visible player count.

### A2S patch — signature now required

**Commit:** `d0bf986`

`A2SPatch::Apply()` changed from optional-pattern to `.value()` — if the "A2S Advertise
Gate" signature is missing from `deadworks_mem.jsonc`, deadworks aborts on startup rather
than silently skipping the patch and producing servers that don't appear in browser.
Also removed an unused inline hook that was wired to a null detour.

### RemoveAbility log noise

**Commits:** `073f443`, `9d876bd`

Noisy per-call debug logging removed/commented out from `NativeAbility.cpp`.

---

## Launcher changes

**Commits:** `03f7f87` (fix old install dir detection), `0cfdf44` (version bump)

Tauri launcher only — not visible to server plugin authors.

---

## Repo impact — deadlock-server-plugins

- **None required.** No deprecated APIs. No signature changes that break existing plugins.
- **Opportunities:** `OnPawnHeroInitialized` is directly useful for TrooperInvasion's
  `SelectHero` flow (currently uses a `Timer.Once(1.Seconds())` delay to wait for hero
  initialization). Can be replaced with `SwapOrReset(..., onReady: () => ...)`.
- `CBaseEntity.ModelName` / `SetScale` are additive; no current plugin uses them.
- Observer API is additive; could be useful for a future spectator-placement feature.
