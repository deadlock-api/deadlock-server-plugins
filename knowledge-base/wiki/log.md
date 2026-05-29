---
title: Operation Log
type: log
---

# Operation Log

Append-only. Newest entries on top. Every ingest, query-that-wrote-a-page,
and lint run gets an entry.

## [2026-05-29] — TrooperInvasion self-spawns tier1 lane Guardians

- **Operation:** feature + ingest
- **Sources:** `raw/notes/2026-05-29-trooper-invasion-guardians-self-spawn-solution.md`
  (+ supporting `2026-05-29-dl-midtown-tier1-guardians-mode-invalid-proof.md`,
  `2026-05-29-deadworks-usercmd-visitor-build-list-omission.md`)
- **Pages updated (2):** `plugins/trooper-invasion.md` (new "Lane Guardians — self-spawned"
  section + supersede banner + new `TrooperInvasion.Guardians.cs` in Files); `index.md`
  (last-ingest banner).
- **Key findings:** tier1 Guardians never spawned on the insecure dedicated server. VRF
  decompile of `dl_midtown` `default_ents.vents_c` proved the map is **authored for**
  Guardians (per-lane tier1 shops + zipline `guard_boss_name` wiring referencing
  `boss_{combine,rebel}_t1_{blue,yellow,purple}`) but does **not place the npc_boss_tier1
  entities** — they're spawned by a match-init code path the server never runs. Neither
  `m_eGameState` pinning nor `game_mode/match_mode=Normal/Unranked` triggers them (modes
  default to **Invalid**, not Normal as previously believed). **Fix:** self-spawn the 6 via
  `CreateByDesignerName("npc_boss_tier1")` + `CEntityKeyValues` (`subclass_name=
  "npc_boss_tier1"` is load-bearing) from `ArmWaves`. No crash (vs the boss-wave null-KV
  crash — populated KV is the difference). **Gotcha:** spawned Guardians report
  `DesignerName="npc_trooper_boss"`, so they self-culled via the friendly-trooper handler
  until exempted by `_guardianIndices` (added before `Spawn()`).
- **Supersedes:** the "dl_midtown has no tier1 Guardians" conclusion in
  `2026-05-04-dl-midtown-no-tier1-guardians.md` (map IS authored for them; they're just not
  map-placed entities) and the mode-related refutations therein (modes read Invalid, and
  fixing that doesn't spawn Guardians).
- **Aside:** `make dev` native link was broken — `../deadworks` commit `1b94487` added
  `UsercmdVisitorRuntime.cpp` but not to `docker/build-native.sh`'s source list; fixed
  (one line) in the deadworks repo.

## [2026-05-29] — extract StuckCommand plugin from TrooperInvasion

- **Operation:** refactor (extract reusable plugin) + ingest
- **Source:** `raw/notes/2026-05-29-stuck-command-extraction.md`
- **Pages created (1):** `plugins/stuck-command.md`
- **Pages updated (2):** `plugins/trooper-invasion.md` (command table note that
  `!stuck`/`!suicide` now come from [[stuck-command]]); `index.md` (new plugin in
  catalog, totals 38→39, last-ingest banner).
- **Key findings:** `!stuck`/`!suicide` moved out of `TrooperInvasionPlugin` into a
  standalone `StuckCommandPlugin` (csproj cloned from HealOnSpawn). Wired into
  `gamemodes.json` for `normal`, `lock-timer`, `trooper-invasion`, plus paths-filter
  entries in both CI workflows. **Deliberately excluded from deathmatch:** Deathmatch
  (`Deathmatch.cs:852`) has its own stuck/suicide that clears spawn-protection
  invulnerability before the lethal hit — the generic plugin would duplicate-register
  and be absorbed. Confirmed no other plugin (StatusPoker/Hostname/Feedback/LockTimer)
  registers stuck/suicide. Makefile + docker-gamemodes read `gamemodes.json` dynamically,
  so no hardcoded plugin list to update. Both new/modified projects build 0 errors.
- **Next:** none.

## [2026-05-29] — TrooperInvasion single-class file split (refactor)

- **Operation:** refactor + wiki maintenance (no ingest discussion needed — structural only)
- **Source:** `raw/notes/2026-05-29-trooper-invasion-file-split.md`
- **Pages updated (1):** `plugins/trooper-invasion.md` — corrected the stale
  "single-file plugin (~500 LOC)" claim in the Files section; now lists the
  8-file layout. Added new source files + the raw note to frontmatter; bumped
  `updated` to 2026-05-29.
- **Key findings:** `TrooperInvasionPlugin` (1045 lines, one class/file) split with
  **zero behavioural change** into two real collaborators (`WaveTuning` static pure
  tuning, `SessionStats` telemetry) + five `partial class` files by concern
  (`.Waves`/`.Troopers`/`.EndMode`/`.Players`/`.Commands`). `RoundLength` moved to
  `WaveTuning`. Builds 0 errors against the sibling-deadworks ProjectReference.
  No csproj change (default glob compiles all `.cs`).
- **Next:** none.

## [2026-05-29] — ingest DisconnectCleanup managed-API refactor

- **Operation:** ingest (repo source + commit)
- **Source:** `../DisconnectCleanup/DisconnectCleanup.cs` at commit `1f7ae19`
  ("refactor(disconnect-cleanup): remove player via managed API, drop sv_cheats").
  Cross-checked against `../deadworks` (`ClientDisconnectedEvent.cs`,
  `CCitadelPlayerController.cs`, `CBaseEntity.cs`, `TagPlugin.cs`).
- **Pages created (2):**
  - `sources/disconnect-cleanup-managed-refactor.md` — before/after diff, rationale,
    full API verification, team-roster gap.
  - `plugins/disconnect-cleanup.md` — first page for this plugin; behaviour, history,
    relationship to Deathmatch/TrooperInvasion/LockTimer.
- **Pages updated (1):**
  - `concepts/deadlock-game.md` — "Player disconnect cleanup" section rewritten:
    `citadel_kick_disconnected_players` now marked **tried-and-abandoned**, no longer
    the recommended path; added related links; `updated: 2026-05-29`.
  - (plus `index.md` header/catalog/counts and this log.)
- **Key findings:** managed removal drops the `sv_cheats` toggle and is scoped to the
  single disconnecting client, vs. the concommand's server-wide sweep. The
  `controller.GetHeroPawn()?.Remove();` idiom matches upstream `TagPlugin.cs:137`.
- **Contradiction resolved:** this **reverses** the 2026-04-23 recommendation that the
  cheat-flagged concommand replace the manual pawn/controller removal. Wiki now reflects
  the repo's actual choice.
- **Gap flagged:** concommand help text claims it also removes players "from any teams";
  the managed pawn+controller `Remove()` does not explicitly do roster cleanup. Whether
  team-roster state lingers after managed removal is **untested**.

## [2026-05-13] — ingest Deadworks v0.4.8 release notes

- **Operation:** ingest (release notes + git history)
- **Sources:**
  - `raw/notes/2026-05-13-deadworks-0.4.8-release.md` — v0.4.8 release notes,
    derived from `git log v0.4.7..v0.4.8 --oneline` on `Deadworks-net/deadworks`.
    5 commits (`1c3554a`, `67ac6e9`, `11cff37`, `93a3dd2`, `36d481a`);
    every commit diff verified.
- **Pages created (1):**
  - `sources/deadworks-0.4.8-release.md` — full commit map, impact analysis,
    key findings including `CCollisionProperty` identity-rotation caveat and
    `IsReservedSlot` sig-not-required gotcha.
- **Pages updated (4):**
  - `concepts/deadworks-runtime.md` — new "v0.4.8" section (Friction, RespawnTime,
    Collision/CCollisionProperty/BoundingBox, IsReservedSlot native hook);
    new source + related link; `updated: 2026-05-13`.
  - `concepts/plugin-api-surface.md` — new "v0.4.8 API additions" section;
    new source + related link; `updated: 2026-05-13`.
  - `entities/schema-accessors.md` — new "CCollisionProperty" section with
    full member table and rotation gotcha; entity type reference row updated;
    new source + related link; `updated: 2026-05-13`.
  - `index.md` — last-ingest blurb rewritten; prior blurb demoted; new source
    page added to Sources section; page total bumped 35 → 36.
  - `log.md` — this entry.
- **Key findings / surprises:**
  - **`CCollisionProperty.WorldMins/WorldMaxs` assume identity rotation.** The
    source code comment is explicit. For rotated entities the world-space AABB
    computed by `WorldMins = Mins + Owner.Position` is geometrically wrong.
    Deadlock has plenty of angled geometry, so any plugin using `Contains` on
    map entities should verify the entity is axis-aligned or accept approximate
    results.
  - **`IsReservedSlot` sig is NOT in the required-sig crash list.** The startup
    `ValidateSignatures` pass in deadworks crashes the server if a required
    sig is missing, but `CServerSideClientBase::IsReservedSlot` was added as
    an optional entry. If the sig goes stale after an engine update, the hook
    silently doesn't install and slot reservation resumes — no observable crash
    at startup. Server operators would see the "full server" symptom return
    without any error log.
  - **`11cff37` (launcher: Linux path detection) not ingested.** Launcher-only
    change; no plugin or server-runtime impact.
- **Repo impact:** No plugin changes required for v0.4.8 (no breaking changes).
  All new APIs are additive; no plugin currently uses Collision, Friction, or RespawnTime.
- **Contradictions flagged:** none.

## [2026-05-02] — ingest Deadworks v0.4.7 release notes + TrooperInvasion git history

- **Operation:** ingest (release notes + git history catch-up)
- **Sources:**
  - `raw/articles/deadworks-0.4.7-release.md` — v0.4.7 release notes, manually
    captured from upstream `../deadworks/` git log between tags `v0.4.6` and `v0.4.7`
    (12 commits: `2ef15c5..300f0fa`, 2026-04-25 → 2026-05-01). Every commit diff
    verified.
  - `raw/notes/2026-04-28-trooper-invasion-friendly-guardian-false-defeat.md` — captured
    during fix session; describes the scripted-weaken false-defeat bug.
  - `raw/notes/2026-05-02-trooper-invasion-world-reset-changelevel.md` — written this
    session; documents the changelevel-based post-mode reset.
  - `raw/notes/2026-05-02-trooper-invasion-dead-trooper-reconciler.md` — written this
    session; documents the dying-trooper reconciler fix and the reverted EntityHandle
    approach.
  - Server-plugins git history: commits `faa2b33`, `196b442`, `5f4d527`, `b7ea812`,
    `c2813ae`, `c359ee5` (2026-04-24 → 2026-04-28).
- **Pages created (2):**
  - `sources/deadworks-0.4.7-release.md` — full commit map, impact analysis,
    key findings including `OnPawnHeroInitialized` fire-on-every-path gotcha,
    `OnceHeroInitialized` handle vs EntityHandle note, `IsValidObserverTarget`
    team-3 exclusion implication for TrooperInvasion.
  - `entities/observer-api.md` — full reference for the v0.4.7 observer/spectator
    surface: `CPlayer_ObserverServices`, `ObserverMode_t` enum, `CBasePlayerPawn`
    pass-throughs, `CCitadelPlayerController.MakeObserver()`, native signatures,
    current usage (none) and candidate uses.
- **Pages updated (6):**
  - `concepts/plugin-api-surface.md` — new "v0.4.7 API additions" section;
    `Enums/` row updated to mention `ObserverMode_t`; new source + related links;
    `updated: 2026-05-02`.
  - `entities/events-surface.md` — updated preamble to note 24 hooks (was 23);
    new "Hero init (v0.4.7)" section with `OnPawnHeroInitialized` entry and
    fires-on-every-path warning. `updated: 2026-05-02`.
  - `concepts/deadworks-runtime.md` — new "v0.4.7" section mirroring release
    bullets; `related:` + source added; `updated: 2026-05-02`.
  - `plugins/trooper-invasion.md` — three new sections:
    "False-defeat suppression — guardian scripted-weaken gate" (commit `196b442`),
    "Post-mode world reset — changelevel" (commit `5f4d527`),
    "Alive-trooper tracking and reconciler" (commit `c359ee5`);
    win/lose-conditions bullet updated to cross-reference; stale "missing"
    NetMessages bullet corrected; new sources; `updated: 2026-05-02`.
  - `index.md` — last-ingest blurb rewritten; prior blurb demoted; new pages
    added to Sources and Entities sections; page total bumped 33 → 35.
  - `log.md` — this entry.
- **Key findings / surprises:**
  - **`OnPawnHeroInitialized` fires on initial spawn too.** The hook name implies
    "when the player swaps heroes" but it fires whenever
    `CCitadelPlayerPawn::InitializeHeroOnPawn` runs — including the initial hero
    assignment on `OnClientFullConnect`. Plugins using this hook for post-swap setup
    must guard against running setup code twice if they also handle `OnClientFullConnect`.
    The one-shot `OnceHeroInitialized` avoids this problem by design.
  - **`IsValidObserverTarget` hardcodes team 3 as invalid.** TrooperInvasion's enemy
    troopers are team 3; the managed helper would reject them as observer targets.
    Plugins can bypass the helper and call `SetObserverTarget` directly.
  - **The revert pair `c2813ae` / `b7ea812` deleted a raw note.** Commit `c2813ae`
    added `raw/notes/2026-04-28-trooper-invasion-alive-set-leak.md`; the revert commit
    `b7ea812` deleted it. This violates the "never edit raw/ after writing" policy
    (the note was deleted, not just superseded). The raw note no longer exists on disk.
    The replacement `2026-05-02-trooper-invasion-dead-trooper-reconciler.md` covers the
    same territory with the definitive approach. No correction needed in raw/ (it's
    already gone); flagged here for the record.
  - **Changelevel is the only reliable full-engine reset on `dl_midtown`.** Confirmed
    that `citadel_match_end`, `mp_restartgame`, and `citadel_street_brawl_reset` do not
    apply. This is a generalizable finding: any mode on `dl_midtown` that needs a clean
    engine-side round reset must use `changelevel`.
  - **`faa2b33` (docs: advertise !feedback) not ingested into wiki.** The commit adds
    `!feedback` to help text in both Deathmatch and TrooperInvasion. This is a
    user-visible documentation change, not a code/API finding — no wiki page needed.
- **Repo impact:** No plugin changes required for v0.4.7 (no breaking changes).
  TrooperInvasion wiki pages now accurate to the post-2026-04-28 state.
- **Contradictions flagged:**
  - `plugins/trooper-invasion.md` "What's intentionally missing" had a stale bullet
    "No NetMessages.Send HUD announcements" — the HUD announcements feature was
    implemented in a prior session and the "missing" section wasn't updated.
    **Corrected** this session.

## [2026-04-24] — ingest Deadworks v0.4.6 release notes

- **Operation:** ingest
- **Source:** [[deadworks-0.4.6-release]] — user-provided release notes
  for `v0.4.6` at
  `https://github.com/Deadworks-net/deadworks/releases`, captured at
  `raw/articles/deadworks-0.4.6-release.md`. Every bullet verified
  against upstream commits on tag `v0.4.6` in `../deadworks/`
  (8 commits since `v0.4.5`: `8dd5b78`, `0dcf287`, `b84d68c`,
  `ea10f94`, `44c2f8e`, `f1f83e6`, `0f5a5af`, `0b2dd87`).
- **Pages created:** `sources/deadworks-0.4.6-release.md` — full
  commit map, impact analysis, cross-cutting implications.
- **Pages updated:**
  - `concepts/plugin-api-surface.md` — new "v0.4.6 API additions"
    section; `Precache` row annotated with the auto-precache removal;
    added raw article source + `[[deadworks-0.4.6-release]]` related
    link; `updated: 2026-04-24`.
  - `entities/schema-accessors.md` — added `IEnumerable` +
    `Count` note under `EntityData<T>`; new "`CBaseEntity` equality
    (v0.4.6)" section; new "`Entities` — query helpers (v0.4.6)"
    section distinguishing `ByClass` / `ByDesignerName` /
    `ByName`; `AbilityResource` entry added to type reference with
    the latch-networking fix. Sources + related links + `updated`.
  - `concepts/deadworks-runtime.md` — new "v0.4.6 (2026-04-24)"
    section mirroring the release bullets with links to
    [[schema-accessors]]; sources + related; `updated`.
  - `index.md` — last-ingest blurb rewritten; prior blurb demoted to
    "Prev ingest"; new source page listed under Sources; total page
    count bumped 32 → 33.
- **Key findings / surprises:**
  - **Commit `ea10f94`'s subject is misleading.** "Add way to find
    abilities by targetname" — but the added API is `Entities.ByName`
    family for **entities** by targetname. Abilities got their
    `FindAbilityByName` in the prior commit `b84d68c`. Release notes
    are accurate; the commit subject is not.
  - **`SetStamina` + `AbilityResource` latch fix are coupled.** Both
    landed 2026-04-24 and the release notes list them as separate
    bullets. Without the latch fix (`0f5a5af`), the new `SetStamina`
    helper (`0b2dd87`) would have written fields that don't network,
    making the helper semi-useless. They're a logical unit.
  - **`CBaseEntity` equality is handle-based, not EntityIndex-based.**
    The commit subject (`f1f83e6`) says "EntityIndex-based equality"
    but the implementation compares the packed `EntityHandle`
    (serial + index). Safer semantic — re-used entity slots with a
    bumped serial compare unequal, as they should.
  - **Hero auto-precache removal is a soft-regression risk for any
    plugin that swaps heroes dynamically.** Grepped the repo — no
    plugin in `deadlock-server-plugins/` currently overrides
    `OnPrecacheResources`, and the existing gamemodes rely on
    map-prescribed heroes. Flagged as "watch for this on future
    hero-roulette modes" in the source summary.
  - **`EntityData<T>` enumeration yields `new CBaseEntity(handle)`**
    where `handle` is the stored `uint` key, passed to the
    `CBaseEntity(nint)` constructor. Works for equality purposes
    (`EntityHandle` property reads back the stored uint) but the
    wrapper's internal `Handle` field is *the entity handle uint*,
    not a native pointer. Plugins that iterate `EntityData` and call
    methods requiring a real pointer (`.Remove()`, schema accessor
    `Set`) would hit undefined behavior. This is an implementation
    caveat of the new API, not documented in the release notes.
    Consider flagging to upstream if it causes issues in practice.
- **Repo impact:** **none today.** All four plugins already migrated
  to `[Command]` (no deprecation cliff in this release); none
  override `OnPrecacheResources`; none depend on wrapper
  reference-equality; no stamina manipulation today; none use
  `AbilityResource` directly. The new APIs are purely additive
  surface we can adopt when we next write a hero-roulette or
  targetname-scanning feature.
- **Contradictions flagged:** none.

- **Operation:** ingest (second scan pass on upstream `../deadworks/`)
- **Source:** user-requested scan of `../deadworks/` for knowledge not
  yet covered in the wiki. Produced 5 raw notes:
  - `raw/notes/2026-04-23-plugin-native-dll-resolution.md`
  - `raw/notes/2026-04-23-telemetry-env-vars.md`
  - `raw/notes/2026-04-23-entity-io-api.md`
  - `raw/notes/2026-04-23-trace-api.md`
  - `raw/notes/2026-04-23-soundevent-builder.md`
- **Pages created (3):**
  - `sources/deadworks-scan-2026-04-23.md` — source summary listing
    commits since last scan, what this pass covered, and wiki
    corrections applied.
  - `entities/entity-io.md` — `EntityIO.HookOutput` / `HookInput`
    plugin-facing API. `EntityOutputEvent` / `EntityInputEvent` shapes,
    `"{designerName}:{outputName}"` ordinal-case-sensitive key,
    snapshot-based lock-free dispatch, exception isolation. **Key
    gotcha documented: no auto-cleanup on plugin unload** — handles
    must be disposed in `OnUnload` or dispatch crashes into a released
    ALC. Exact-match (no wildcards) at hook-registration. Not
    currently used by any plugin in this repo.
  - `entities/trace-api.md` — VPhys2 `Trace` API. Three entrypoints
    (`Ray`, `SimpleTrace`/`SimpleTraceAngles`, `TraceShape`),
    `Ray_t` union with 5 shape variants, `CGameTrace` / `TraceResult`,
    `HitEntityByDesignerName`, silent no-op when
    `NativeInterop.TraceShapeFn == 0`, `CTraceFilter` vtable gotcha
    (zero-vtable filter → engine crash unless `EnsureValid` paper-overs).
- **Pages updated (3):**
  - `entities/deadworks-plugin-loader.md` — replaced paragraph on
    upstream `211583e` (unreachable from `main`) with full section
    describing the `LoadUnmanagedDll` override on canonical SHA
    `f9a876c` (2026-04-14): uses per-plugin
    `AssemblyDependencyResolver.ResolveUnmanagedDllToPath`, enables
    plugins bundling native deps (e.g. `Microsoft.Data.Sqlite` /
    `e_sqlite3`). Managed vs native resolution differ: managed has
    shared-host fast path; native does not. Frontmatter bumped
    (`updated: 2026-04-23`, added new raw note + cross-link).
  - `concepts/deadworks-runtime.md` — Telemetry section heading
    updated from `deb8ff2` to `224d660` (canonical main-branch SHA;
    `deb8ff2` exists but is unreachable). Expanded env-var /
    JSONC-key reference table (`DEADWORKS_TELEMETRY_ENABLED`,
    `_OTLP_ENDPOINT`, `_OTLP_PROTOCOL`, `_SERVICE_NAME`, `_LOG_LEVEL`),
    noted env overrides JSONC, listed JSONC-only keys, documented
    the `NativeEngineLogger` prefix mapping (`trce`/`dbug`/`info`/
    `warn`/`fail`/`crit`).
  - `concepts/plugin-api-surface.md` — Sounds/SoundEvent row rewritten
    to describe the builder API properly (was one line saying
    "v0.4.5 adds single-player target path"); Trace/ row links to new
    `[[trace-api]]` page; Events/ row links to `[[entity-io]]` for
    mapper-wired I/O. Frontmatter bumped with new sources + related
    links.
- **Index + log updated:** two new entity pages + one new source
  summary added to `wiki/index.md`; last-ingest blurb rewritten; page
  total bumped 29 → 32.
- **Key findings (cross-cutting):**
  - **Prior wiki cites unreachable SHAs.** `deb8ff2` (runtime
    telemetry) and `211583e` (plugin-loader native-DLL fix) both exist
    as git objects but are not reachable from `main`. Canonical SHAs
    are `224d660` and `f9a876c`. Citations still resolve via
    `git rev-parse`, so they're not broken — just non-canonical.
    Corrected both. The pre-rebase SHAs likely came from an early
    ingest of PR branches.
  - **EntityIO is the only plugin-facing hook system without
    auto-cleanup.** Timers, ChatCommands, PluginBus events/queries,
    NetMessage hooks all get cleaned up via
    `PluginRegistrationTracker` / context-based owner resolution /
    stack-walk. `PluginLoader.EntityIO.cs` has no owner tracking —
    plugins MUST dispose their `IHandle` in `OnUnload`. Flagged in
    the new entity page.
  - **Trace API works end-to-end.** The wiki previously wrote it off
    as "not used in any example plugin"; closer read shows it's a
    complete VPhys2 surface (line/sphere/hull/capsule/mesh, simple
    LOS checks, entity-filtered casts). Silent no-op fallback means
    it's safe to wire up eagerly; just won't do anything until the
    physics query system is ready post-map-load.
  - **SoundEvent builder has GUID-addressable lifecycle.** `.Emit()`
    returns a `uint GUID` that can be used later with `SetParams` or
    `Stop`/`StopByName`. Field names are MurmurHash2-lowercased with
    specific seeds (`SosHashSeeds.FieldName` / `.SoundeventName`).
    Previously described as just `Play`/`PlayAt` helpers.
  - **Plugin native-DLL resolution uses only the per-plugin resolver.**
    There is no "shared native" concept — each plugin ships its own
    copy of `e_sqlite3.dll` if multiple want SQLite. Fine in practice
    since `runtimes/<rid>/native/` is per-plugin anyway.
- **Contradictions reconciled:**
  - `deadworks-runtime` telemetry section's `deb8ff2` → `224d660`.
  - `deadworks-plugin-loader` native-DLL paragraph's `211583e` →
    `f9a876c`, plus it was under the "Shared assemblies" section and
    didn't actually describe what the fix did — moved out and
    expanded.
- **Scope NOT covered in this pass:** `launcher/` (Tauri app),
  `DeadworksManaged.Tests/` (still deferred), deep re-read of
  `docker/entrypoint.sh` (ops pages already covered the changes), and
  the PluginBus commits (already ingested on 2026-04-23).
- **Candidate next ingest:** Not urgent, but the 2026-04-22 scan
  summary's "single-player target path" line on Sounds remains in
  `deadworks-scan-2026-04-22.md:57` — that source summary is
  immutable history now, so leaving as-is. Reconciliation is on the
  live `plugin-api-surface.md` page.

## [2026-04-23] — catalogue PluginBus (new in upstream deadworks)

- **Operation:** ingest (new API surface added upstream)
- **Source:** `raw/notes/2026-04-23-plugin-bus.md` (captured from a user
  description of the new API; cross-checked against
  `../deadworks/managed/DeadworksManaged.Api/Bus/*.cs`,
  `../deadworks/managed/PluginLoader.PluginBus.cs`,
  `../deadworks/managed/HandlerRegistry.cs`,
  `../deadworks/managed/ConCommandManager.cs`, and
  `../deadworks/managed/DeadworksManaged.Tests/PluginBusTests.cs`)
- **Pages created:** `entities/plugin-bus.md` — full reference:
  events (4 Subscribe overloads + `[EventHandler]`), queries
  (3 `HandleQuery` overloads + `[QueryHandler]`), max-wins `HookResult`
  aggregation, context types, stack-walk sender resolution,
  `AllowMultiple` attributes, exception isolation, collect-all
  semantics, type-identity caveat across `AssemblyLoadContext`,
  `dw_pluginbus` diagnostics with 60-second ring buffers +
  did-you-mean suggestions, perf notes, quick reference table.
- **Pages updated:**
  - `concepts/plugin-api-surface.md` — added `Bus/` row to Subsystems
    table linking to `[[plugin-bus]]`; added raw note + wikilink to
    frontmatter; bumped `updated:` to 2026-04-23.
  - `index.md` — added `[[plugin-bus]]` entry under Entities; replaced
    "last ingest" blurb; bumped page count 28 → 29.
- **Key findings:**
  - **Name comparison is ordinal and case-sensitive.** Both
    `_eventRegistry` and `_queryRegistry` are constructed with
    `StringComparer.Ordinal` in `PluginLoader.cs:66-67`. `"My:Foo"`
    and `"my:foo"` are distinct names.
  - **Type identity is per-plugin ALC.** Because each plugin loads in
    its own collectible `AssemblyLoadContext`, a payload/request class
    defined in two plugins' own DLLs has distinct `Type` identities —
    typed `Subscribe<T>` / `HandleQuery<…>` / typed-request handlers
    silently never match across plugins. Fix: put the contract in
    `DeadworksManaged.Api` (shared-identity assembly) or use untyped
    `object?` payloads. Ordered preference ladder: framework types →
    API types → shared-contract DLL → untyped `object?`.
  - **Typed-request mismatch is the most likely silent bug.**
    `dw_pluginbus` surfaces it via `(handlers: N, responses: 0)` rows
    in the recent-queries section — `0` responses while handlers are
    registered means the request type didn't match any handler's
    declared `TRequest`.
  - **`Publish` aggregation uses the same max-wins rule as
    `IDeadworksPlugin`** (`HookResult.Continue < Stop < Handled`), so
    subscribers "vote" the same way engine-event subscribers do in
    [[events-surface]]. All subscribers always run, even after a
    `Handled`.
  - **Auto-cleanup is free.** Host routes each manual
    `Subscribe`/`HandleQuery` through a stack-walking dispatcher that
    resolves the calling plugin's path and stores it on the
    subscription; `HandlerRegistry.UnregisterPlugin` removes events +
    queries in one pass on unload. No `OnUnload` bookkeeping needed.
  - **Dispatch is direct delegate invocation.** Typed wrappers are
    generated once at register time via cached `MethodInfo +
    MakeGenericMethod` (`BuildTypedEventFunc`, `BuildQueryTypedFunc`
    in `PluginLoader.PluginBus.cs:44-73`). Handler lists are
    snapshot-copied under lock, iterated lock-free — handlers can
    call back into `Publish`/`Query` without self-deadlocking.
  - **Diagnostics ring buffer** holds 64 entries each for publishes
    and queries (`RecentHistoryCapacity`,
    `PluginLoader.PluginBus.cs:34`), filtered to the last 60s at
    display time by `dw_pluginbus`. Command registered in
    `ConCommandManager.cs:26`.
- **Repo impact:** none yet — no plugin in
  `deadlock-server-plugins/` currently publishes or subscribes via
  `PluginBus`. Candidate future uses: TrooperInvasion exposing wave /
  patron state; Deathmatch exposing round boundaries; a shared
  `stats:online_players` query across gamemode plugins. Noted on the
  entity page's "Current usage in this repo" section.
- **Contradictions flagged:** none.

## [2026-04-23] — catalogue citadel_kick_disconnected_players

- **Operation:** ingest (engine-API catalogue + plugin applicability pass)
- **Source:** `raw/notes/2026-04-23-citadel-kick-disconnected-players.md`
- **Pages updated:** `concepts/deadlock-game.md` — new "Player disconnect
  cleanup" section (between Flex-slot mechanics and Pause ConVars)
  describing the concommand, safe invocation idiom, and current
  per-plugin applicability. Source added; `related:` gains
  `[[trooper-invasion]]`; `updated:` bumped. `index.md` last-ingest line
  replaced.
- **Key findings:**
  - **`citadel_kick_disconnected_players` exists** in `server.dll`. Help
    string (verbatim): *"Clear out all players who aren't connected,
    removing them from any teams"*. Confirmed by `strings -n 8
    game/server.dll` (custom-server build).
  - **It's a concommand, not a standing convar** — imperative "run-once
    to sweep". Flag category unconfirmed from strings alone; sits next
    to `citadel_guide_bot_say` and cinematic-restart in the dump, so
    safest invocation is the repo's existing `sv_cheats 1 / … /
    sv_cheats 0` bracket (per `FlexSlotUnlock.cs:29-31`) via
    `Server.ExecuteCommand` (not `ConVar.Find().Set*`).
  - **Deathmatch and TrooperInvasion could substitute this for the
    manual `pawn.Remove() + controller.Remove()` pair** inside their
    `OnClientDisconnect` handlers (`Deathmatch.cs:983-985`,
    `TrooperInvasion.cs:899-902`). The help text's "removing them from
    any teams" hints at extra team-roster bookkeeping the manual path
    doesn't do explicitly. Not swapped in this pass — catalogue only;
    would want a test harness to confirm no regressions.
  - **LockTimer `OnClientDisconnect` does not apply** — it only clears
    plugin-internal dicts (engine per-slot state, HUD maps), no entity
    `Remove()` calls.
  - **StatusPoker, FlexSlotUnlock, HealOnSpawn, HeroSelect, Hostname,
    TeamChangeBlock do not define `OnClientDisconnect`** — the concommand
    is not relevant to them.
  - **Other potential uses (unimplemented)**: periodic janitor via
    `Timer.Every`, round-reset sweep in TrooperInvasion after
    `DisarmWaves("last player disconnected")`, or defensive
    `OnStartupServer` call.
- **Caveats:** FCVAR flags unverified; bulk sweep means per-disconnect
  invocation still scans all slots; event re-entrancy unknown.
- **Contradictions flagged:** none.

## [2026-04-23] — boss-wave native crash; boss waves removed from TrooperInvasion

- **Operation:** ingest (bug-fix + feature-removal finding)
- **Source:** `raw/notes/2026-04-23-boss-wave-native-crash.md`
- **Pages updated:** `plugins/trooper-invasion.md` — new "Boss waves —
  removed (native crash on first spawn)" section documents the managed
  spawn path that crashed the server, why (lane-AI NPCs need a populated
  `CEntityKeyValues` at Spawn time for `m_iLane` + squad + navmesh region;
  `CCitadelTrooperSpawnGameSystem` is the engine's real spawn path), why
  `CPointWorldText`/`ParticleSystem` managed spawns don't trip it (they
  pass explicit KV; no AI dependency), and the reach-for-next-time
  `citadel_spawn_trooper x,y,z boss` + `sv_cheats 1/0` pattern. `updated:`
  bumped; new raw note appended to `sources:`.
- **Key findings:**
  - **`CreateByDesignerName("npc_trooper_boss") + Spawn()` with null
    CEntityKeyValues crashes the server natively** on the first spawn.
    `npc_trooper_boss` is a lane-AI NPC; the engine's normal path is
    `CEntitySpawner<CNPC_TrooperBoss>::Spawn` driven by
    `CCitadelTrooperSpawnGameSystem` and the map's `info_trooper_spawn` /
    `info_super_trooper_spawn` entities, which populate `m_iLane` + squad
    + navmesh region through a fully-built KV. Without that, the post-
    `Spawn` AI init dereferences null. Sub-managed-boundary crash — C#
    `try/catch` doesn't see it.
  - **Managed entity creation is only safe for point entities with
    explicit KV.** `CPointWorldText.Create` and `ParticleSystem.Spawn`
    use `Spawn(ekv)` with a full `CEntityKeyValues` and have no lane/
    AI dependency, which is why those work.
  - **If boss waves are reintroduced**, use the engine's native cheat
    concommand `citadel_spawn_trooper %f,%f,%f %s` (valid types
    `default / boss / melee / medic / flying` — confirmed from
    `server.dll` strings). Bracket with `sv_cheats 1 / sv_cheats 0` —
    same idiom `FlexSlotUnlock.cs:29-31` uses for
    `citadel_unlock_flex_slots`. Format coords with
    `CultureInfo.InvariantCulture` so a Wine locale doesn't turn
    `123.45` into `123,45` and corrupt the comma-separated args list.
  - **Engine still emits `npc_trooper_boss` naturally** via the
    super-trooper promotion (`citadel_super_trooper_gold_mult`), so
    the plugin's `_trooperDesigners` list, OnEntitySpawned cap
    tracking, HP scaling, and kill-attribution branches still need to
    recognise bosses — only our *manual* spawning is gone.
- **Code changes:**
  - Removed `SpawnBossTroopers`, `TriggerBossWave`, `IsBossWave`,
    `BossBonusGoldAt` methods.
  - Removed `_pendingBossSpawn` handle + all Cancel/clear sites.
  - Removed constants: `BossWaveEveryN`, `BossesPerLane`,
    `BossBonusGoldBase`, `BossBonusGoldPerWave`,
    `BossSpawnDelaySeconds`.
  - Removed boss-kill bonus branch in `OnEntityKilled`
    (including the `ECurrencySource.EBossKill` payout).
  - Removed `[BOSS WAVE]` suffix from `!wave` output and the help line
    advertising boss waves.
  - Removed `ti_boss_wave_started` + `ti_boss_killed` PostHog events.
- **Contradictions flagged:**
  - Raw note `2026-04-23-trooper-invasion-boss-waves.md` §1
    ("`CreateByDesignerName + Spawn()`") is **superseded** — that
    managed-spawn pattern crashes for lane-AI NPCs. The note was
    written pre-production-test. Section §§2–6 remain valid.
  - Raw notes are append-only, so the correction lives in
    `2026-04-23-boss-wave-native-crash.md` and the new wiki section.
- **Related surface still intact (not removed):**
  - `npc_trooper_boss` in `_trooperDesigners` — for
    engine-promoted-bosses tracking.
  - `!voteskip` command, round summary, HP scaling across waves/rounds
    — all unrelated to the boss-wave feature.

## [2026-04-22] — Deadlock has only 3 lanes; fix TrooperInvasion 4-player wedge

- **Operation:** ingest (bug-fix finding)
- **Source:** `raw/notes/2026-04-22-deadlock-three-lanes-only.md`
- **Pages updated:** `plugins/trooper-invasion.md` — rewrote the Lane
  gating section with the corrected 3-lane model (`{1=Yellow, 4=Blue,
  6=Purple}`), explicit-marker OR formula, and post-mortem on the
  `(1 << N) - 1` wedge. Added the new raw note to the sources list.
- **Key findings:**
  - **Deadlock currently has 3 lanes, not 4.** Valid `citadel_active_lane`
    single-lane IDs are `{1, 4, 6}`. Value `3` (Green) is no longer a
    lane and silently no-ops the spawn pipeline when written.
  - **Bitmask math `(1 << N) - 1` is wrong for this convar.** It
    produced mask `3` at 4 players, referencing the defunct Green lane.
    Scheduler kept running (wave timers fired, "next in 19s" chat lines
    printed) but zero troopers emerged. Symptom surfaced exactly at
    4 players because that was the first transition away from the
    1-lane Yellow-only value.
  - **Fix:** OR the first `activeLanes` markers from `{1, 4, 6}`, with
    `activeLanes = Clamp(humans/2, 1, 3)`. Produces masks `{1, 5, 7}`
    for 1/2/3 lanes — none collide with single-lane IDs, so the engine
    interprets them as combined bitmasks.
- **Contradictions flagged (for next lint):**
  - `raw/notes/2026-04-22-citadel-active-lane-bitmask.md` claims
    "Deadlock's 4 lanes map to bits `0b0001 / 0b0010 / 0b0100 / 0b1000`"
    and that `(1 << N) - 1` is a valid "N lanes" mask. **Both wrong** —
    Deadlock has 3 lanes and the convar takes lane IDs, not bit
    positions. Raw notes are append-only; correction lives in the new
    `2026-04-22-deadlock-three-lanes-only.md` note and this log entry.
  - `Deathmatch.cs:51` has `_laneCycle = { 1, 3, 6 }` with the comment
    "Yellow, Green, Purple — skip Blue (4)". The value `3` is stale
    (Green no longer exists) — Deathmatch's lane rotation probably
    misbehaves on the middle entry. Separate investigation; not touched
    in this fix.
- **Commit:** `5382526` (fix(trooper-invasion): use Deathmatch lane IDs
  to avoid 4-player wedge).

## [2026-04-22] — TrooperInvasion round-cycle, lane-gating, HUD toasts

- **Operation:** ingest (iteration session)
- **Sources:** three new raw notes from the iteration session:
  - `raw/notes/2026-04-22-trooper-invasion-round-cycle-and-balancing.md`
  - `raw/notes/2026-04-22-citadel-active-lane-bitmask.md`
  - `raw/notes/2026-04-22-hud-game-announcement.md`
- **Pages updated:** `plugins/trooper-invasion.md` — added Round Cycling
  section, Lane Gating section, HUD Announcements section, corrected the
  Wave Volume table to reflect player-scaled + ramp formula, and removed
  the per-tick match-clock anchor description (that code is deleted).
  `index.md` header note bumped.
- **Key findings:**
  - **`citadel_active_lane` is a bitmask, not a count.** `(1 << N) - 1`
    enables N cumulative lanes. DeathmatchPlugin uses `4` (`0b0100`) for
    a single lane; TagPlugin uses `255` for all-on. TrooperInvasion uses
    `Clamp(humans/2, 1, 4)` lanes to implement a "≥ 2 players per active
    lane" rule, rewritten every `RunWave` via `Server.ExecuteCommand`.
  - **`CCitadelUserMsg_HudGameAnnouncement` pattern.** Centered HUD toast
    with `TitleLocstring` + `DescriptionLocstring`. Send via
    `NetMessages.Send(announcement, RecipientFilter.All)`. **csproj
    needs no `Google.Protobuf` reference** — the proto type is
    transitively in `DeadworksManaged.Api`. Fixed-in-code pattern same
    as `TagPlugin.cs:342-346`.
  - **Round cycling.** `RoundLength = 20` waves → 30s intermission →
    auto-rearm. Bounds `_waveNum`, catch-up gold, and bounty. Player
    progression (items/AP/gold) persists across rounds.
  - **Scheduler timer tracking.** Every scheduled action stored in
    `IHandle` fields (`_pendingWaveTimer`, `_pendingBurstEnd`).
    `DisarmWaves` cancels both. Solves the rapid-join/leave stacked-
    timer bug that produced back-to-back waves without this discipline.
  - **Match-clock anchor code deleted.** Per-tick rewrite of
    `m_flGameStartTime` + 4 companion fields + `m_eGameState` is
    redundant — engine extrapolates from the anchor indefinitely and
    runs its own HUD clock just fine. `OnGameoverMsg` + `OnRoundEnd`
    returning `HookResult.Stop` is sufficient to hold the mode in-
    progress. Removed ~30 lines of code and 6 schema accessors.
  - **Event-driven empty-server cleanup.** Moved from a 5s polling
    `Timer.Every` to the natural moment in `OnClientDisconnect` when
    the leaver was the last human. Full reset at that point:
    `DisarmWaves` + `_roundNum=1` + `_modeOver=false` +
    `_starterGoldSeeded.Clear()`.
  - **`UnlockFlexSlots` per-player-join, not at startup.** Boot-time
    call no-ops because `CCitadelTeam` entities don't exist on an empty
    map. Per-join call via `Timer.Once(1.Seconds()).CancelOnMapChange()`
    is idempotent (guards on current bit state).
  - **Strict enemy-team filter.** Trooper-tracking branch in
    `OnEntitySpawned` now checks `TeamNum == EnemyTeam (3)` exactly,
    not `!= HumanTeam`. Neutral-team NPCs bypass.
  - **Player-scaled trooper cap.** `80 (1p) → 600 (32p)` linear,
    replacing the flat `MaxAliveEnemyTroopers = 200`.
  - **Wave-count-scaled catch-up gold.** Seed = `2500 + max(0, _waveNum - 1) * 500`
    so a late joiner mid-round 20 gets 11,500 gold instead of 2,500.
- **Surprises:**
  - `AnchorMatchClock` turned out to be unnecessary entirely. Earlier
    sessions treated the per-tick rewrite as load-bearing "anchor the
    HUD clock" code; in practice letting the engine run its native HUD
    clock with only `OnGameoverMsg` suppression works identically.
- **Contradictions flagged:** none.

## [2026-04-22] — TrooperInvasion gameplay overhaul + operational learnings

- **Operation:** ingest (session learnings)
- **Source:** four new raw notes captured inline during a single iteration
  session that took TrooperInvasion from god-mode scaffold to real PvE loop
  while hitting three native-crash classes:
  - `raw/notes/2026-04-22-host-api-version-skew.md`
  - `raw/notes/2026-04-22-trooper-convar-runtime-mutation.md`
  - `raw/notes/2026-04-22-trooper-squad-size-cap.md`
  - `raw/notes/2026-04-22-onentityspawned-remove-deferral.md`
  - `raw/notes/2026-04-22-trooper-invasion-gameplay-overhaul.md`
- **Pages updated:** `plugins/trooper-invasion.md` substantially rewritten
  (scheduler, pulse-vs-squad-size wave volume, friendly-trooper culling
  with deferred Remove, progression loop, `Server.ExecuteCommand` rule,
  host/Api skew). `index.md` header note bumped.
- **Pages created:** none (all learnings folded into the existing plugin
  page where they apply most directly).
- **Key findings:**
  - **Host/Api version skew crashes every `[Command]` invocation.** The
    deployed `managed/DeadworksManaged.dll` (host) and
    `DeadworksManaged.Api.dll` (api) are independently built and must
    match. Skew → `MissingMethodException` from
    `PluginLoader.DispatchChatMessage` on the first `!cmd`. Affects every
    `[Command]`-using plugin, not just the one you're developing. Recovery
    = `dotnet build` the local `deadworks/managed/DeadworksManaged.csproj`
    and copy the full `bin/Debug/net10.0/` (host + ~14 deps like
    `Microsoft.Extensions.Logging.Abstractions.dll`) into the game's
    `managed/` folder.
  - **`strings -e l -n 12 dump.mdmp` recovers UTF-16 managed exception
    messages from Windows minidumps** — default `strings` is ASCII-only
    and misses them. This trick pinpointed the
    `ChatCommandContext..ctor` signature mismatch without WinDbg.
  - **Runtime ConVar mutation rule:** for *mid-frame* convar writes from
    chat-command handlers or Timer callbacks, use
    `Server.ExecuteCommand("name value")`. Direct
    `ConVar.Find().SetInt/SetFloat` crashed natively on trooper
    `spawn_interval_*`, `max_per_lane`, and re-toggling `spawn_enabled`
    0→1. `ConVar.Find().Set*` is reserved for `OnStartupServer`
    (pre-subsystem-init). C# `try/catch` doesn't see these crashes —
    they're below the managed boundary.
  - **Engine caps `citadel_trooper_squad_size` at 8.** Higher values are
    silently accepted by the convar but produce `Squad … is too big!!!
    Replacing last member` spew on every pulse. Horde volume must come
    from `pulses × lanes × squad`, not squad size.
  - **`OnEntitySpawned` direct `Remove()` is unsafe at horde scale.** The
    TagPlugin idiom (sync `args.Entity.Remove()` from the hook) crashed
    with native AV during heavy trooper spawn cascades. Canonical fix:
    capture `EntityIndex`, defer via `Timer.Once(1.Ticks(), () =>
    CBaseEntity.FromIndex(idx)?.Remove())`. Compounded by an aggressive
    `citadel_trooper_max_per_lane=2048` we had set; dropped to 256 too.
  - **Slot-from-pawn pattern:** `pawn.Controller?.Slot` —
    `CBasePlayerPawn.Controller` is a schema accessor on `m_hController`;
    `CBasePlayerController.Slot => EntityIndex - 1`. Useful for per-slot
    state (we used it for a `HashSet<int>` tracking one-time starter-gold
    seeding per player).
  - **Progression design:** 2500-gold one-time starter stipend (seeded
    per slot, cleared on disconnect), no max-upgraded abilities, no spawn
    protection, `citadel_trooper_gold_reward = 120 + wave×15` (50 % above
    vanilla).
  - **Wave scheduler design:** auto-arm on first player join, auto-pause
    on last disconnect (filter the disconnecting EntityIndex from live
    Players count — the registry still holds them at OnClientDisconnect).
    Wave interval linearly interpolates 20s (1p) → 5s (32p). First three
    waves ramp burst 1.5s → 4s for onboarding.
- **Surprises:**
  - The [[examples-index|TagPlugin]] `OnEntitySpawned.Remove()` pattern
    documented as canonical is **only safe for low-volume one-shot map
    cleanup**. For per-spawn filters during a horde, always defer.
  - The host/Api skew can persist silently because plugin *loading* still
    works — only first `[Command]` dispatch triggers it. `OnLoad` logs
    fine, which masks the problem.
- **Contradictions flagged:** none new. The [[events-surface]] page says
  `OnEntitySpawned` is "safe to modify" — that remains true for single
  entities, but should eventually grow a caveat about horde-scale
  synchronous `Remove()`. Not fixed in this ingest to avoid overreach on
  a conclusion from a single plugin's crash.

## [2026-04-22] — scaffold TrooperInvasion plugin

- **Operation:** ingest (new-plugin scaffold)
- **Source:** `raw/notes/2026-04-22-trooper-invasion-scaffold.md` — captured
  inline while scaffolding a new PvE co-op gamemode plugin. Covers all
  non-obvious design choices made during scaffold.
- **Pages created:** `plugins/trooper-invasion.md`.
- **Pages updated:** `index.md` (new plugin entry, count 27 → 28).
- **Files created in repo:**
  - `TrooperInvasion/TrooperInvasion.cs` (~260 LOC)
  - `TrooperInvasion/TrooperInvasion.csproj` (triple-mode reference pattern)
  - `TrooperInvasion/Properties/launchSettings.json`
- **Files updated in repo:**
  - `gamemodes.json` — added `"trooper-invasion": ["StatusPoker", "TrooperInvasion"]`
  - `docker-compose.yml` — added `trooper-invasion` service on port 27018
    plus `gamedata-trooper-invasion` and `compatdata-trooper-invasion` volumes
  - `.github/workflows/docker-gamemodes.yml` — added `TrooperInvasion`
    paths-filter stanza
  - `.github/workflows/build-plugins.yml` — added `TrooperInvasion`
    paths-filter stanza
- **Key design choices (captured in the wiki page):**
  - All human players forced to team 2 via `controller.ChangeTeam(2)` in
    `OnClientFullConnect`; team 3 is NPC-only, providing the PvE enemy via
    engine-spawned Sapphire-side troopers/guardians/walkers.
  - Map NPCs intentionally **not** stripped (opposite of [[deathmatch]])
    — the existing Sapphire NPCs are the gameplay content.
  - `citadel_trooper_spawn_enabled` and `citadel_npc_spawn_enabled` forced
    to `1` — opposite of [[examples-index|Tag]] and Deathmatch, both of
    which disable them.
  - Commands use v0.4.5 `[Command]` attribute (not legacy `[ChatCommand]`),
    matching LockTimer's post-6ace83c state.
  - Deathmatch's lane rotation, walker spawn capture, cooldown scaling,
    per-round kill tracking, and rank-based balancing all deliberately
    skipped as team-vs-team concepts with no PvE analog. The HUD clock
    anchor tick-write (`m_flGameStartTime` + 4 friends in lockstep) is
    kept — needed for any mode that wants `EGameState.GameInProgress`
    held indefinitely.
  - No `Google.Protobuf` package reference in the csproj since the
    scaffold doesn't call `NetMessages.Send<T>`. Flagged in the wiki page
    as a follow-on if HUD announcements are added.
  - Both CI workflows needed `paths-filter` updates; neither workflow
    auto-discovers new plugin dirs (build-plugins.yml does discover
    `*.csproj` at matrix-compute time but its upstream paths-filter
    determines WHETHER matrix compute runs in the first place).
- **Surprises:** none structural — scaffold followed existing patterns.
- **Contradictions flagged:** none.

## [2026-04-22] — ingest deadworks API surface & examples scan

- **Operation:** ingest (bulk)
- **Source:** [[deadworks-scan-2026-04-22]] — deep scan of the sibling
  `../deadworks/` repo, captured as 10 raw notes under
  `raw/notes/2026-04-22-deadworks-*.md`. Covered areas not previously
  on the wiki: plugin-facing API surface (`DeadworksManaged.Api/`),
  host-side dispatcher partials, source generator, 11 example plugins,
  and native C++ tree layout.
- **Pages created (10):**
  - `sources/deadworks-scan-2026-04-22.md`
  - `concepts/plugin-api-surface.md`
  - `entities/command-attribute.md`
  - `entities/timer-api.md`
  - `entities/events-surface.md`
  - `entities/schema-accessors.md`
  - `entities/netmessages-api.md`
  - `entities/plugin-config.md`
  - `entities/gameevent-source-generator.md`
  - `plugins/examples-index.md`
- **Pages updated:**
  - `concepts/deadworks-runtime.md` — added `related:` cross-links to new
    API pages; reconciled the `[ChatCommand]` bare-name convention (the
    dispatcher strips both `/` and `!` prefixes before lookup, so
    `[ChatCommand("zones")]` handles both surfaces — NOT a latent bug);
    added pointer block near the API section.
  - `operations/docker-build.md` — added the native-layout note as a
    source (content already covered the build-native.sh gotcha and the
    clang-cl C++23 flag quirk).
  - `index.md` — added all new pages; bumped counts (27 total).
- **Key findings (deep-scan specifics):**
  - **`[Command]` attribute machinery.** `CommandBinder` reflectively
    builds a Plan of 4 slot kinds (Caller/RawArgs/Typed/Params).
    Caller nullability annotation matters:
    `CCitadelPlayerController?` accepts null caller (server console);
    non-nullable with null caller → silent skip. `CommandConverters`
    lets plugins register custom type parsers from `OnLoad`.
    `CommandTokenizer` honours `\"` and `\\` escapes inside double
    quotes.
  - **Timer Sequence semantics.** `IStep.Run` starts at 1 (not 0).
    `Pace` is abstract with internal `WaitPace`/`DonePace` — plugins
    return `step.Wait(...)` / `step.Done()`. `IHandle.CancelOnMapChange()`
    hooks `OnStartupServer`. **Framework auto-cancels plugin timers on
    unload** — `OnUnload` cleanup is defensive, not required.
  - **`NetMessageRegistry` is RUNTIME reflection, not source
    generation.** Build-time protobuf descriptor scan + enum-name-based
    mapping rules (`k_EUserMsg_Foo` → `CCitadelUserMsg_Foo`, 12 enum
    types mapped). Manual override via `RegisterManual<T>`.
  - **`EntityData<T>` is keyed by `uint EntityHandle`**, not pointer.
    Handles carry a generation counter; auto-cleanup via
    `EntityDataRegistry` on entity delete (weak-reference registry).
  - **Config key = plugin C# class name**, NOT folder name
    (distinct from `gamemodes.json` which uses the folder name).
    `configs/<ClassName>/<ClassName>.jsonc`. First-load auto-creates
    a defaulted file with a `// Configuration for …` header.
    `IConfig` marker is optional; `Validate()` clamp-in-place is the
    idiomatic pattern.
  - **`HookResult` aggregation across plugins is MAX-wins**. All
    plugins always see every event — no plugin can short-circuit
    dispatch to others.
  - **`AbilityAttemptEvent` is mask-based**, not boolean — plugins mutate
    `BlockedButtons`/`ForcedButtons` (OR'd across plugins).
  - **`CheckTransmitEvent.Hide`** clears a bit in a native `ulong*`
    transmit bitmap; must be re-applied every tick.
  - **GameEventSourceGenerator** sorts `.gameevents` files
    alphabetically; `core.gameevents` (c) < `game.gameevents` (g) so
    `game` overrides `core` for duplicates. Field types `long`/`short`/
    `byte` all narrow to C# `int` (no `GetLong` accessor).
  - **`Players.MaxSlot = 31`** but RecipientFilter/CheckTransmit bitmaps
    iterate 64 bits. 31 is the documented player slot cap; underlying
    infrastructure handles 64.
  - **`SchemaAccessor<T>.Set` auto-calls `NotifyStateChanged`** if the
    field was networked at resolve-time. Plugins don't manually fire the
    notification — accessor does it.
  - **Chat command dispatcher strips prefix before lookup.**
    `PluginLoader.ChatCommands.cs:14-47` — so `[ChatCommand("foo")]`
    handles both `/foo` and `!foo`, while `[ChatCommand("!foo")]`
    handles only `!foo`. **Reconciles the contradiction flagged in the
    previous log entry** about LockTimer's bare-name registrations.
- **Example plugin patterns catalogued (11 plugins):**
  - AutoRestart — countdown timer sequences with `CancelOnMapChange`
  - ChatRelay — outgoing net message hook with rebroadcast re-entrance
    guard
  - Dumper — admin ConVar/ConCommand enumeration dump
  - ExampleTimer — full ITimer tour
  - ItemRotation — complex nested config + game-mode state machine
  - ItemTest — v0.4.5 `AddItem(enhanced)`, `params string[]` commands,
    PrintToConsole
  - RollTheDice — ParticleSystem builder, KeyValues3+AddModifier,
    nested timers, per-pawn state
  - Scourge — `OnTakeDamage` ability filter, DOT sequence with
    `EntityData<IHandle>`
  - SetModel — minimal precache + SetModel
  - Tag — full game-mode with batch ConVar setup, team assignment,
    mask-based ability blocking
- **Surprises:**
  - The `[ChatCommand]` convention reconciliation — prior log entry's
    contradiction flag was incorrect. The dispatcher's prefix-stripping
    behaviour makes bare-name registrations valid.
  - `NetMessageRegistry` runtime reflection — prior scan summary
    incorrectly claimed this was a source generator.
  - `EntityData<T>` uses handles (with generation counters), not raw
    entity references — safer than pointer-based keys across map
    changes.
  - `GameEventSourceGenerator` narrows `long`/`short`/`byte` to `int`
    — a 64-bit event field declared as `long` is silently lossy; use
    `uint64` for full precision.
- **Contradictions flagged:**
  - Prior log's claim that `[ChatCommand("zones")]` is a latent bug —
    reconciled. It's valid; the dispatcher strips prefixes.
  - Prior scan summary's source-generator claim for NetMessageRegistry
    — corrected: it's runtime reflection.

## [2026-04-22] — ingest deadworks v0.4.5 release notes

- **Operation:** ingest
- **Source:** [[deadworks-0.4.5-release]] — user-provided summary of the
  upcoming v0.4.5 release at
  `https://github.com/Deadworks-net/deadworks/releases`, captured at
  `raw/articles/deadworks-0.4.5-release.md`.
- **Pages created:** `sources/deadworks-0.4.5-release.md`.
- **Pages updated:** `concepts/deadworks-runtime.md` (new v0.4.5 section;
  attribute list rewritten for `[Command]` + deprecations; port note
  updated), `glossary.md` (added `[Command]` and `Slot` entries),
  `index.md` (added source, bumped counts).
- **Key findings:**
  - **New `[Command]` attribute** is the unified command API. Single
    `[Command("heal")]` registers `dw_heal` console concommand + `/heal`
    chat slash + `!heal` chat bang, all at once. Handler signature drops
    `ChatCommandContext`/`HookResult` — just
    `(CCitadelPlayerController caller, <typed args>)` returning `void`,
    with host-side arg parsing.
  - **`[ChatCommand]` and `[ConCommand]` are deprecated** and will be
    removed. All three plugins in this repo currently use
    `[ChatCommand]` and will need migration before removal.
  - **`CBasePlayerController.Slot`** is now the canonical way to get a
    player slot, replacing the widely-used `controller.EntityIndex - 1`
    idiom (including the mapping inside `Chat.PrintToChat`).
  - **`CCitadelPlayerPawn.AddItem`** gains `bool enhanced = false` to
    grant enhanced items; `HeroID` now exposed directly on the pawn.
  - **`CBasePlayerController.PrintToConsole`** was broken prior to v0.4.5
    and is now fixed. Any plugin that avoided it because it silently did
    nothing can now re-adopt it.
  - **Soundevents can now be sent directly to a single player** via a
    new API (scoped, not the default broadcast path).
  - **Default port reverted to `27067`** to avoid conflicts with the game
    client — partially resolves the 27067 vs 27015 inconsistency noted in
    the previous log entry (deadworks-side aligned; the Docker compose
    flow in this repo is unaffected because it sets `SERVER_PORT`
    explicitly).
- **Cross-cutting implications flagged:**
  - LockTimer's bare-name `[ChatCommand("zones")]` inconsistency with the
    `!`-prefix convention becomes moot on migration to `[Command]` (which
    registers both `/` and `!` automatically). Revisit at migration time.
  - All three plugins need a `[ChatCommand]` → `[Command]` migration
    pass before the old attribute is removed upstream.
- **Surprises:** none beyond the content above.
- **Contradictions flagged:** none.

## [2026-04-21] — bulk ingest of session extracts

- **Operation:** bulk-ingest
- **Source:** [[session-extracts-2026-04-21]] — ~61 raw notes under
  `knowledge-base/raw/notes/sessions-2026-04-21/`, derived from Claude Code
  JSONL transcripts across four sibling project dirs (server-plugins,
  deathmatch, deadworks, custom-server).
- **Pages created:**
  - `sources/session-extracts-2026-04-21.md`
  - `concepts/source-2-engine.md`
  - `concepts/deadlock-game.md`
  - `concepts/deadworks-runtime.md`
  - `concepts/plugin-build-pipeline.md`
  - `entities/deadworks-sourcesdk.md`
  - `entities/deadworks-mem-jsonc.md`
  - `entities/deadworks-plugin-loader.md`
  - `entities/protobuf-pipeline.md`
  - `plugins/deathmatch.md`
  - `plugins/lock-timer.md`
  - `plugins/status-poker.md`
  - `operations/docker-build.md`
  - `operations/proton-runtime.md`
- **Pages updated:** `index.md`, `glossary.md`.
- **Key findings (cross-cutting):**
  - Deadworks is a **replacement entry point**, not an injected DLL:
    `deadworks.exe` coexists with `deadlock.exe` in `game/bin/win64/`, loads
    `engine2.dll`, installs ~30 safetyhook inline trampolines (driven by
    `config/deadworks_mem.jsonc`), then calls the engine's `Source2Main`
    with `"citadel"` as the game name. Managed .NET layer boots via
    nethost → hostfxr → .NET 10 (Windows runtime installed inside the
    Wine prefix).
  - Plugin csprojs in this repo support **three mutually-exclusive
    reference modes** for `DeadworksManaged.Api`: `DeadlockDir` HintPath
    (for `build-plugins` CI), sibling `ProjectReference` (for local dev),
    and bare `<Reference>` fallback (for Docker, resolved via an
    auto-generated `Directory.Build.targets` injecting `/artifacts/managed`
    into `AssemblySearchPaths`). Commit arc: `da1edf3` → `59b6e96` →
    `2648aa6`.
  - **Plugins using `NetMessages.Send<T>` (protobuf) need a direct
    `Google.Protobuf` PackageReference** in their csproj for Docker CI
    — local dev resolves transitively, Docker CI links against the
    published `DeadworksManaged.Api.dll` which does NOT ship
    `Google.Protobuf.dll`. Symptom: CS0311 + cascade of misleading
    CS0246 errors.
  - **Schema vtable calls crash under Wine/Proton**; the authoritative
    access pattern is "scan-first-then-vtable" — binary-scan `server.dll`
    for the class-name string, find the `SchemaClassInfoData_t` by
    pointer-equality, iterate the fields array. `__m_pChainEntity`
    lookup crashes the schema system outright.
  - HUD match clock requires writing FIVE `CCitadelGameRules` fields
    in lockstep per tick: `m_flGameStartTime`, `m_fLevelStartTime`,
    `m_flRoundStartTime`, `m_flMatchClockAtLastUpdate`,
    `m_nMatchClockUpdateTick`. Missing any leaves the client
    extrapolating past the server writes.
  - Flex-slot unlock requires **dual writes**: the `m_bFlexSlotsForcedUnlocked`
    bool on `CCitadelGameRules` AND the `m_nFlexSlotsUnlocked` short
    bitmask (set to `0xF`) on every `CCitadelTeam` entity, re-applied at
    multiple lifecycle points because teams may not have networked
    initial state by the first write.
  - `gamemodes.json` keys match on-disk **plugin folder names**, NOT
    csproj `AssemblyName`. Renaming `DeathmatchPlugin/` → `Deathmatch/`
    requires updating the JSON or the image ships with no plugin.
- **Surprises:**
  - `-netconport` opens a **plain-text TCP console**, not Source 1 binary
    RCON. Unauthenticated. `rcon_password` does not apply. Full binary
    Source 2 RCON is a separate mechanism with a one-packet auth handshake
    (Source 1 sent two).
  - `controller.ChangeTeam(int)` bypasses both the team-picker prompt AND
    the `changeteam`/`jointeam` client concommand hook — blocking
    concommands does NOT disable the server-side auto-balancer.
  - `deadlock.exe` does NOT import `version.dll`. Any DLL-proxy injection
    using `version.dll` silently never loads.
  - `WINEDLLOVERRIDES` set in the outer entrypoint shell is silently
    clobbered by the inner `gosu steam bash -c` block. Same for
    `WINEDEBUG`. Overrides must live inside the gosu subshell.
  - Timer is an **instance** property on `DeadworksPluginBase`, not static.
    Plugin helpers that need `Timer` cannot be `static` (CS0120).
- **Contradictions flagged (newer supersedes):**
  - Entity chunk count: older Rust-era notes said `NUM_CHUNKS=32`, newer
    Deadlock-era notes correct to 64 (32 non-networkable + 32 networkable).
    **Use 64.**
  - `CEntityIdentity` stride: some CS2 refs gave 0x78 (120 bytes);
    Deadlock is **0x70 (112 bytes)**.
  - Deadlock app id: a few older notes mention `1422460` as a separate
    dedicated-server app id; all current server infrastructure (compat
    dir, `app_update`, `steam_appid.txt`) uses **`1422450`**.
  - Protobuf pipeline: three eras (vendored → fork's auto-update →
    build-time via sourcesdk protoc). Commit `a92d744` is the cutover;
    the fork's `148cfaa`/`d7d9675` update-protos commits are now
    semantically obsolete.
  - LockTimer's `[ChatCommand("zones")]` etc. register bare names without
    the `!` prefix — diverges from host convention (and the plugin's own
    `docs/plan.md` uses `!` prefix). Likely latent bug; not reconciled.
  - Deadworks default local-dev port is `27067` (README), but Docker
    flow uses `27015`. Documentation inconsistency noted.

## [2026-04-21] — bootstrap

- **Operation:** bootstrap
- **Source:** none
- **Pages created:** `index.md`, `log.md`, `overview.md`, `glossary.md`
- **Pages updated:** —
- **Key findings:** Wiki scaffolded per Karpathy's LLM Wiki pattern, tailored
  to the `deadlock-server-plugins` repo (three C# plugins — DeathmatchPlugin,
  LockTimer, StatusPoker — built into Docker images via CI).
- **Next:** first ingest of the repo READMEs and plugin source as initial content.
