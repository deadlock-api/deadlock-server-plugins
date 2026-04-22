---
date: 2026-04-22
task: extract HeroSelect + HealOnSpawn shared plugins from Deathmatch/TrooperInvasion
files:
  - HeroSelect/HeroSelect.cs
  - HeroSelect/HeroSelect.csproj
  - HealOnSpawn/HealOnSpawn.cs
  - HealOnSpawn/HealOnSpawn.csproj
  - Deathmatch/Deathmatch.cs
  - TrooperInvasion/TrooperInvasion.cs
  - gamemodes.json
  - .github/workflows/build-plugins.yml
  - .github/workflows/docker-gamemodes.yml
---

Extracted two duplicated patterns from Deathmatch + TrooperInvasion into their own
plugins, extending the micro-plugin pattern set by FlexSlotUnlock / TeamChangeBlock /
Hostname.

- **HeroSelect** — owns the `!hero <name>` fuzzy-match command. The matcher
  (`FuzzyMatchHero`) is `public static` so gamemode plugins can reuse it for their
  own auto-pick logic (e.g. least-present-hero on join) without carrying a copy.
  Uses the v0.4.5 `[Command]` attribute (not `[ChatCommand]`). Does **not** handle
  Deathmatch's post-death `_heroSwapUntil` concommand window — that's
  DM-state-coupled and stays in DeathmatchPlugin.
- **HealOnSpawn** — hooks `player_respawned` + `player_hero_changed` and runs the
  20-tick `TryHeal` retry loop. Centralises the "`GetMaxHealth()` returns 0 for
  several ticks after these events" gotcha so no further plugin has to rediscover
  it. TI previously wrapped `HealToFull` in a 1-tick `DeferredSpawnRitual`; the
  defer was **not** needed for healing (the retry loop already handles unsettled
  hero assets) — only for `SeedStarterGold` which lives in TI's remaining ritual.

**CI filter gap found (not fixed for existing plugins):** The `paths-filter`
stanzas in both workflows only list a subset of plugin dirs. FlexSlotUnlock /
TeamChangeBlock / Hostname are currently missing from the filter — edits to them
alone would not trigger a build. I added HeroSelect + HealOnSpawn but did not
retro-add the three older ones. Worth revisiting.

Load-order / conflict check:
- Both new plugins are registered last in each gamemode's list. They have no
  state interactions with Deathmatch or TrooperInvasion — the healing hook
  returns `HookResult.Continue` so DM/TI's own spawn-ritual hooks still fire.
- `[Command("hero")]` registration is exclusive to HeroSelect now; the old
  `[ChatCommand("!hero")]` in Deathmatch and `[Command("hero")]` in
  TrooperInvasion are both gone.
