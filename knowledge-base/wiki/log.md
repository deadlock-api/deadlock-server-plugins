---
title: Operation Log
type: log
---

# Operation Log

Append-only. Newest entries on top. Every ingest, query-that-wrote-a-page,
and lint run gets an entry.

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
