# LockTimer — Design

**Date:** 2026-04-12
**Status:** Approved for implementation planning
**Target:** `Plugins/LockTimer/` — Deadworks managed server plugin

## Summary

LockTimer is a minimalist speedrun timer plugin for Deadlock, modeled after
the CS2 plugin [poor-sharptimer](https://github.com/Letaryat/poor-sharptimer)
but stripped to the essentials. Players define two axis-aligned bounding-box
zones per map — a `start` and an `end` — by capturing crosshair raycasts via
chat commands. The plugin times each player's run between the two zones and
persists personal best (PB) records to a local SQLite database. Zone edges
are rendered as glowing particle beams visible to everyone.

There are no permission guards of any kind — every command is callable by
every connected player. This is intentional for the MVP.

## Non-goals

- Multi-stage runs, checkpoints, or bonuses.
- Multi-course maps (multiple independent start/end pairs).
- Full run history — MVP stores PB only.
- Web / HTTP APIs, Discord integration, cross-server syncing.
- Permission system, admin commands, or authentication.
- Zone rotation (OBBs). Zones are axis-aligned boxes.
- Anti-cheat / replay validation.

## Feasibility — DeadworksManaged.Api surface verified

Everything required exists in the current managed API (checked against
`deadworks/managed/DeadworksManaged.Api/`):

| Need | API |
|---|---|
| Crosshair raycast | `Trace.SimpleTraceAngles(eyePos, viewAngles, …)` / `Trace.Ray(start, end, ignore)` |
| Player eye pos / angles | `CCitadelPlayerPawn.EyePosition`, `.ViewAngles` (raw float precision) |
| Player world position | `CBaseEntity.Position` (AbsOrigin) |
| Chat commands | `[ChatCommand("!cmd")]` attribute |
| Persistent particles | `CParticleSystem.Create(...).WithDataCP(cp, vec).Spawn()` |
| Map name | `Server.MapName` |
| Custom content path | `Server.AddSearchPath(path, "GAME")` |
| Frame tick | `DeadworksPluginBase.OnGameFrame(...)` |
| Client lifecycle | `OnClientPutInServer`, `OnClientDisconnect` |
| Ground / velocity | `CBaseEntity.IsOnGround`, `.AbsVelocity` (pulled 2026-04-12) |

## Architecture — layered services

Single `Plugins/LockTimer/LockTimer.csproj` (net10.0, class library, references
`DeadworksManaged.Api.dll` + `Google.Protobuf.dll`, `DeployToGame` target).
`LockTimerPlugin : DeadworksPluginBase` is a thin shell that wires focused
services together:

```
Plugins/LockTimer/
├── LockTimer.csproj
├── LockTimerPlugin.cs             // entry, ~120 lines wiring
├── Zones/
│   ├── Zone.cs                    // record: Id, Map, Kind, Min, Max, UpdatedAt
│   ├── ZoneRepository.cs          // SQLite CRUD, scoped per Server.MapName
│   ├── ZoneEditor.cs              // !start1/2, !end1/2, !savezones, !delzones
│   └── ZoneRenderer.cs            // spawns/destroys particle edges per Zone
├── Timing/
│   ├── RunState.cs                // enum Idle | InStart | Running | Finished
│   ├── PlayerRun.cs               // per-player: State, StartTickMs, WasInStart
│   └── TimerEngine.cs             // OnGameFrame tick, AABB containment, FSM
├── Records/
│   ├── Record.cs                  // record: SteamId, Map, TimeMs, Name, AchievedAt
│   ├── RecordRepository.cs        // SQLite CRUD, PB-only upsert
│   └── TimeFormatter.cs           // FormatTime(int ms) -> "H:MM:SS.fff"
├── Data/
│   ├── LockTimerDb.cs             // SQLite connection + migrations
│   └── Migrations/001_initial.sql
└── Commands/
    └── ChatCommands.cs            // [ChatCommand] methods, forward to services
```

**Dependency direction:** `Commands → Editor/Engine → Repositories → LockTimerDb`.
`ZoneRenderer` is invoked only by `LockTimerPlugin` during load / save / delete —
it never calls repos. `TimerEngine` never talks to the DB directly; it holds
cached `Zone?` references injected by the plugin shell.

**DB location:** `<game>/bin/win64/managed/plugins/LockTimer/locktimer.db`.
Created on first run; schema applied via `CREATE TABLE IF NOT EXISTS`.

**NuGet dependencies:**
- `Microsoft.Data.Sqlite.Core`
- `SQLitePCLRaw.bundle_e_sqlite3`

Both must be deployed alongside the plugin DLL. The csproj `DeployToGame`
target's `DeployFiles` ItemGroup is extended to glob `$(OutputPath)*.dll` and
the `e_sqlite3.dll` native binary from the build output.

## Data flow — timer state machine

Per-player state lives in `TimerEngine` as `Dictionary<int slot, PlayerRun>`.
Populated in `OnClientPutInServer`, removed in `OnClientDisconnect`. Bots
(`IsBot == true`) never enter the dictionary.

**`PlayerRun`**
- `State: RunState`
- `StartTickMs: long` — `Environment.TickCount64` at the moment Running began
- `WasInStart: bool` — previous-frame containment, for edge detection

**State machine** (ticked from `OnGameFrame`, only when `simulating == true`):

```
Idle       ──pos∈start──▶ InStart
InStart    ──pos∉start──▶ Running   (StartTickMs = now)
Running    ──pos∈end────▶ Finished  (elapsed = now - StartTickMs)
Running    ──pos∈start──▶ InStart   (reset; elapsed discarded)
Finished   ──next tick──▶ Idle      (one-shot, flush to DB before)
```

**On `Finished`:**
1. `elapsed = Environment.TickCount64 - StartTickMs` (int32, capped at int32.MaxValue).
2. `RecordRepository.UpsertIfFaster(steamId, map, elapsed, name, nowUnix)` returns `(bool changed, int? previousMs)`.
3. Broadcast chat message:
   - `"[LockTimer] <name> finished in 0:01:23.456 (new PB!)"` if changed and previous was null
   - `"[LockTimer] <name> finished in 0:01:23.456 (new PB! prev 0:01:25.777)"` if changed and previous existed
   - `"[LockTimer] <name> finished in 0:01:27.444 (pb 0:01:25.777)"` if slower than PB
4. State → `Idle` on the same tick.

**Containment check:** `Zone.Contains(Vector3 p) =>
p.X >= Min.X && p.X <= Max.X && p.Y >= Min.Y && p.Y <= Max.Y && p.Z >= Min.Z && p.Z <= Max.Z`.
The player's feet position is `pawn.Position` (AbsOrigin).

**Frame cost:** one AABB test × connected players × 2 zones per frame.
No per-frame allocations — `PlayerRun` instances are kept alive between ticks.

**Map change:** `OnStartupServer` clears all `PlayerRun`s to `Idle`, reloads
zones for the new `Server.MapName`, re-spawns particle edges. Any in-flight run
is abandoned.

## Commands

All commands are `[ChatCommand("...")]` methods on `ChatCommands.cs`. No
permission checks. Unknown points / incomplete saves produce a chat response
to the caller only.

### Zone editing

| Command | Behavior |
|---|---|
| `!start1` | Raycast from caller's `EyePosition` along `ViewAngles`, `maxDistance=8192`, mask `Solid`, ignoring caller's own pawn. Hit point written to `ZoneEditor._pendingStart.P1`. Chat: `"start p1 set at (x, y, z)"` or `"no surface hit within 8192u"`. |
| `!start2` | Same, writes `_pendingStart.P2`. |
| `!end1` / `!end2` | Same, for `_pendingEnd`. |
| `!savezones` | Requires all 4 points set. For each pair: `Min = ComponentWiseMin(p1,p2)`, `Max = ComponentWiseMax(p1,p2)`. Rejects zero-volume zones (`Min == Max`) with chat error. Writes both rows to `ZoneRepository` for `Server.MapName` (upsert). Invalidates `TimerEngine` zone cache. Despawns old particle edges, spawns new ones. Chat: `"zones saved for <map>"`. |
| `!delzones` | Deletes both zones for the current map. Despawns particles. Resets every `PlayerRun` to `Idle`. Chat: `"zones cleared for <map>"`. |
| `!zones` | Prints current persisted zone corners and whether a pending edit is staged. |

**Pending edits** live in memory only on `ZoneEditor`. A server restart mid-edit
loses unsaved points — saves are always explicit.

### Runs & records

| Command | Behavior |
|---|---|
| `!pb` | `"your PB on <map>: 0:01:23.456"` or `"no PB yet"`. Queries `RecordRepository.GetPb(steamId, map)`. |
| `!top` | Top 10 by `time_ms ASC` on current map. Lines formatted `"1. <name> 0:01:20.111"`. Name column prefers live `CCitadelPlayerController.PlayerName` when connected, falls back to stored `player_name`. |
| `!reset` | Force caller's own `PlayerRun` to `Idle`. Useful if stuck. |

## Database schema

One SQLite file, two tables. Applied via `Data/Migrations/001_initial.sql`
at startup — idempotent `CREATE TABLE IF NOT EXISTS`. `journal_mode=WAL` set
on connection open.

```sql
CREATE TABLE IF NOT EXISTS zones (
    map        TEXT    NOT NULL,
    kind       INTEGER NOT NULL,    -- 0 = start, 1 = end
    min_x      REAL    NOT NULL,
    min_y      REAL    NOT NULL,
    min_z      REAL    NOT NULL,
    max_x      REAL    NOT NULL,
    max_y      REAL    NOT NULL,
    max_z      REAL    NOT NULL,
    updated_at INTEGER NOT NULL,    -- unix epoch seconds
    PRIMARY KEY (map, kind)
);

CREATE TABLE IF NOT EXISTS records (
    steam_id    INTEGER NOT NULL,
    map         TEXT    NOT NULL,
    time_ms     INTEGER NOT NULL,
    player_name TEXT    NOT NULL,
    achieved_at INTEGER NOT NULL,
    PRIMARY KEY (steam_id, map)
);

CREATE INDEX IF NOT EXISTS idx_records_top ON records (map, time_ms);
```

**PB upsert:**
```sql
INSERT INTO records (steam_id, map, time_ms, player_name, achieved_at)
VALUES (@sid, @map, @t, @n, @at)
ON CONFLICT(steam_id, map) DO UPDATE SET
    time_ms     = excluded.time_ms,
    player_name = excluded.player_name,
    achieved_at = excluded.achieved_at
WHERE excluded.time_ms < records.time_ms;
```

`RecordRepository.UpsertIfFaster` returns `(bool changed, int? previousMs)` by
reading the row both before and after the upsert in a single transaction.

**Concurrency:** single-process SQLite. All reads/writes happen on the game
server's main thread (chat-command handlers and `OnGameFrame` run there).
No cross-thread locking needed.

## Particle rendering

Each zone is an AABB with 8 corners and 12 edges. Each edge is rendered as a
dedicated `info_particle_system` entity whose control points are the two
edge endpoints (standard Source 2 beam convention: CP0 = start, CP1 = end,
set via `Builder.WithDataCP(0, start)` / `WithDataCP(1, end)`).

`ZoneRenderer` keeps `Dictionary<int zoneId, List<CParticleSystem>> _handles`.
On zone delete / map change / `OnUnload`, it iterates handles and calls
`particle.Destroy()`.

**Coloring:**
- Start zone → `Color.LimeGreen` via `Builder.WithTint(...)` on CP0.
- End zone → `Color.Red`.

Always visible to everyone — no recipient filtering.

**Particle effect path — open research item.** In priority order:

1. **Preferred:** reuse an existing Deadlock particle that's already a
   CP0→CP1 beam (e.g. ability tether, zipline trail). Implementation phase
   grep the game's `particles/` VPK for `beam` / `tether` / `line` effects.
2. **Fallback:** ship a minimal custom `locktimer_edge.vpcf` in
   `Plugins/LockTimer/content/particles/`, deploy alongside the DLL, and
   register the directory via `Server.AddSearchPath(pluginDir, "GAME")`.
3. **Last resort:** chain of point particles (~one every 32 units along each
   edge) using a plain glowing sprite. Works anywhere, acceptable at most
   zone sizes.

The plan will pick one after a short investigation.

## Error handling & edge cases

**Posture:** fail soft, log loud. The game server keeps running regardless of
plugin errors. Every public boundary (chat command, game-frame tick) wraps its
body in `try/catch` and logs `[LockTimer] <context>: <ex.Message>`. No
exceptions propagate into the engine.

| Case | Behavior |
|---|---|
| `!savezones` before all 4 points set | Chat: `"need 4 points — missing: start2, end1"` |
| Zero-volume zone (Min == Max) | Rejected at save with chat error |
| Start / end overlap or identical | Allowed; FSM naturally handles it (spawning inside end never triggers Running) |
| Player disconnects mid-run | `PlayerRun` removed, no record written |
| Map change mid-run | `OnStartupServer` clears state, no record written |
| Bot player | Never added to engine dictionary — no timing, no records |
| DB file locked / corrupt on open | Logged, plugin disables timing (renders still work if zones are already cached in memory) |
| `Server.MapName` empty at startup | Skip zone load; next `OnStartupServer` retries |
| Crosshair raycast hits nothing | Chat: `"no surface hit within 8192u"`, pending point unchanged |
| Crosshair raycast hits caller's pawn | Filtered via `Trace.Ray(..., ignore: callerPawn)` |
| Elapsed > int32.MaxValue (~24 days) | Clamp and log; effectively impossible in a real run |

## Testing strategy

There is no Deadworks test harness short of the real game server. The test
pyramid is:

1. **Unit tests** — `Plugins/LockTimer.Tests/`, xUnit, net10.0, references
   the main project but *not* `DeadworksManaged.Api.dll`:
   - `TimerEngine` FSM: construct with synthetic zones, step through
     `Tick(slot, position)` calls, assert state transitions and returned
     `FinishedRun?` events. Engine takes positions as inputs — no engine
     interop — so it's pure.
   - `ZoneRepository` and `RecordRepository` against an in-memory SQLite
     connection (`Data Source=:memory:`). Verify schema, upsert semantics,
     top-N query ordering, `UpsertIfFaster` returning `(changed, previous)`.
   - `TimeFormatter.FormatTime`: 0 ms, 1 ms, 999 ms, 1 s, 60 s, 1 h, 24 h.
   - `Zone.Contains(Vector3)`: corners, faces, off-by-epsilon, negative
     coordinates.
2. **Manual in-game smoke checklist** — lives in `Plugins/LockTimer/README.md`:
   - Load plugin → `!start1/2/end1/2/savezones` → 24 glowing edges appear
   - Walk start → end → chat shows time, DB row inserted
   - Run faster → "new PB! prev …" message, DB row updated
   - Run slower → "(pb …)" message, DB unchanged
   - `!delzones` → particles vanish, DB rows gone
   - Disconnect mid-run → reconnect → no state leak
3. **Build verification:** `dotnet build Plugins/LockTimer` must succeed with
   zero warnings against the real `DeadworksManaged.Api.dll` reference.

## Implementation sequencing (preview)

Exact phases go in the plan, but the high-level order is:

1. **Scaffold** — csproj, SQLite deps, `LockTimerPlugin` empty shell, build
   against real Deadworks DLL.
2. **Data layer** — `LockTimerDb`, `Zone`, `Record`, both repositories,
   migration SQL, unit tests for repos.
3. **Timer engine** — `PlayerRun`, `RunState`, pure `TimerEngine.Tick(...)`,
   unit tests for FSM.
4. **Commands** — `ChatCommands` class, zone editing commands, records
   commands, `!reset`, wired through plugin shell.
5. **Particle rendering** — research effect path, `ZoneRenderer`, integrate
   with save/delete/startup.
6. **Integration** — `OnGameFrame` loop, `OnStartupServer` zone load,
   `OnClientPutInServer` / `OnClientDisconnect` state management.
7. **Polish** — error handling wrappers, chat formatting, README smoke
   checklist, final build-clean pass.

Each phase is independently reviewable and produces a green build.
