---
date: 2026-04-22
task: Restrict trooper spawning to a subset of lanes
files: [TrooperInvasion/TrooperInvasion.cs, ../deadworks/examples/plugins/DeathmatchPlugin/DeathmatchPlugin.cs, ../deadworks/examples/plugins/TagPlugin/TagPlugin.cs]
---

**`citadel_active_lane` is a bitmask, not a count.** Deadlock's 4 lanes
map to bits `0b0001 / 0b0010 / 0b0100 / 0b1000`. Two reference points from
the Deadworks examples:

- `DeathmatchPlugin.cs:109` sets `citadel_active_lane 4` (= `0b0100`,
  enables lane 2 only — Deathmatch funnels the PvP action into a single
  non-outer lane).
- `TagPlugin.cs:90` sets `citadel_active_lane 255` (= all bits,
  lanes-all-on for maximum map coverage).

Cumulative "N lanes" mask: `(1 << N) - 1`:

| Lanes | Mask |
|---|---|
| 1 | 1 (`0b0001`) |
| 2 | 3 (`0b0011`) |
| 3 | 7 (`0b0111`) |
| 4 | 15 (`0b1111`) |

TrooperInvasion uses this to enforce "≥ 2 players per active lane":
`Clamp(humans/2, 1, 4)` → mask via `(1 << lanes) - 1`. Written via
`Server.ExecuteCommand` inside `RunWave` so the lane set updates as
players join/leave between waves.

Setting from chat-command or Timer callback via `Server.ExecuteCommand`
is the safe surface per the runtime-convar-mutation rule in
`2026-04-22-trooper-convar-runtime-mutation.md`.
