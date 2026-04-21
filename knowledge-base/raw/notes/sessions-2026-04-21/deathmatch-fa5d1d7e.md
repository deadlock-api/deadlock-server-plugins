---
date: 2026-04-21
task: session extract — deathmatch fa5d1d7e
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-deathmatch/fa5d1d7e-7207-46ae-9b04-fa3c40f4d478.jsonl]
---

## Source 2 engine

- Panorama HUD top-bar clock lives at `citadel_hud_top_bar.xml:16-18`: `<Panel class="GameClock"><Label class="GameTime" text="{s:game_clock}" /></Panel>`. The `game_clock` binding is the only wiring between `CCitadelGameRules` clock fields and the rendered HUD string.
- Panorama XML inside the shipped VPKs is reconstructable via Source 2 Viewer 12.0.0.0 (comment in the dumped file: "xml reconstructed by Source 2 Viewer 12.0.0.0").
- Shipped Linux runtime at `.../Deadlock [EXPERIMENTAL]/game/bin/linuxsteamrt64/libclient.so` is stripped of the symbolic strings this session needed. The Windows build under `.../Deadlock/game/citadel/bin/win64/{client.dll,server.dll}` (non-experimental branch) still contains them — `strings` over those two DLLs is what unblocked the schema hunt.
- `CGlobalVarsBase` layout is pinned in `deadworks/managed/DeadworksManaged.Api/GlobalVars.cs`: `kCurTime=0x30`, `kTickCount=0x44`, `kIntervalPerTick=0x54`; base ends at `0x5C` before `CGlobalVars` adds mapname/etc.

## Deadlock game systems

- `CCitadelGameRules` clock fields (from `client.dll`/`server.dll` strings): `m_flGameStartTime`, `m_fLevelStartTime`, `m_flMatchClockAtLastUpdate`, `m_nMatchClockUpdateTick`, `m_flRoundStartTime`, `m_flStateStartTime`, `m_flGameStateEndTime`, `m_flGameTimeAllPlayersDisconnected`, `m_bGamePaused`, `m_bRoundDown`, `m_nLevel`, `m_nMatchLengthMinutes`, `m_nRoundsTotal`, `m_nRoundsRemaining`, `m_nGameOverEvent`.
- HUD clock extrapolation: client computes `game_clock ≈ m_flMatchClockAtLastUpdate + (CurTick − m_nMatchClockUpdateTick) * IntervalPerTick`. Writing only the float (as the old `FreezeMatchClock` did) leaves the tick anchor stale, so the client keeps adding `(now − oldTick)*dt` on top — clock appears to free-run even while the stored float is pinned. Both fields must be written together.
- Separately, `game_clock` also factors `CurTime − m_fLevelStartTime`: pinning only the match-clock pair works for round 1 (because both are ~0 at boot) but later rounds drift. `m_fLevelStartTime` has to be re-anchored each rotation alongside `m_flGameStartTime`.
- RTTI strings confirm both clock fields are networked: `CNetworkVarBase<int, ..._m_nMatchClockUpdateTick@C_CitadelGameRules>` and `CNetworkVarBase<float, ..._m_flMatchClockAtLastUpdate@C_CitadelGameRules>` — client-side C-prefix class is `C_CitadelGameRules`, server-side is `CCitadelGameRules`.
- `CNPC_TrooperBoss` (walker) carries `m_eLaneColor` as `CMsgLaneColor` (uint enum). Observed values 1/3/4/6 correspond to the four lanes (Blue/Yellow/Green/Purple implied by usage). The field can read back as 0 on early `OnEntitySpawned` — meaning the schema read races spawn-time initialisation, so plugins must not gate capture on a valid read. Fallback used in the plugin: bucket walkers per team by bearing angle around team centroid.

## Deadworks runtime

- `GlobalVars.TickCount` (returns `*(int*)(p + 0x44)`) is the correct source for `m_nMatchClockUpdateTick` writes — pairs cleanly with `GlobalVars.CurTime` for the float field.
- `NetMessages.Send<T>` constrains `T : IMessage<T>` (Google.Protobuf). Sending `CCitadelUserMsg_HudGameAnnouncement` from a plugin therefore requires the plugin csproj to reference `Google.Protobuf` at compile time. Local builds picked it up transitively through the sibling `DeadworksManaged.Api` ProjectReference; the Docker CI build links against the already-published `Api.dll` and doesn't see the transitive package, producing `CS0311` until a direct `PackageReference` is added.
- Driving timers off a ticking schema write (inside a per-tick callback that reads `GlobalVars.CurTime − _roundStart`) is more reliable than `Timer.Every(180)` — `Timer.Every` may not fire while the server is idle/empty but the tick callback still runs as long as the engine ticks.

## Plugin build & deployment

- CI error CS0311 for `CCitadelUserMsg_HudGameAnnouncement` not satisfying `IMessage<T>` is resolved by adding `<PackageReference Include="Google.Protobuf" Version="3.29.3" Private="false" ExcludeAssets="runtime" />` to the plugin csproj (same pattern the `DeadworksManaged.Api` project uses so the host-provided `Google.Protobuf.dll` isn't duplicated in the plugin output).
- Docker build log prefix observed: `/build/extra-plugins/<Plugin>/<Plugin>.csproj` — confirms the CI image mounts the plugin as `extra-plugins/` relative to `/build`.
