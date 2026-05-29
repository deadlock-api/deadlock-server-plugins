---
date: 2026-05-29
task: Make tier1 lane Guardians appear in TrooperInvasion (final working solution)
files:
  - TrooperInvasion/TrooperInvasion.Guardians.cs
  - TrooperInvasion/TrooperInvasion.Troopers.cs
  - TrooperInvasion/TrooperInvasion.Waves.cs
  - TrooperInvasion/TrooperInvasion.cs
---

Resolves the long-running "Guardians don't spawn" question. **Supersedes the
mode/state conclusions in `2026-05-29-dl-midtown-tier1-guardians-mode-invalid-proof.md`
and `2026-05-04-dl-midtown-no-tier1-guardians.md`** (which concluded tier1 is simply
absent from dl_midtown — that was incomplete).

## What's actually true (verified, build 6536)

- `npc_boss_tier1` (Guardian) is a live class in server.dll (npc_boss_tier1.cpp,
  `CNPC_Boss_Tier1_GraphController`, `citadel_t1_boss_*` convars, objective slots
  `k_eCitadelObjective_TeamN_Tier1_Lane1..4`).
- dl_midtown's entity lump (`maps/dl_midtown/entities/default_ents.vents_c`, dumped via
  ValveResourceFormat `-b DATA`) is **authored for Guardians but does not place them**:
  it has the per-lane tier1 **shops** (only tier1 has shops), zipline nodes, and
  `guard_boss_name`/`secondary_guard_boss_name`/`tertiary_guard_boss_name` keys all
  referencing `boss_{combine,rebel}_t1_{blue,yellow,purple}` (3 lanes x 2 teams = 6).
  tier2/tier3/barrack ARE placed as entities; tier1 is not, and there are no tier1
  spawn anchors.
- => Guardians are spawned by a matchmaking/objective-init code path that the
  `-insecure -allow_no_lobby_connect` server never runs. Confirmed NOT triggerable via:
  pinning `m_eGameState=GameInProgress` (engine reaches it on its own anyway), or setting
  `game_mode=Normal`/`match_mode=Unranked` (set them — reads back Normal/Unranked — still
  zero tier1). The earlier "modes are already Normal" claim was wrong; they default to
  **Invalid**, but fixing that does not spawn Guardians.

## Working solution — self-spawn

Spawn the 6 ourselves with the managed entity API at the authored lane positions (taken
from the `guard_boss_name` zipline-node origins). This works and does NOT crash (unlike
the old `npc_trooper_boss` null-KV boss-wave attempt):

```csharp
var ekv = new CEntityKeyValues();
ekv.SetString("targetname", bossName);     // e.g. boss_rebel_t1_yellow
ekv.SetString("BossName", bossName);
ekv.SetString("subclass_name", "npc_boss_tier1");
ekv.SetInt("teamnumber", team);            // 2=Amber/rebels, 3=Sapphire/combine
ekv.SetInt("LaneNum", lane);               // 1=Yellow, 4=Blue, 6=Purple
ekv.SetVector("origin", pos);
ekv.SetVector("angles", Vector3.Zero);
var g = CBaseEntity.CreateByDesignerName("npc_boss_tier1");
g.Spawn(ekv); g.Teleport(pos); g.TeamNum = team;
```

`subclass_name="npc_boss_tier1"` is the load-bearing KV (VData). Spawns alive at
5500 HP, settles to ground, behaves as a defensive boss. Driven from `ArmWaves`
(MaybeSpawnGuardians, once per map) — NOT a startup timer, because timers don't tick
while the server hibernates with no players.

## The critical gotcha — self-cull

A spawned `npc_boss_tier1` reports **`DesignerName == "npc_trooper_boss"`**. So
TrooperInvasion's own `OnEntitySpawned` trooper handling caught them: the team-2
(friendly) ones hit the `TeamNum == HumanTeam` cull → `Remove()` within <0.5s; the
team-3 ones hit the enemy path → HP-scaled (5500->8800) and counted toward the wave cap.
Symptom seen live: "enemy guardians spawn, my own don't."

Fix: track the indices of Guardians we spawn in `_guardianIndices` (added BEFORE
`Spawn()`, since OnEntitySpawned fires synchronously during Spawn) and early-return for
them at the top of `OnEntitySpawned`.

## Files

- `TrooperInvasion.Guardians.cs` (new) — GuardianSpecs, `_guardianIndices`,
  `EnsureNormalMatchMode`, `ResetGuardians`, `MaybeSpawnGuardians`, `SpawnGuardian`.
- `TrooperInvasion.Troopers.cs` — `OnEntitySpawned` exempts `_guardianIndices`.
- `TrooperInvasion.Waves.cs` — `ArmWaves` calls `MaybeSpawnGuardians`.
- `TrooperInvasion.cs` — `OnStartupServer` calls `EnsureNormalMatchMode` + `ResetGuardians`.

## Open / untested

- `EnsureNormalMatchMode` is kept because Guardians were validated with it on; not proven
  required. Easy to drop if it perturbs the Invalid-mode-balanced PvE economy.
- Both teams' Guardians are spawned (enemy ones give the humans push objectives). Tier1
  shops exist at the friendly positions but shop wiring/objective registration of the
  self-spawned boss is unverified (it isn't in the engine objective table — see
  `citadel_bot_list_objectives_ent`).
- Guardians spawn once per map; destroyed ones don't respawn until changelevel.
