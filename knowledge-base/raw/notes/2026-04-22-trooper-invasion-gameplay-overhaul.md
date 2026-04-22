---
date: 2026-04-22
task: Convert TrooperInvasion from power-fantasy scaffold to real PvE horde mode
files: [TrooperInvasion/TrooperInvasion.cs]
---

The original scaffold (2026-04-22 earlier-same-day) was essentially a god-mode
sandbox: 999 999 starting gold, all signature abilities maxed on every spawn,
3s invulnerability on respawn, engine-default trooper cadence, no wave
scheduler, no win condition progression. User iterated it into a real PvE loop
over this session. Design decisions worth persisting on the plugin page:

**Wave scheduler** — autonomous, player-count-driven. Armed on first
`OnClientFullConnect`; paused when last player disconnects (filter the
disconnecting `EntityIndex` out of `Players.GetAll()` — the registry still
contains them at the `OnClientDisconnect` hook). Wave interval interpolates
linearly from `SlowWaveIntervalSeconds = 20f` at 1 player to
`FastWaveIntervalSeconds = 5f` at 32 players. Re-computed every
`ScheduleNextWave` call, so join/leave during a session adjusts cadence live.
`!startwaves` / `!stopwaves` kept as manual overrides.

**Wave volume** — constant count per wave (user specifically *does not* want
scaling trooper count). Volume = `MaxSquadSize=8 × 4 lanes × 1s pulse ×
BurstSeconds`. First three waves ramp the burst (1.5s → 2.5s → 3.5s → 4s
plateau) so a fresh de-powered player isn't wiped on wave 1.

**Friendly-trooper culling** — no per-team spawn convar exists in Deadlock,
so any team-2 trooper that spawns is wasted (no enemy to fight). Plugin hooks
`OnEntitySpawned`, filters on `DesignerName ∈ {"npc_trooper",
"npc_trooper_boss"} && TeamNum == 2`, and defers a one-tick `Remove()`. See
`2026-04-22-onentityspawned-remove-deferral.md` for why the deferral is
load-bearing.

**Progression** — real vanilla loop:
- `StarterGold = 2500`, seeded **once per slot** (HashSet<int>
  `_starterGoldSeeded`). Respawns don't re-seed — death costs you what you
  earned. Disconnect clears the slot so a reconnect re-seeds.
- No ability-signature pre-upgrade. Players earn AP via trooper kills and
  spend through the normal upgrade UI.
- Trooper bounty `citadel_trooper_gold_reward = 120 + wave × 15`
  (50 % above vanilla, steeper per-wave to reward survival).
- Spawn protection entirely removed — no 3s invuln, no `OnTakeDamage`
  override, no `_invulnerableUntil` tracking. Death matters.
- `HealToFull` on spawn kept (PvE forgiveness — respawn fully healed).
- `citadel_allow_duplicate_heroes = 1` keeps Amber from locking on pick.

**Hero switching** — the initial scaffold hard-blocked
`selecthero`/`citadel_hero_pick` concommands. Requirement changed: every
player must be able to re-pick any time. `OnClientConCommand` now blocks
**only** `changeteam`/`jointeam` (to keep everyone on team 2). The in-game
hero-pick UI works; `!hero <name>` fuzzy-match remains as an alternative.

**Progression API** — to seed per-slot gold I needed to resolve slot from a
pawn. Canonical path: `pawn.Controller?.Slot` where `CBasePlayerPawn.Controller`
is a schema accessor on `m_hController` (returns `CBasePlayerController?`) and
`Slot => EntityIndex - 1` is defined on `CBasePlayerController`. Useful
pattern for any per-player state keyed by slot.
