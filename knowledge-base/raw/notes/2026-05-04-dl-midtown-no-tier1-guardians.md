---
date: 2026-05-04
task: Diagnose why tier1 Guardians never spawn in TrooperInvasion
files:
  - TrooperInvasion/TrooperInvasion.cs
  - Deathmatch/Deathmatch.cs
---

`dl_midtown` does not contain `npc_boss_tier1` (Guardians) entities — and
**not because they're conditionally spawned**. The map ships without any
tier1-related content of any kind. Verified via runtime entity-list dumps
at multiple snapshots:

- 2× `npc_boss_tier3` (Base Guardian / Shrine — 1 per team, at y=±8048)
- 6× `npc_boss_tier2` (Walker — 3 per team at lane positions y=±3200/4736/5024)
- 12× `npc_barrack_boss` (Patron + flanking barracks — 6 per team at y=±5454/6396/6756/6788)
- **0× `npc_boss_tier1`** (and 0 `alt_npc_boss_tier1`)
- **0× any classname containing `tier1` / `guardian` / `tower`**
- 353× `info_*spawn*` entities, but only 4 distinct types — all troopers:
  `info_neutral_trooper_spawn`, `info_super_trooper_spawn`,
  `info_team_spawn`, `info_trooper_spawn`. **No tier1 spawn anchors.**

## Hypotheses ruled out (all five)

1. **`m_eGameState` pin gates tier1 spawn** — wrong. Removing the pin
   (`5890263`) didn't bring them; the engine reaches `GameInProgress` on
   its own.
2. **`citadel_npc_spawn_enabled=1` needed** — wrong. Setting it had no
   effect; reverted.
3. **dl_midtown has tier1 but engine isn't triggering them** — wrong.
   `tier1Like=0` across multiple time snapshots; no anchors either.
4. **`m_eGameMode`/`m_eMatchMode = Invalid` gates tier1 spawn** — wrong.
   Diag showed mode was already `Normal/Unranked` at startup before any
   write; tier1 still 0. Both convar-set (`Server.ExecuteCommand`) and
   schema-set (`SchemaAccessor.Set`) succeeded but were no-ops because
   the value was already correct.
5. **`alt_npc_boss_tier1` placeholder swap mechanism** — wrong.
   `altLike=0`, no alt-classed tier1 entities on the map.

## Likely real explanation — community/UI terminology

Players and community refer to **any lane defensive tower as "Guardian."**
On the legacy 4-lane map the first lane tower was `npc_boss_tier1` and
literally called Guardian; behind it sat `npc_boss_tier2` Walker. On
`dl_midtown` (the current 3-lane Midtown layout) there is no tier1 — the
only lane tower is the tier2 Walker, which the community still calls
"Guardian" out of habit. So when a user reports "vanilla has Guardians,"
they're seeing the same tier2 Walker entities our server already shows.

## Implications

- `Deathmatch.cs:20` listing `npc_boss_tier1` in `MapNpcsToRemove` is
  **dead code on `dl_midtown`** — kept harmless as defensive code for any
  future legacy-style map.
- TrooperInvasion's defense objectives on this map are: 6 Walkers
  (tier2) + 2 Base Shrines (tier3) + 12 Patron buildings (`npc_barrack_boss`).
  No Guardian phase exists to be missing.
- The original commit `a246595` ("pin m_eGameState so Guardians spawn")
  was a misdiagnosis. Pinning `m_eGameState` does nothing for entity
  placement. Removed in `5890263`.
- Follow-up `f636857` (pin earlier) wrong for the same reason.
- Mode-set attempt `3c68720` / `23abc4e` reverted in this session — the
  modes were already correct.

## Diagnostic technique worth keeping

`Entities.All` iteration at multiple `Timer.Once` snapshots is the
cheapest way to answer "what entities exist on this map at time T?" —
beats grepping `server.dll` strings or trying to derive map content from
binary artifacts. Bucket counts + first N distinct-name examples per
bucket gives a useful summary without flooding the log.

`OnStartupServer` fires twice on cold boot: once at engine init (empty
entity list, total=0 in a +0s dump) and again after the map loads
(~2 minutes later in our case, when all map entities are present). The
first snapshot is misleading; +5s post-second-startup is the first real
reading.

## Schema-write of game/match mode is a no-op when already set

`Server.ExecuteCommand("game_mode 1")` plus `sv_cheats 1`/`0` bracket
appears to silently fail to change the value when the convar is read-only
or the engine has already committed the mode. **Schema-writing the
underlying field via `SchemaAccessor.Set` succeeds**, but only changes
the displayed value — it does not retroactively trigger any spawn logic
the engine may have keyed off the original value.

Engine's mode-selection on plugin-hosted servers defaults to
`gameMode=Normal`/`matchMode=Unranked` automatically — no plugin work
required for that. The earlier "default mode is Invalid" assumption was
wrong.
