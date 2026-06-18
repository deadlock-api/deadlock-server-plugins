---
date: 2026-06-18
task: restructure repo into gamemodes/ and utils/ top-level folders
files: [gamemodes.json, Makefile, .github/workflows/build-plugins.yml, .github/workflows/docker-gamemodes.yml]
---

Repo was restructured from flat plugin layout to two top-level folders:
- `gamemodes/` — actual gamemode plugins (Deathmatch, LockTimer, TrooperInvasion) + planned gamemode stubs (ArenaFights, CaptureTheFlag, DuelArena, FreeForAll, GunGame, HordeSurvival, KingOfTheHill, PayloadRace, TeamDeathmatch)
- `utils/` — utility/shared plugins (DisconnectCleanup, Feedback, FlexSlotUnlock, HealOnSpawn, HeroSelect, HeroSelectOnNextSpawn, Hostname, StatusPoker, StuckCommand, TeamChangeBlock)

**gamemodes.json** now uses full relative paths like `"utils/StatusPoker"` and `"gamemodes/Deathmatch"` instead of bare names. The Makefile `csproj_for` function and `cd "$$p"` work transparently with these paths since they're relative to the repo root.

**All csproj files**: local-dev `ProjectReference` path updated from `$(MSBuildThisFileDirectory)..\..\deadworks\` to `$(MSBuildThisFileDirectory)..\..\..\deadworks\` (one extra `..` due to being one level deeper).

**CI workflows**: path filter globs updated to new locations (e.g. `Deathmatch: - 'gamemodes/Deathmatch/**'`). The `build-plugins.yml` matching logic changed from `split("/")[0]` to `split("/")[-1]` to extract the plugin name from a multi-segment path. The `docker-gamemodes.yml` jq expression similarly uses `($p | split("/")[-1])` to match filter names against plugin paths from gamemodes.json.

**Docker staging**: `rsync -a "plugins/utils/StatusPoker" "staged-plugins/"` correctly produces `staged-plugins/StatusPoker/` because rsync without a trailing slash on source copies the directory itself — so the `extra-plugins` Docker build context still receives flat named plugin dirs.
