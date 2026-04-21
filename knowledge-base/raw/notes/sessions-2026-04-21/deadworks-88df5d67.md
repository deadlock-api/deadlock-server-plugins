---
date: 2026-04-21
task: session extract — deadworks 88df5d67
files: [/home/manuel/.claude/projects/-home-manuel-deadlock-deadworks/88df5d67-71c2-4f2f-8014-323900de2346.jsonl]
---

## Source 2 engine

- Deadworks boots the real Source 2 engine by `LoadLibrary`-ing `engine2.dll` and calling its exported `Source2Main` symbol directly; signature is `int (*)(void *hInstance, void *hPrevInstance, const char *pszCmdLine, int nShowCmd, const char *pszBaseDir, const char *pszGame)` (`deadworks/src/startup.cpp:9`). The final call passes `"citadel"` as the game name at `deadworks/src/startup.cpp:76`.
- The one hook installed before handoff is `OnAppSystemLoaded`; all other game-engine hooks are initialized from inside that hook once app systems are up (~30 hooks total, using SafetyHook, driven by IDA patterns in `config/deadworks_mem.jsonc`).

## Deadlock game systems

- Assigning a hero to a player goes through the player controller: `controller.SelectHero(Heroes.Warden)` on a `CCitadelPlayerController` at `managed/DeadworksManaged.Api/Entities/PlayerEntities.cs:58`. Under the hood it marshals to a native cdecl callback (`managed/DeadworksManaged.Api/NativeInterop.cs:72`, `NativeCallbacks.cs:72`).
- Real usage: `TagPlugin.cs:129` assigns Warden vs Astro per team; `DeathmatchPlugin.cs:223,388` calls `SelectHero(newHero)` on respawn, resolving loadout via `Config.HeroItemSets` keyed by stringified hero id.
- Hero-id-keyed data shows up across protobufs (`citadel_gcmessages_common.proto`, `citadel_clientmessages.proto`, `citadel_usermessages.proto`) and `DeathmatchPlugin/HeroItemSets.jsonc` (numeric `hero_id` keys 1..81 with per-hero item sets).

## Deadworks runtime

- Plugin language is C# only. Plugins load as .NET assemblies into isolated `AssemblyLoadContext`s via `PluginLoader`; a file watcher on `/plugins` drives hot-reload. Rust/other-language plugins are not supported — would require a NativeAOT bridge compiling to a .NET assembly.
- `IDeadworksPlugin` exposes ~18 lifecycle/hook methods (OnLoad, OnGameFrame, OnTakeDamage, OnChatMessage, …). Host-side dispatch: native C++ hooks fire → `EntryPoint` unmanaged callbacks → `PluginLoader` iterates registered plugins.
- API assembly is referenced with `Private=false` so host and plugin share one set of API types (important for marshalling and type-identity across ALCs).
- Plugin-scoped services: per-plugin `TimerEngine`, `ConCommandManager`, config auto-reload on file change, net-message handlers (in/out), entity IO hooks, and game-event handlers.

## Plugin build & deployment

- Self-hosted model: `deadworks.exe` is a replacement entry point that must be run from `<Deadlock>/game/bin/win64/` (README.md:46,65). Players connect via in-game console `connect localhost:27067` (README.md:67) — 27067 is the default port.
- `local.props` (user-created) drives paths: `ProtobufIncludeDir`, `ProtobufLibDir`, `NetHostDir` (e.g. `C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Host.win-x64\10.0.5\runtimes\win-x64\native`, README.md:22), and optional `DeadlockDir` which, when set, enables post-build auto-deploy to the game dir.
- Toolchain: MSVC v145 (VS 2022), C++20, statically links `libprotobuf.lib` (protobuf 3.21.8) and links .NET `nethost`. C# side targets .NET 10. `deadworks.exe` is Windows x64-only.
- Linux/Docker/Proton hosting is undocumented and unproven. Risk surface: Wine-loaded `engine2.dll`, SafetyHook under Wine, .NET native hosting interop, and signature-pattern matching against possibly-different in-memory layouts when loaded via Proton.
