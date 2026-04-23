---
date: 2026-04-23
task: add boss waves to TrooperInvasion (every Nth wave spawn multiple npc_trooper_boss with bonus bounty)
files:
  - TrooperInvasion/TrooperInvasion.cs
---

## Non-obvious findings

### 1. `npc_trooper_boss` has no dedicated ConVar — manual spawn via the entity API is the only handle

The citadel ConVar surface (`citadel_trooper_*`) covers regular troopers only:
squad size, spawn interval, per-lane cap, gold reward, spawn_enabled. There is
no `citadel_trooper_boss_spawn_count` or equivalent. In vanilla Deadlock the
engine emits `npc_trooper_boss` on its own cadence as part of the trooper
progression (minute-N "super trooper" promotion); plugins have no knob to
influence that pacing.

To inject bosses at arbitrary wave boundaries we must go through the managed
entity API:

```csharp
var boss = CBaseEntity.CreateByDesignerName("npc_trooper_boss");
boss.TeamNum = EnemyTeam;          // MUST set before Spawn()
boss.Teleport(position);
boss.Spawn();
```

`CreateByDesignerName` resolves the designer name → base entity class + subclass
id hash and writes `m_nSubclassID` / `m_pSubclassVData` directly so VData is
live before `Spawn()` is queued (see `CBaseEntity.cs:51-76`). **TeamNum must
be set before Spawn** — setting it after leaves the NPC on team 0/unaffiliated
and it won't participate in lane AI or take damage normally. This also lets
our existing `OnEntitySpawned` strict-`TeamNum == EnemyTeam` filter correctly
track manually-spawned bosses into `_aliveEnemyTroopers`.

### 2. "Reuse lane infra" without hardcoding per-map coords: piggyback on live trooper positions

The engine places regular `npc_trooper` NPCs at each active lane's spawn point
when `citadel_trooper_spawn_enabled=1` and `citadel_active_lane` is set. Rather
than hardcode lane-spawn Vector3s per map, sample the positions of the regular
troopers that the engine has JUST spawned in the current burst:

```csharp
var positions = Entities.ByDesignerName("npc_trooper")
    .Where(e => e.TeamNum == EnemyTeam)
    .Select(e => e.Position)
    .Where(p => p != Vector3.Zero)
    .ToList();
```

This genuinely "reuses existing lane/wave infra" — a boss wave spawns at the
same lane corridors as the regular burst, dynamically, on any map. The catch
is timing: if we sample IMMEDIATELY after setting `citadel_trooper_spawn_enabled 1`,
the engine hasn't queued the first pulse yet → `positions` is empty. A
`Timer.Once(1.5s)` delay before sampling gives the burst time to place at
least one trooper at each active lane. Emptied-lane races are handled by a
"no lane troopers to sample, skipping" console log — not worth retrying; next
boss wave is 10 waves away.

### 3. `ECurrencySource.EBossKill` exists and is the correct source tag

The `ECurrencySource` enum (Currency.cs:16-63) has a dedicated `EBossKill = 0xd`
that maps to the client HUD's "boss kill" credit animation. Using it for
`pawn.ModifyCurrency(EGold, +bonus, EBossKill)` makes boss bounties read
differently from regular trooper souls in the kill feed — small touch but
important for the gamemode's theming (boss waves should feel distinct, not
"same trooper with extra zeros"). Also available for semantic clarity:
`ELaneTrooperKill`, `EOrbTier{1,2,3}TrooperBoss`, `EBaseSentryKill`,
`EBaseGuardians` — a much richer source taxonomy than the
`EStartingAmount`/`EPlayerKill` two-liner most plugins reach for.

### 4. Manually-spawned bosses still trip `OnEntitySpawned` — existing cap logic handles them automatically

`CreateByDesignerName + Spawn` invokes the full engine spawn pipeline, which
fires `OnEntitySpawned`. Because `_trooperDesigners = {"npc_trooper",
"npc_trooper_boss"}` already includes the boss designer, our existing
per-spawn handler counts bosses toward `_aliveEnemyTroopers` and triggers
the cap-check spawn-disable at the right threshold. **Zero new bookkeeping
required** — the boss wave piggybacks on the exact same tracking the regular
burst uses. This is the argument for keeping the `_trooperDesigners` list
unified: tracking follows the filter, not the spawn path.

### 5. `EntityKilledEvent.EntindexAttacker` → typed player pawn in one call via `FromIndex<T>`

For "who killed this boss" attribution without a manual `As<T>()` cast:

```csharp
var attackerPawn = CBaseEntity.FromIndex<CCitadelPlayerPawn>(args.EntindexAttacker);
if (attackerPawn != null && attackerPawn.TeamNum == HumanTeam) { ... }
```

`FromIndex<T>` returns null if the index doesn't resolve OR the native class
doesn't match T — so non-player killers (map kill triggers, fall damage,
environmental) cleanly no-op without a team check needed beforehand. Compare
to the untyped `FromIndex` + `As<T>` pattern used elsewhere in the file; the
typed form is shorter and equally safe.

### 6. Boss-wave detection: `waveNum % BossWaveEveryN == 0` matches "round-end wave 10"

`_waveNum` resets to 0 on every `DisarmWaves` (round boundary + session reset),
so `_waveNum == RoundLength` and `_waveNum > 0 && _waveNum % BossWaveEveryN == 0`
are equivalent given the current code. Picked the modulo form because the
task framing was "every Nth wave" — leaves room for a future split where
`BossWaveEveryN` diverges from `RoundLength` (e.g. mini-bosses every 5 waves,
big boss every 10) without touching callsites.
