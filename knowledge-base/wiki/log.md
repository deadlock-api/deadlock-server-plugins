---
title: Operation Log
type: log
---

# Operation Log

Append-only. Newest entries on top. Every ingest, query-that-wrote-a-page,
and lint run gets an entry.

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
