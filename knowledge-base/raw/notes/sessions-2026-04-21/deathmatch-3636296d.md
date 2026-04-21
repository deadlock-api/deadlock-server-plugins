---
date: 2026-04-21
task: session extract — deathmatch 3636296d
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadlock-deathmatch/3636296d-c124-4068-947a-2febaa578702.jsonl]
---

## Deadworks runtime

- `NetMessages.Send<T>` at `/home/manuel/deadlock/deadworks/managed/DeadworksManaged.Api/NetMessages/NetMessages.cs:22` constrains `T : IMessage<T>` from `Google.Protobuf`; callers using generated protobuf types (e.g. `CCitadelUserMsg_HudGameAnnouncement` in `DeathmatchPlugin.cs:409`) therefore transitively require the `Google.Protobuf` assembly to be resolvable at compile-time against the plugin.
- Generated proto classes live under `DeadworksManaged.Api/obj/Debug/net10.0/CitadelUsermessages.cs` (via `<Protobuf Include="..\protos\**\*.proto">` in `DeadworksManaged.Api.csproj:31`). `Google.Protobuf` 3.29.3 is a `PackageReference` in the Api csproj (line 17); `Grpc.Tools` 2.69.0 provides the codegen.
- `DeadworksManaged.Api.csproj` does NOT ship `Google.Protobuf.dll` as a loose file anywhere under `deadworks/managed` — `find ... Google.Protobuf.dll` returned no results. At runtime, the DLL must arrive via NuGet restore into the host `DeadworksManaged` process; plugins reference the proto types but do not get a private copy.
- Deadworks plugin discovery: sibling plugins live at `/home/manuel/deadlock/deadworks/managed/plugins/` (AutoRestartPlugin, DumperPlugin, ExampleTimerPlugin, ItemRotationPlugin, ItemTestPlugin, RollTheDicePlugin, ScourgePlugin, SetModelPlugin, TagPlugin, DeathmatchPlugin). None of them currently use `NetMessages.Send` or any protobuf type — a grep across the whole tree returned zero matches. The Deathmatch plugin is the first in-tree consumer of `NetMessages` with a protobuf payload.

## Plugin build & deployment

- The `deadlock-deathmatch` repo is a satellite repo (cwd `/home/manuel/deadlock/deadlock-deathmatch`) with a dual-mode csproj pattern. Both `DeathmatchPlugin.csproj:12-26` and `StatusPoker.csproj:13-26` conditionally pick between `<ProjectReference>` when the sibling `../../../deadworks/managed/DeadworksManaged.Api/DeadworksManaged.Api.csproj` exists (local dev) vs. a bare `<Reference Include="DeadworksManaged.Api">` fallback for Docker builds.
- Docker build error CS0311+CS0012 reproduces here: in Docker, the deadworks Dockerfile at line 108-114 auto-generates `extra-plugins/Directory.Build.targets` injecting `<AssemblySearchPaths>$(AssemblySearchPaths);/artifacts/managed</AssemblySearchPaths>` so the bare `DeadworksManaged.Api` reference resolves against pre-published `/artifacts/managed/DeadworksManaged.Api.dll`. However `Google.Protobuf.dll` is NOT in `/artifacts/managed`, so the compiler cannot see `IMessage<T>` and the `NetMessages.Send<CCitadelUserMsg_HudGameAnnouncement>` call fails type inference. Fix direction (not applied in session): add `<PackageReference Include="Google.Protobuf" Version="3.29.3">` to the plugin csproj, or publish `Google.Protobuf.dll` alongside `DeadworksManaged.Api.dll` into `/artifacts/managed`.
- `DeadworksManaged.csproj` (the host, not Api) declares `<EnableDynamicLoading>true</EnableDynamicLoading>` and publishes a `.deps.json` + `.runtimeconfig.json` (`DeadworksManaged.csproj:27-30`). Plugin csprojs also set `EnableDynamicLoading=true` — required for AssemblyLoadContext isolation when `DeadworksManaged` loads each plugin DLL at runtime.
- The deathmatch-satellite layout is `plugins/<Name>/<Name>.csproj` (flat), not `extra-plugins/` — the Docker error path shows the repo is mounted at `/build/extra-plugins/` by deadworks' outer Dockerfile and discovered via `find extra-plugins -name '*.csproj'`.

## Deadlock game systems

- Deathmatch lane rotation cycle at `plugins/DeathmatchPlugin/DeathmatchPlugin.cs:33` is `{ 1, 3, 6 }` = Yellow, Green, Purple; blue lane (4) is explicitly skipped. Round length `RoundSeconds = 180f` (line 32). Rotation announcement at line 409-413 sends a `CCitadelUserMsg_HudGameAnnouncement` with Amber vs Sapphire score + top-killer + next-lane name to `RecipientFilter.All`.
- Map NPCs stripped in deathmatch mode (line 14-21): `npc_boss_tier1` (Guardian), `npc_boss_tier2` (Walker), `npc_boss_tier3` (Base Guardian/Shrine), `npc_barrack_boss` (Patron), `npc_base_defense_sentry`, `npc_trooper_boss`.
- Hero max-HP is not settled at hero-changed/respawn event time — `HealToFull` at `DeathmatchPlugin.cs:435` retries briefly until `GetMaxHealth` returns a sane value (comment at line 437-439). Subtle timing coupling with CCitadel schema propagation.
- User follow-up (line 90, unaddressed in this session) requested a design change: round timer still not resetting after rotation; switch from bulk-teleport-all-alive at rotation to per-player teleport on next death, so the active lane shifts gradually rather than snapping.
