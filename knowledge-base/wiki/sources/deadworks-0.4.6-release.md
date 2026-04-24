---
title: "Source: Deadworks v0.4.6 release"
type: source-summary
sources:
  - knowledge-base/raw/articles/deadworks-0.4.6-release.md
  - ../deadworks/managed/EntryPoint.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CCitadelAbilityComponent.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CCitadelPlayerPawn.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/Entities.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/EntityData.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/CBaseEntity.cs
  - ../deadworks/managed/DeadworksManaged.Api/Entities/AbilityResource.cs
related:
  - "[[deadworks-runtime]]"
  - "[[plugin-api-surface]]"
  - "[[schema-accessors]]"
  - "[[deadworks-0.4.5-release]]"
  - "[[glossary]]"
created: 2026-04-24
updated: 2026-04-24
confidence: high
---

# Source: Deadworks v0.4.6 release

User-provided summary of Deadworks v0.4.6 release notes, captured
2026-04-24. Each bullet was verified against upstream commits on tag
`v0.4.6` in `../deadworks/` (8 commits since `v0.4.5`).

## Headline changes

1. **Heroes are no longer precached by default.** Previously,
   `EntryPoint.OnPrecacheResources` iterated every `Heroes` enum value
   and called `Precache.AddHero(hero)` for each `AvailableInGame` entry
   before dispatching to plugins. That auto-loop is gone (commit
   `0dcf287`, 2026-04-23). Connecting players skip loading resources
   they never need. **`Precache.AddHero` is still available** for
   plugins that genuinely need a specific hero's resources — call it
   from `OnPrecacheResources`.
2. **`CCitadelAbilityComponent.FindAbilityByName(string)`** — new
   method that returns `CCitadelBaseAbility?` by internal ability name
   (e.g. `"citadel_ability_primary_dash"`). Commit `b84d68c`
   (2026-04-24). Backed by new `NativeInterop.FindAbilityByName`
   native callback.
3. **`CCitadelPlayerPawn.RemoveAbility(CCitadelBaseAbility)`** — new
   overload alongside the existing `RemoveAbility` entrypoints. Returns
   `bool` on success. Backed by `NativeInterop.RemoveAbilityByEntity`.
   Commit `b84d68c`.
4. **Entity lookup by targetname** — four new static methods on
   `Entities`:
   - `Entities.FirstByName(string) → CBaseEntity?`
   - `Entities.FirstByName<T>(string) → T?` (where `T : CBaseEntity`)
   - `Entities.ByName(string) → IEnumerable<CBaseEntity>`
   - `Entities.ByName<T>(string) → IEnumerable<T>`
   All **case-sensitive** per the XML doc comments. Backed by a
   cursor-style `NativeInterop.FindEntityByName(cursor, name)` callback
   that iterates the engine's `CGlobalEntityList` targetname index.
   Commit `ea10f94`.
5. **`CCitadelPlayerPawn.GetStamina()` / `SetStamina(float)`** — new
   helpers that read/write `AbilityComponent.ResourceStamina`.
   `SetStamina` writes three fields in one call: `CurrentValue`,
   `LatchValue`, and `LatchTime = GlobalVars.CurTime`. Commit `0b2dd87`.
6. **`EntityData<T>` is now `IEnumerable<KeyValuePair<CBaseEntity, T>>`**
   and exposes a `Count` property. Plugins can iterate per-entity
   state via `foreach (var (entity, value) in _data)`. Enumeration
   yields `new CBaseEntity(handle)` wrappers per entry. Doc warns:
   **do not add/remove entries while iterating.** Commit `44c2f8e`.
7. **`CBaseEntity` equality compares the packed entity handle.**
   `CBaseEntity` now implements `IEquatable<CBaseEntity>` with
   `==`/`!=` operators, `Equals(object?)`, and `GetHashCode()` all
   derived from `EntityHandle` (serial + index packed `uint`). Prior
   releases fell back to reference-equality on the wrapper, which
   broke when two wrappers were constructed for the same native entity
   — `wrapA == wrapB` could be `false` even though both pointed at the
   same entity. Commit `f1f83e6`. _Commit subject says
   "EntityIndex-based equality" but the code compares full
   `EntityHandle` (serial+index)._
8. **`AbilityResource.LatchTime` / `LatchValue` setters now fire
   network notifications.** Previously these setters did raw pointer
   writes (`*(float*)_latchTime.GetAddress(...) = value`) that
   bypassed `NotifyStateChanged`, so client-side latch state drifted
   after a plugin write. Fixed by constructing the accessors with the
   networked flag (`new(Class, "m_flLatchTime"u8, 1)`) and routing
   writes through `SchemaAccessor.Set` (which auto-notifies). Commit
   `0f5a5af`. **Direct impact:** the `SetStamina` helper added in
   the same release now actually propagates — prior to this fix,
   plugins setting latch state on AbilityResource would have had
   their writes de-synced with clients.

## Commit map (v0.4.5 → v0.4.6)

| Commit | Subject | Notes |
|---|---|---|
| `8dd5b78` | split classes to their own files | refactor, no API change |
| `0dcf287` | stop precaching all heroes by default | bullet 1 |
| `b84d68c` | new ability apis | bullets 2, 3 |
| `ea10f94` | add way to find abilities by targetname | bullet 4 — note commit subject mentions abilities but the added API is `Entities.ByName`/`FirstByName` (entity-targetname, not ability-name) |
| `44c2f8e` | make EntityData enumerable | bullet 6 |
| `f1f83e6` | EntityIndex-based equality for CBaseEntity | bullet 7 |
| `0f5a5af` | properly notify for change on networked AbilityResource_t fields | bullet 8 |
| `0b2dd87` | add CCitadelPlayerPawn.GetStamina and SetStamina helpers | bullet 5 |

## Impact on this repo

- **`OnPrecacheResources` hook is now effectively "bring your own
  heroes" territory.** No plugin in this repo currently overrides
  `OnPrecacheResources` (grepped `--include='*.cs'`). Plugins that
  dynamically swap heroes on players mid-match (e.g.
  [[trooper-invasion]] theoretically, or a future hero-roulette mode)
  may now need to precache ahead of time. No known regression — all
  current gamemodes rely on the map-prescribed hero, which the engine
  loads on selection.
- **Stamina manipulation became two lines.** Anyone wiring a
  "refill stamina on kill" mechanic previously needed to find the
  `ResourceStamina` sub-struct and write three fields in lockstep;
  `pawn.SetStamina(3f)` now does it. Combined with the
  `AbilityResource` latch fix, it now networks correctly on the
  first frame.
- **`EntityData<T>` iteration unblocks "reset all tracked entities"
  patterns.** [[trooper-invasion]]'s scheduler tracks spawned
  troopers; TrooperInvasion currently stores alive-trooper tracking
  in plain `HashSet<int>` fields rather than `EntityData`. If that
  ever migrates, `foreach` over the data store is now available
  without maintaining a parallel keyset.
- **`CBaseEntity` equality semantics flip.** Any code relying on
  reference-equality of wrappers (e.g. `list.Contains(ent)`,
  `Dictionary<CBaseEntity, …>`) now uses handle-equality. Grep
  found no such usages in this repo; all plugins key by
  `EntityIndex`/`Slot`/custom IDs, not by wrapper reference. Still
  worth watching for on next plugin write.
- **`Entities.ByName` / `FirstByName`** could replace any mapper-
  wired entity lookup pattern that currently scans `Entities.All`
  filtering by `targetname`. Fast path for map-scripted entities
  (`info_player_*`, named triggers, named props).

## Cross-cutting implications

- **No deprecations in this release.** v0.4.5 deprecated
  `[ChatCommand]`/`[ConCommand]`; this release neither removes them
  nor adds new deprecations. All three plugins in this repo
  ([[deathmatch]], [[lock-timer]], [[status-poker]] — plus
  [[trooper-invasion]]) are already migrated to `[Command]`.
- **`Precache.AddHero` remains the canonical hero-precache entry.**
  Wiki's existing `Precache` row on [[plugin-api-surface]] describes
  it as "call from `OnPrecacheResources`" — that remains the guidance.

## Feedback / surprises

- Commit `ea10f94`'s subject ("add way to find abilities by
  targetname") is misleading — the added API is for **entities** by
  targetname (`Entities.ByName` family), not abilities. Abilities get
  their dedicated `FindAbilityByName` in `b84d68c` (the prior commit).
  Release notes are accurate; the commit subject is not.
- Bullet 8 explains *why* `SetStamina` (bullet 5) is actually useful:
  without the AbilityResource latch fix, the new stamina helper
  would have written fields that don't network. The two commits
  landed the same day and are semantically coupled.
