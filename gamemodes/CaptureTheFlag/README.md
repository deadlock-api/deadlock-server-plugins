# CaptureTheFlag

One-Flag Capture-the-Flag for the Deadworks-managed Deadlock dedicated server.
Pure server-side — **no client mods**. The flag is simulated entirely in plugin
state; all feedback is chat + HUD announcements.

## How it plays

- A single **neutral flag** sits at map center. Either team can grab it by
  walking within `PickupRadius`.
- The **carrier cannot fire their weapon** (primary + alt) — abilities and items
  still work — so the flag must be **escorted**, not solo-run.
- Score by holding the flag in the **enemy base zone** for `CaptureHoldSeconds`.
- The flag **drops on death** (returns to center after `FlagResetSeconds` if not
  re-grabbed) and resets to center if the carrier disconnects.
- First team to `CapturesToWin` wins the match; after `MatchEndDelaySeconds` a new
  match starts. The mode runs forever — the engine win screen is suppressed.

## Deployment

Composed via the repo-root `gamemodes.json` key `capture-the-flag`:

```
"capture-the-flag": ["utils/StatusPoker", "gamemodes/CaptureTheFlag", "utils/Hostname",
                     "utils/FlexSlotUnlock", "utils/TeamChangeBlock", "utils/HeroSelect",
                     "utils/HealOnSpawn", "utils/DisconnectCleanup", "utils/Feedback",
                     "utils/StuckCommand"]
```

Build/deploy uses the standard repo pipeline (Docker per-mode image, GHCR tag
`capture-the-flag`). Nothing CTF-specific is required.

## Base zones (auto-derived)

Zones are built at `OnStartupServer` from the live **Patron** (`npc_boss_tier3`)
positions — no hardcoded coordinates, so the mode adapts to map updates. Lane
troopers are stripped; the Patron, Walkers, Guardians and Watchers are **kept** as
base landmarks and made damage-immune so a team can't raze a base or trip the
engine's native objective/win path.

## Configuration

Bound via `[PluginConfig]` → `CaptureTheFlag/CaptureTheFlag.jsonc`, hot-reloadable
with `dw_reloadconfig`.

| Field | Default | Meaning |
|-------|---------|---------|
| `CapturesToWin` | `3` | Captures to win the match |
| `CaptureHoldSeconds` | `10` | Continuous hold in the enemy zone to score |
| `RespawnDelaySeconds` | `5` | Respawn delay (`citadel_player_spawn_time_max_respawn_time`) |
| `FlagResetSeconds` | `30` | Dropped flag's return-to-center timeout |
| `PickupRadius` | `150` | Horizontal pickup radius (world units) |
| `PollIntervalTicks` | `4` | Pickup/capture poll cadence |
| `WarmupSeconds` | `15` | Countdown before the first round once a human is present |
| `MatchEndDelaySeconds` | `15` | Pause on the win announcement before the next match |
| `BaseZoneRadius` | `700` | Half-extent (XY) of each base zone around its Patron |
| `CarrierDisableFire` | `true` | Block the carrier's primary/alt fire (the escort mechanic) |
| `CarrierSpeedModifier` | `""` | Move-speed modifier applied to the carrier — see below |
| `CarrierSpeedProperty` | `BonusMoveSpeedPercent` | Modifier property to override |
| `CarrierSpeedBonusPercent` | `50` | Value written into that property (+50%) |

### Enabling the carrier speed boost (the "Deadlock twist")

The `+50%` carrier move-speed buff ships **disabled** (`CarrierSpeedModifier = ""`)
because the exact move-speed modifier VData name must be confirmed on a live
server — applying a wrong name is a harmless no-op (logged once) but gives no buff.
The mode is fully playable without it: the **no-fire escort mechanic** (confirmed
working) is what forces teams to escort.

To enable: set `CarrierSpeedModifier` to a modifier whose VData auto-registers a
`MODIFIER_VALUE_MOVEMENT_SPEED_MAX_PERCENT` property named by `CarrierSpeedProperty`
(percent values like `BonusMoveSpeedPercent`/`MoveSpeedBonusPct` are present
throughout `abilities.vdata`), then `dw_reloadconfig`. The buff is applied with the
`AddModifier(name, abilityValues)` overload and removed on drop/score/death.

## Chat commands

| Command | Behavior |
|---------|----------|
| `!flag` | Flag state and rough location |
| `!score` | Current capture score |
| `!help` | Command + rules summary |
| `!stuck` / `!suicide` | Kill self to respawn (from `StuckCommand`) |

See `PRODUCT_PLAN.md` for the full design rationale.
