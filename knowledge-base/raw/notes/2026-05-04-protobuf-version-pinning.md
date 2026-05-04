---
date: 2026-05-04
task: investigate failed Build Gamemode Images CI run; fix CS1705 in TrooperInvasion
files: [TrooperInvasion/TrooperInvasion.csproj, Deathmatch/Deathmatch.csproj]
---

When deadworks bumps `Google.Protobuf` in `managed/DeadworksManaged.Api/DeadworksManaged.Api.csproj`,
every plugin that has a `<PackageReference Include="Google.Protobuf" Version="...">` must be bumped to
the **same** version, otherwise the C# compiler refuses the link with:

> CS1705: Assembly 'DeadworksManaged.Api' uses 'Google.Protobuf, Version=X' which has a higher
> version than referenced assembly 'Google.Protobuf' with identity 'Google.Protobuf, Version=Y'.

This bites in the deadworks Docker `extra-plugins` stage (`/build/extra-plugins/<Plugin>.csproj`),
not on local sibling-repo `ProjectReference` builds — locally the plugin transitively picks up
DeadworksManaged.Api's package version.

Plugins in this repo that pin Protobuf and need to be kept in sync with deadworks:
- `TrooperInvasion/TrooperInvasion.csproj`
- `Deathmatch/Deathmatch.csproj`

Both use `<Private>false</Private>` + `<ExcludeAssets>runtime</ExcludeAssets>` because
Google.Protobuf is loaded into the host's default ALC and shared with plugins — the package
reference exists only to satisfy the compiler for `NetMessages.Send<T> where T : IMessage<T>`.
The version number must match deadworks but the package itself is never shipped.

Triggering deadworks commit: `bb670a3 update dotnet dependencies` (3.29.3 → 3.34.1).

Side-finding (local-only, ignore for CI): with .NET SDK 10.0.107 the `DeadworksManaged.Generators`
analyzer fails to load locally (`CS9057: analyzer references compiler 5.3.0.0, currently running
5.0.0.0`), so the `*.gameevents`-driven event classes (`RoundEndEvent`, `EntityKilledEvent`,
`PlayerHeroChangedEvent`, …) are silently not generated and the plugin appears to have a flood of
CS0246 errors on a local `dotnet build`. CI is unaffected (`mcr.microsoft.com/dotnet/sdk:10.0`
ships a newer csc that accepts the analyzer).
