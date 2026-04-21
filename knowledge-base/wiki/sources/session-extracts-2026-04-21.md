---
title: Session extracts — 2026-04-21 bulk ingest
type: source-summary
sources:
  - knowledge-base/raw/notes/sessions-2026-04-21/
related:
  - "[[source-2-engine]]"
  - "[[deadlock-game]]"
  - "[[deadworks-runtime]]"
  - "[[plugin-build-pipeline]]"
created: 2026-04-21
updated: 2026-04-21
confidence: high
---

# Session extracts — 2026-04-21

A batch of ~61 raw notes produced by scraping prior Claude Code session
transcripts across four sibling project directories. Each note is a <800-word
extract capturing one session's concrete findings about Source 2, Deadlock,
the Deadworks plugin runtime, or the Docker/Proton build-and-deploy pipeline.

## Provenance

Notes were derived from Claude Code session JSONL files spread across four
working repos on the user's laptop:

- `deadlock-server-plugins/` — this C#/.NET plugins monorepo (12 sessions)
- `deadlock-deathmatch/` — the standalone Deathmatch plugin repo (11 sessions)
- `deadworks/` — the Deadworks plugin host / runtime (28 sessions)
- `deadlock-custom-server/` — Rust RCON/injector stack, sibling project (7 sessions)

Sessions cluster around April 2026 but include a few older (March 2026)
Rust-era sessions whose findings were retained as negative results (things
that do NOT work under Wine/Proton) and for Source 2 / schema internals that
still apply.

## Breadth of topics

- **Source 2 engine internals**: entity system layout (`CGameEntitySystem`,
  chunks, `CEntityIdentity`), schema system access patterns (RE vtable indices,
  scan-first fallback), `CreateInterface` discovery, pause / HUD clock / game
  state ConVars and networked fields, `-netconport` plain-text console vs
  binary RCON, SourceTV / broadcast split.
- **Deadlock specifics**: Steam app ID 1422450, `dl_midtown` default map,
  `citadel_*` ConVars, `CCitadelGameRules` offsets and fields, hero/team/lane
  enums, walker (`npc_boss_tier2`) and other NPC classnames, flex-slot
  mechanics requiring dual-field writes.
- **Deadworks runtime**: `deadworks.exe` as a replacement entrypoint (loads
  `engine2.dll` then calls `Source2Main`), C# plugin host via nethost/hostfxr,
  `PluginLoader` with collectible `AssemblyLoadContext`, shared-API pattern
  (`Private=false`), memory signature file `deadworks_mem.jsonc`, protobuf
  pipeline evolution from vendored `.pb.cc` to build-time sourcesdk protoc.
- **Build & deployment**: 3-stage Docker build (clang-cl + xwin cross-compile,
  dotnet publish, Proton runtime stage), `gamemodes.json`-driven per-mode
  image composition, `extra-plugins` BuildKit contexts, CI workflows for
  GHCR publishing, sig-validation script, update workflows.

## Overlaps and contradictions

Many notes contain identical app-ID / Steam-redist / Proton-prefix detail
because those boilerplate bits appear in every sessions's container
entrypoint. Newer sessions supersede older ones where offsets / dispatch
patterns drifted. Key contradictions flagged in `[[log]]`:

- Entity chunk count: older Rust-era notes say `NUM_CHUNKS=32`, newer notes
  correct to 64 (32 non-networkable + 32 networkable). Newer is authoritative.
- `CEntityIdentity` stride: earlier sessions said 120 bytes (0x78);
  Deadlock-era sessions corrected to 112 (0x70).
- Protobuf pipeline: vendored `.pb.cc/.pb.h` files were committed historically,
  then removed in upstream commit `a92d744` in favor of build-time generation
  via sourcesdk protoc. The fork carried auto-update scripts pointing at the
  old vendored tree; those are now obsolete.
- Entry protocol to the dedicated server's console socket: `-netconport` is
  plain-text TCP (unauthenticated), NOT Source 1 binary RCON. A separate
  Source-1-style RCON implementation exists in the `deadlock-custom-server`
  repo for admin tasks.

## Use this summary as a jump-off

The subsequent wiki pages under `concepts/`, `entities/`, `plugins/`, and
`operations/` synthesize the facts in these notes into cross-linked,
deduplicated pages. When in doubt about a claim, trace back to the specific
note file listed in a page's `sources:`.
