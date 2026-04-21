---
date: 2026-04-21
task: session extract — deathmatch 5233473a
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-deathmatch/5233473a-0a3f-49eb-95b7-8abe82036a25.jsonl]
---

Session: `/simplify` pass over the separate `deadlock-deathmatch` repo (sibling of `deadlock-server-plugins`). Three-agent code-reuse / quality / efficiency review on `DeathmatchPlugin.cs` (688 lines) and `StatusPoker.cs` (110 lines). Findings apply across both plugin repos.

## Deadlock game systems

- `CCitadelGameRules` HUD-clock anchor requires setting THREE schema fields in lockstep every tick for correct client extrapolation: `m_flGameStartTime`, `m_flLevelStartTime`, `m_flRoundStartTime`, plus `m_flMatchClockAtLastUpdate` and `m_nMatchClockUpdateTick` (DeathmatchPlugin.cs:45-50, 116-121). Missing `m_flRoundStartTime` caused HUD to extrapolate past writes — fixed in commit `dc9114e`.
- Post-death hero swap is gated by a 10s window keyed on controller `EntityIndex`: con-commands `selecthero` / `citadel_hero_pick` are normally `HookResult.Stop`-ed, but allowed through during `_heroSwapUntil` window seeded only on an actual respawn-after-death (DeathmatchPlugin.cs:329-338, 393-395). `changeteam` / `jointeam` are always blocked.
- "Drift-on-respawn" rotation pattern: rotating active lane does NOT teleport alive players — they stay in the old lane and naturally drift to the new one on next respawn because `PickSpawnPoint` targets `ActiveLane`. Replaced an earlier mass-teleport reset (DeathmatchPlugin.cs:443-450, commit `65eb42c`).
- Walker spawn-point capture: `OnEntitySpawned` fires for every entity and triggers full `RebuildWalkerBuckets()` + `RecomputeMapCenter()` per walker add. Assumed to be map-load-time-only; would thrash if walkers spawned at tick rate (DeathmatchPlugin.cs:241-260).
- Hero enumeration pattern: `Enum.GetValues<Heroes>().Where(h => h.GetHeroData()?.AvailableInGame == true)` is the canonical "currently playable heroes" filter via `HeroTypeExtensions.GetHeroData()`.

## Deadworks runtime

- Host `Timer.Every` is **sync-only** — cannot be used for async HTTP polling. StatusPoker's `System.Threading.Timer` with a `CancellationTokenSource` + manual reschedule loop is kept deliberately for that reason (StatusPoker.cs:12, 46-47).
- No centralized HTTP facility in host API — plugins use their own `static readonly HttpClient` (StatusPoker.cs:11).
- No env-var config helper in host API despite `ConfigManager.cs` / `[PluginConfig]` existing; plugins rolling their own `DEADWORKS_ENV_*`-prefixed getters is intentional.
- `[PluginConfig]`-decorated classes must exist even if empty — empty `DeathmatchConfig` at DeathmatchPlugin.cs:6-8 is required by the host contract, not dead code.
- `SchemaAccessor<T>` uses UTF-8 string literals (`"CCitadelGameRules"u8`, `"m_flGameStartTime"u8`) for class/field names (DeathmatchPlugin.cs:46).
- Disconnect cleanup must cover ALL per-player dicts: `_invulnerableUntil`, `_heroSwapUntil`, `_lastDeathPos`, `_killsThisRound`, `_writtenCooldowns` all keyed by controller/entity index and pruned in `OnClientDisconnect` (DeathmatchPlugin.cs:670-678).
- Tick-rate hot-path GC gotcha: `ScaleAbilityCooldowns` originally allocated a fresh `Dictionary<nint, (float, float)>` every tick at 64 Hz and reassigned `_writtenCooldowns = seen`. Rewrote to mark-and-sweep in place with a reused `HashSet`/`List` to eliminate millions of throwaway dicts per match (DeathmatchPlugin.cs:635 area).
- `ScoreCandidate` was O(candidates × enemies × 2) — enemies walked twice per candidate for min-distance and min-angular-offset. Folded into one pass (DeathmatchPlugin.cs:553-591).

## Plugin build & deployment

- `dotnet build` from a plugin repo transitively builds the sibling `deadworks` managed projects: `DeadworksManaged.Generators` (netstandard2.0) and `DeadworksManaged.Api` (net10.0), then the plugin DLLs into `plugins/<Name>/bin/Debug/net10.0/`.
- Persistent build warning `MSB3023` at `DeadworksManaged.Api.csproj(38,5)`: "Kein Ziel für den Kopiervorgang" — a `<Copy>` task missing `DestinationFiles`/`DestinationFolder`. Non-fatal, appears on every build.
- Plugin .cs files compile under `net10.0` target framework.
