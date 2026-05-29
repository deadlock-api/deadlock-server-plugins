---
date: 2026-05-29
task: simplify TrooperInvasion — split the 1045-line single class/file
files: [TrooperInvasion/TrooperInvasion.cs, TrooperInvasion/WaveTuning.cs, TrooperInvasion/SessionStats.cs, TrooperInvasion/TrooperInvasion.Waves.cs, TrooperInvasion/TrooperInvasion.Troopers.cs, TrooperInvasion/TrooperInvasion.EndMode.cs, TrooperInvasion/TrooperInvasion.Players.cs, TrooperInvasion/TrooperInvasion.Commands.cs]
---

`TrooperInvasionPlugin` was one 1045-line file/class. Split with **zero behavioural
change** into: two real collaborator classes (`WaveTuning` — static, pure difficulty
constants + curves; `SessionStats` — instance, owns all telemetry counters +
`RoundPlayerStats` + PostHog/chat emission) and five `partial class` files grouping
the plugin's own members by concern (`.Waves`, `.Troopers`, `.EndMode`, `.Players`,
`.Commands`). Core `TrooperInvasion.cs` keeps config, fields, game constants, and
lifecycle (OnLoad/OnStartupServer/OnUnload).

Key extraction details: `ComputeHealthScale`/`ComputeRespawnDelay`/`ComputeBurstSeconds`
became pure `WaveTuning.*(roundNum, waveNum, …)` (round/wave passed in, not read from
fields). `SessionStats` owns `_deathsThisWave`/`DeathsPrevWave` (`RecordDeath()` +
`RotateWaveDeaths()`), `_playerJoinTimes` (`RecordJoin()`/`EndSession()`), and takes
wave/round as emit-time params (`EmitRoundSummary(outcome, round, wave)`,
`EmitSessionOutcome(outcome, wave, round)`). `RoundLength` moved to `WaveTuning` (now
`WaveTuning.RoundLength`). Partials require no csproj change — default glob compiles
all `.cs`. Builds 0 errors against sibling deadworks ProjectReference. The csproj is
unchanged; `StatsClient` in `Stats/` was already separate.
