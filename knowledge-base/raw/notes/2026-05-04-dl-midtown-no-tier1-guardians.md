---
date: 2026-05-04
task: Diagnose why tier1 Guardians never spawn in TrooperInvasion
files:
  - TrooperInvasion/TrooperInvasion.cs
  - Deathmatch/Deathmatch.cs
---

`dl_midtown` does not contain `npc_boss_tier1` (Guardians) entities at all.
Verified by adding a startup-time diagnostic dump of every `npc_boss_*`,
`npc_barrack_boss`, and `info_npc_spawn*` entity at +5s, +30s, +60s after
`OnStartupServer`. All three snapshots returned an identical 20-entity set:

- 2× `npc_boss_tier3` (Base Guardian / Shrine — 1 per team, at y=±8048)
- 6× `npc_boss_tier2` (Walker — 3 per team at lane positions y=±3200/4736/5024)
- 12× `npc_barrack_boss` (Patron + flanking barracks — 6 per team around y=±5454/6396/6756/6788)
- **0× `npc_boss_tier1`**
- **0× `info_npc_spawn*`** (no spawn anchors for tier1 either)

In normal Deadlock, tier1 Guardians sit in front of the walkers at y ≈ ±1500.
On `dl_midtown` they're absent by design — the map ships only with walkers,
patron buildings, and the central shrine.

## Implications

- `Deathmatch.cs` `MapNpcsToRemove` lists `npc_boss_tier1` for removal
  (deathmatch line 20). This is **dead code** on `dl_midtown` — it never
  matches because tier1 entities don't exist.
- The original commit `a246595` ("pin m_eGameState so Guardians spawn") was
  based on a wrong premise. The pin couldn't have made tier1 appear; pinning
  the schema field doesn't trigger map-entity placement, and the entities
  weren't in the map to begin with.
- The follow-up `f636857` (pin earlier) was equally wrong for the same
  reason. Removed in `5890263`.
- The `citadel_npc_spawn_enabled=1` attempt (`2a60bba`) also wrong premise —
  the convar can't summon entities the map lacks. Reverted in this session.

## Two real options if Guardian-tower gameplay is wanted

1. **Manually spawn `npc_boss_tier1` ourselves** at hardcoded lane-front
   positions (e.g. y=±1500 on each lane's x). Boss-wave precedent
   (`cbc9f75`) shows lane-AI NPCs need a fully populated `CEntityKeyValues`
   with `m_iLane`, squad registration, and navmesh region — null KV crashes
   the server natively. Same crash class would apply here.
2. **Accept the map as-is** — TrooperInvasion's defense objectives are
   walkers + patron buildings only. No Guardian phase.

## Diagnostic technique worth keeping

`Entities.All` iteration at multiple Timer.Once snapshots is the cheapest
way to answer "what entities exist on this map at time T?" — beats grepping
strings of `server.dll` and trying to derive map content from binary clues.

`OnStartupServer` fires twice on cold boot: once at engine init (empty
entity list, total=0 in the +0s dump) and again after the map loads
(~2 minutes later in our case, when all map entities are present). The
"+0s" snapshot is misleading; "+5s" is the first real reading.
