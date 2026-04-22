---
date: 2026-04-22
task: swap Deathmatch flex-slot unlock to cheat concommand
files: [Deathmatch/Deathmatch.cs]
---

Deadlock exposes a cheat-gated concommand `citadel_unlock_flex_slots` that
achieves the same effect as writing `CCitadelGameRules.m_bFlexSlotsForcedUnlocked`
+ per-team `CCitadelTeam.m_nFlexSlotsUnlocked` bitmask. Sequence used in
Deathmatch (`Deathmatch/Deathmatch.cs` `UnlockFlexSlots`):

```
sv_cheats 1
citadel_unlock_flex_slots
sv_cheats 0
```

This replaces the previous schema-write approach that was documented on
[[deathmatch]] "Flex slot unlock" section (lines 72-82 of
wiki/plugins/deathmatch.md as of 2026-04-21). Still invoked at the same
lifecycle points: 1s-after-startup timer, `OnClientFullConnect`,
`OnPlayerHeroChanged`, `OnPlayerRespawned`. The `_flexSlotsForcedUnlocked` /
`_nFlexSlotsUnlocked` SchemaAccessors and `AllFlexSlotUnlockBits` const were
deleted as unused.

Worth capturing as a general pattern for [[source-2-engine]] / [[deadlock-game]]:
cheat-gated concommands can be bracketed with `sv_cheats 1 … sv_cheats 0` to
invoke a one-shot effect without leaving cheat mode enabled, where the
alternative is a multi-field schema write.
