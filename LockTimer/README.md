<p align="center">
  <img src="logo.png" alt="LockTimer" width="400">
</p>

Speedrun timer plugin for [Deadlock](https://store.steampowered.com/app/1422450/Deadlock/) using the [Deadworks](https://github.com/Deadworks-net/deadworks) managed plugin system.

> **Early Development Build** -- This plugin is under active development and is constantly being updated. Expect breaking changes, bugs, and incomplete features. Use at your own risk.

## Requirements

- **Deadlock** (Steam)
- **Deadworks** installed and configured in your Deadlock game directory
- **.NET 10 SDK** for building from source

## Installation

### From source

1. Clone this repository:
   ```
   git clone https://github.com/Oskar-Sterner/lock-timer.git
   cd lock-timer
   ```

2. Set the `DEADLOCK_GAME_DIR` environment variable to your Deadlock installation path:
   ```powershell
   # PowerShell
   $env:DEADLOCK_GAME_DIR = "F:\SteamLibrary\steamapps\common\Deadlock"
   ```
   Or pass it directly to the build:
   ```powershell
   dotnet build -p:DeadlockDir="F:\SteamLibrary\steamapps\common\Deadlock"
   ```

3. Build the plugin. It will auto-deploy to the Deadworks plugins directory:
   ```
   dotnet build
   ```

4. Start `deadworks.exe` and launch Deadlock.

### Manual install

1. Build the project and copy `LockTimer.dll` to:
   ```
   <Deadlock>/game/bin/win64/managed/plugins/
   ```
2. Copy the SQLite dependencies to the same plugins directory:
   - `Microsoft.Data.Sqlite.dll`
   - `SQLitePCLRaw.core.dll`
   - `SQLitePCLRaw.provider.e_sqlite3.dll`
   - `SQLitePCLRaw.batteries_v2.dll`
3. Copy the native SQLite library to the process directory (not plugins):
   ```
   <Deadlock>/game/bin/win64/e_sqlite3.dll
   ```

## Commands

All commands use the `/` prefix in game chat.

| Command | Description |
|---|---|
| `/start1` `/start2` | Set corner 1 / 2 of the start zone at your crosshair |
| `/end1` `/end2` | Set corner 1 / 2 of the end zone at your crosshair |
| `/savezones` | Save both zones for the current map |
| `/delzones` | Delete zones for the current map |
| `/zones` | Show pending zone status and re-render outlines |
| `/pb` | Show your personal best on the current map |
| `/top` | Show top 10 times on the current map |
| `/reset` | Reset your current run |
| `/pos` | Debug: show your position and zone containment status |

## How it works

1. **Set up zones**: Use `/start1` and `/start2` to mark two opposite corners of the start zone, then `/end1` and `/end2` for the end zone. Use `/savezones` to save them.

2. **Run**: Walk into the start zone (green outline). When you leave it, the timer starts. When you enter the end zone (red outline), the timer stops and your time is recorded.

3. **Zone visualization**: Zones are rendered as colored block outlines in the game world. Green = start zone, red = end zone, blue = checkpoint. The outlines appear automatically when a player connects.

4. **Checkpoints (optional)**: Add an ordered `checkpoints` list under a map in `zones.yaml`. The runner must touch each checkpoint in the listed order before the end zone will register a finish. Each checkpoint split is announced in chat and recorded as a `locktimer_checkpoint_time_ms` metric.

   ```yaml
   maps:
     dl_midtown:
       start: { min: [...], max: [...] }
       end:   { min: [...], max: [...] }
       checkpoints:
         - { name: cp1, min: [...], max: [...] }
         - { name: cp2, min: [...], max: [...] }
   ```

5. **Records**: Personal bests are stored per Steam ID and map in a local SQLite database. Times persist across server restarts.

## Database

SQLite database stored at `<Deadlock>/game/bin/win64/LockTimer/locktimer.db`. Contains:

- **zones** -- Zone boundaries per map (start/end bounding boxes)
- **records** -- Personal best times per player per map

## License

MIT
