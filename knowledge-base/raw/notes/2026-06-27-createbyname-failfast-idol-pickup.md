---
date: 2026-06-27
task: spawn the Idol/Urn as the CTF flag — hit a server crash
files: [gamemodes/CaptureTheFlag/CaptureTheFlag.Idol.cs, ../deadworks/managed/DeadworksManaged.Api/Entities/CBaseEntity.cs]
---

`CBaseEntity.CreateByDesignerName("citadel_item_pickup_idol")` HARD-CRASHES the
dedicated server (native `RaiseFailFastException`, process terminated). Stack:
`CreateByName` → `CreateByDesignerName` → our `SpawnIdol` → `OnStartupServer`.
A managed `try/catch` around the create does NOT help — it's a native fail-fast
inside `CEntitySystem::CreateEntityByName`, not a managed exception.

Cause: `CreateByDesignerName` (CBaseEntity.cs:66) calls `ResolveDesignerName`
(returns null for a non-subclass name), then falls through to
`CreateByName(rawName)` → native `CreateEntityByName`. If the engine has no
registered entity **factory** for that classname, the native call aborts the
process. The idol pickup's factory (schema/networked class `CCitadelItemPickupIdol`,
seen in demo files) is only registered once the **objective system** initialises —
which an `-insecure -allow_no_lobby_connect` server never does. Same root cause as
the guardian/mid-boss "match-init path never runs" findings in [[trooper-invasion]].

Rule of thumb: **only `CreateByName`/`CreateByDesignerName` classnames known to have
a registered factory.** Subclass NPCs resolved via the registry are safe (guardians
spawn `npc_boss_tier1` fine). Standard props (`prop_dynamic`, `prop_dynamic_override`,
`prop_physics_override`) are safe. Objective/match-gated entities (the idol/urn pickup,
likely the rejuv pickup too) are NOT — they crash rather than return null. There is no
managed factory-exists check exposed, so an allowlist is the only safe guard.

Resolution for CTF: the flag is now a `prop_dynamic` wearing the urn model
`models/props_gameplay/idol_urn/idol_urn.vmdl` (precached in `OnPrecacheResources`),
driven by the existing simulated-flag state machine. Looks like the urn; no native
carry/cash-in behaviour and no healthbar (a prop has none).
