---
date: 2026-06-27
task: research the native Idol/Urn ("treasure") objective to reuse it as the CTF flag
files: [gamemodes/CaptureTheFlag/CaptureTheFlag.Flag.cs]
---

The game's "Idol" (a.k.a. Soul/Sand Urn, "treasure") objective is a real engine
entity and a near-perfect native CTF flag: pick up at mid → carry (slowed,
map-revealed) → cash in at a drop-off → reward. Research findings for reusing it:

- **Entity schema class: `CCitadelItemPickupIdol`** (confirmed in
  `deadlock-detection/extractor/src/main.rs` hash table; parallel
  `CCitadelItemPickupRejuv` is the mid-boss/Rejuvenator reward pickup).
- **Designer/classname for `CBaseEntity.CreateByDesignerName(...)` is UNCONFIRMED.**
  The Rejuv pickup is a vdata NPC subclass `citadel_item_pickup_rejuv`
  (`_class = "citadel_item_pickup_rejuv"`, `deadlock-assets-api/res/npc_units.vdata:4950`),
  so the idol is *probably* `citadel_item_pickup_idol` — but the idol is NOT in
  npc_units.vdata (it's a pure C++ entity), so the exact registered designer name
  must be verified live (try the create, check non-null).
- **Model + params live in `generic_data.vdata` `m_IdolParams`**: model
  `models/props_gameplay/idol_urn/idol_urn.vmdl`, idle seq `golden_idol_idle`,
  crate/parachute models, spawn sounds `Soul.Urn.Spawn[.Complete]`, particles
  `soul_jar_summon/return_location/drop`, drop height 1400, drop duration 12.5.
- **Native carry machinery exists and is detectable server-side:**
  `EModifierState` (`Enums/ModifierState.cs`) has `PickingUpIdol=0x93`,
  `HoldingIdol=0x94`, `ReturnIdolArea=0x95`, `ReturningIdol=0x96`, `DropIdol=0x97`.
  Read via `CModifierProperty.HasModifierState(state)` or hook `HasModifierStateEvent`.
  Carry modifier is `citadel_holding_golden_idol`. Cash-in entity name `idol_cashin`.
  HUD strings `Citadel_HUD_IdolPickedUp` / `Citadel_HUD_IdolReturned`. Net message
  `CCitadelUserMsg_ReturnIdol` (`k_EUserMsg_ReturnIdol = 320`, has `return_location`).
- **Key risk (same family as guardians + mid boss):** idol spawn / carry / cash-in
  is normally driven by the objective/match-init system that an
  `-insecure -allow_no_lobby_connect` dedicated server never runs (see
  [[trooper-invasion]] guardian self-spawn note). Manually creating the entity may
  give the visual idol without a working native pickup→cash-in loop (no cash-in
  points spawned). Must be verified live. Safe fallback: spawn the idol entity for
  visuals/native modifiers but keep the existing CTF state machine (proximity
  pickup + base-zone scoring) driving the game.
- **Mid boss** = classname `npc_mid_boss` (unit subclass `npc_super_neutral`, model
  `models/npc/midboss/midboss.vmdl`). `GameRules` exposes `m_iMidbossKillCount`,
  `m_tNextMidBossSpawnTime`. Suppress by stripping it in `OnEntitySpawned` and/or
  pushing `m_tNextMidBossSpawnTime` far out — though on the insecure server it
  likely never spawns natively anyway.
