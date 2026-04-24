---
source: user-provided release summary (2026-04-24)
upstream: https://github.com/Deadworks-net/deadworks/releases
version: 0.4.6
captured: 2026-04-24
---

# Deadworks v0.4.6 — release notes

> User-provided summary captured 2026-04-24 from
> <https://github.com/Deadworks-net/deadworks/releases>.

## Changes

- **All heroes are no longer precached by default.** This improves loading
  times and memory usage of connecting players. `Precache.AddHero` remains
  available for usage in `OnPrecacheResources`.
- Added `CCitadelAbilityComponent.FindAbilityByName(string abilityName)`.
- Added new overload `CCitadelPlayerPawn.RemoveAbility(CCitadelBaseAbility ability)`.
- Added `Entities.ByName(string name)`, `Entities.ByName<T>(string name)`,
  `Entities.FirstByName(string name)`, and `Entities.FirstByName<T>(string name)`
  for finding entities by targetname.
- Added `CCitadelPlayerPawn.GetStamina()` and
  `CCitadelPlayerPawn.SetStamina(float value)`.
- `EntityData` now implements `IEnumerable<KeyValuePair<CBaseEntity, T>>`.
- Equality comparisons for `CBaseEntity` now compare the equality of the
  underlying handle.
- Fixed network notifications for `AbilityResource.LatchTime` and
  `AbilityResource.LatchValue`.
