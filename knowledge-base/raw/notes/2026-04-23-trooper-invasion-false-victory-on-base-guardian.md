---
date: 2026-04-23
task: Fix TrooperInvasion declaring victory when enemy Base Guardian falls (Patron still alive)
files:
  - TrooperInvasion/TrooperInvasion.cs
---

The OnTakeDamage Patron intercept had a latent hole: any damage event on
`npc_barrack_boss` whose magnitude ≥ current Health triggered
`EndMode(victory: …)`. When players destroyed the enemy Base Guardian
(`npc_boss_tier3`) — and anecdotally a Walker before it — the engine
fires a single large scripted damage event on the enemy Patron (the
"weaken Patron" transition that exposes it to be killable). Our hook
saw this as a lethal hit and declared victory even though no player
ever landed a killing blow on the Patron itself. Observable symptom:
"VICTORY!" HUD fires, EndMode cooldown runs, but the enemy Patron is
still standing in-world (HP pinned to 1 by the same hook).

The fix at `TrooperInvasion/TrooperInvasion.cs:599-633` keeps the HP
pin (so the Patron never reaches 0 → no `m_eGameState` kick — see
`2026-04-23-gamestate-pin-required-on-patron-death.md`) but only calls
`EndMode` when the lethal hit has a real attacker — either a
`CCitadelPlayerPawn` or an `IsTrooperDesigner` entity. Engine-scripted
damage with null / world / non-pawn / non-trooper attacker gets
absorbed without ending the gamemode.

Non-obvious bits to remember:
- `CTakeDamageInfo.Attacker` is the originating entity (player/trooper
  pawn), separate from `Inflictor` (the projectile/bullet). Player-
  landed kills resolve `Attacker` to the pawn, not the gun.
- Can't just rely on `m_eGameState` drift watcher here: the false
  victory came from EndMode being called in-plugin, not from an engine
  state flip. Clients weren't disconnected — they saw the plugin's own
  "VICTORY!" HUD announcement.
- The symmetric defeat path (trooper killing friendly Patron) keeps
  working because troopers pass the `IsTrooperDesigner(attacker.
  DesignerName)` branch.
