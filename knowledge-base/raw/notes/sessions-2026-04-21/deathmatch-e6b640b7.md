---
date: 2026-04-21
task: session extract — deathmatch e6b640b7
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-deathmatch/e6b640b7-f17b-434e-ad6e-55aa9cfbe2b0.jsonl]
---

## Deadlock game systems

- Flex-slot unlock requires BOTH schema fields: `m_bFlexSlotsForcedUnlocked` (bool) on `CCitadelGameRules` AND `m_nFlexSlotsUnlocked` (short bitmask) on `CCitadelTeam`. Writing only the gamerules bool is insufficient; the client appears to derive per-player slot availability from the per-team bitmask. Bits: `Kill2Tier1=0x1 | Kill1Tier2=0x2 | Kill2Tier2=0x4 | BaseGuardians=0x8` — set `0xF` on every `CCitadelTeam` entity to force all four.
- Flex-slot writes must hit BOTH `CCitadelGameRules` and every `CCitadelTeam` entity, and must be re-applied at multiple lifecycle points (startup after a 1s delay so gamerules/teams network, `OnClientFullConnect`, `OnPlayerHeroChanged`, `OnPlayerRespawned`) — writing once at startup is not enough, the flag may not yet have taken effect before the pawn networks its initial state.
- `npc_boss_tier2` is the classname of the Walker/Tier 2 tower; positions can be captured from the startup entity loop and from `OnEntitySpawned` for late spawns. `ent.TeamNum` on the Walker identifies which side it belongs to, enabling per-team spawn pools.
- Deadlock ships with `mp_friendlyfire = 0` by default — stock behaviour blocks same-team bullet damage, but ability AoE/grenades/debuff pulses are NOT guaranteed to respect team; plugin must add explicit `if (v.TeamNum == a.TeamNum && a != v) Damage = 0` to close gaps.
- `controller.ChangeTeam(int)` bypasses the engine's team-picker prompt entirely — the picker appears BEFORE `OnClientFullConnect` fires in some builds, so calling `ChangeTeam` from there may still show the prompt briefly. Server-side `ChangeTeam` calls do NOT flow through the `changeteam`/`jointeam` console command path, so blocking those client concommands does not disable the server-side auto-balancer.
- `ScaleAbilityCooldowns` shifts the cooldown window so remaining time becomes `scale × duration`; `CooldownScale = 0.5f` yields 50% cooldowns.
- `FreezeMatchClock` (forces `EGameState.GameInProgress` + `OnGameoverMsg` Stop + `OnRoundEnd` Stop) is the comprehensive gameover-suppression pattern; pinning the clock rules out any time-limit mode until relaxed.

## Deadworks runtime

- `controller.TeamNum` reflects whatever team the engine's auto-balancer assigned at join time — readable inside `OnClientFullConnect` to key per-team logic; falling back to literal `{2, 3}` is necessary if walkers haven't loaded yet when counting existing players per team.
- `Entities.All` iteration works for discovering `CCitadelTeam` entities at runtime to write per-team schema fields.
- `SchemaAccessor<bool>` / `SchemaAccessor<short>` are the supported idiom for reading/writing arbitrary Deadlock schema fields not exposed by the Api wrappers.
- ConVar writing: `ConVar.Find("hostname")?.SetString(...)` OR `Server.ExecuteCommand("hostname \"...\"")` — both work from `OnStartupServer`, but plugin-set hostnames appear briefly under the default before propagating to the master server. Use launch-arg `+hostname` or `game/citadel/cfg/server.cfg` (loaded before `OnStartupServer`) for the value that the server-browser first sees.
- `OnClientConCommand` is the hook to gate/block client-issued console commands (`changeteam`, `jointeam`, `selecthero`, `die`); returning Stop suppresses the command. Server-initiated code paths (auto-balancer's `ChangeTeam`) are unaffected.
- `Timer.Once(1s, ...)` is the idiomatic way to defer work until after gamerules/teams have networked on startup.

## Source 2 engine

- Source hostname strings display cleanly up to ~64 chars in the server browser; standard Source `hostname` ConVar applies.
- `game/citadel/cfg/server.cfg` is the startup cfg Deadlock loads (standard Source layout under `game/<mod>/cfg/`).
