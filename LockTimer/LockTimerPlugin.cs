using System.IO;
using DeadworksManaged.Api;
using LockTimer.Hud;
using LockTimer.Records;
using LockTimer.Runtime;
using LockTimer.Timing;
using LockTimer.Zones;

namespace LockTimer;

public class LockTimerPlugin : DeadworksPluginBase
{
    public override string Name => "LockTimer";

    private string? _zonesYamlPath;
    private ZoneConfig? _zoneConfig;
    private ZoneRenderer? _renderer;
    private TimerEngine? _engine;
    private TimerHud? _timerHud;
    private MetricsClient? _metrics;
    private AutoSpawn? _autoSpawn;
    private readonly Dictionary<int, ulong> _slotToSteamId = new();
    private readonly Dictionary<int, long> _slotReadyAt = new();
    private IHandle? _tickTimer;

    private bool _zonesRendered;
    private Zone? _startZone;
    private Zone? _endZone;
    private IReadOnlyList<Zone> _checkpointZones = Array.Empty<Zone>();
    private IReadOnlyList<string> _checkpointNames = Array.Empty<string>();

    public override void OnLoad(bool isReload)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "LockTimer");
            Directory.CreateDirectory(dir);
            _zonesYamlPath = Path.Combine(dir, "zones.yaml");

            _zoneConfig = ZoneConfig.LoadFromFile(_zonesYamlPath);

            var apiBase  = Env("API_BASE", "https://api.deadlock-api.com");
            var secret   = EnvOrNull("SECRET");
            var serverId = Env("SERVER_ID", Guid.NewGuid().ToString());
            var gameMode = Env("GAME_MODE", "locktimer");
            var region   = Env("REGION", "");
            _metrics = new MetricsClient(apiBase, secret, serverId, gameMode, region);

            _renderer  = new ZoneRenderer();
            _engine    = new TimerEngine();
            _timerHud  = new TimerHud();
            _autoSpawn = new AutoSpawn();

            // Timer.Every avoids the per-tick native interop overhead of OnGameFrame,
            // which caused thread starvation and client timeouts during connection.
            _tickTimer = Timer.Every(100.Milliseconds(), TickPlayers);

            Console.WriteLine(
                $"[{Name}] {(isReload ? "Reloaded" : "Loaded")}. " +
                $"Zones: {_zonesYamlPath} ({_zoneConfig.Maps.Count} maps)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnLoad failed: {ex}");
        }
    }

    public override void OnUnload()
    {
        try
        {
            _tickTimer?.Cancel();
            _renderer?.ClearAll();
            Console.WriteLine($"[{Name}] Unloaded.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnUnload failed: {ex}");
        }
    }

    public override void OnStartupServer()
    {
        try
        {
            if (_engine is null || _renderer is null || _zonesYamlPath is null) return;

            _renderer.ClearAll();
            _engine.ResetAll();

            // Re-read YAML on every map change so admins can edit zones between
            // matches without reloading the plugin.
            _zoneConfig = ZoneConfig.LoadFromFile(_zonesYamlPath);

            var map = Server.MapName;
            if (string.IsNullOrEmpty(map))
            {
                Console.WriteLine($"[{Name}] No map name yet; skipping zone load.");
                return;
            }

            var zoneSet = _zoneConfig.GetForMap(map);
            _startZone = zoneSet.Start;
            _endZone   = zoneSet.End;
            _checkpointZones = zoneSet.Checkpoints;
            _checkpointNames = zoneSet.CheckpointNames;
            _engine.SetZones(_startZone, _endZone, _checkpointZones, _checkpointNames);
            _autoSpawn?.SetStartZone(_startZone);
            _zonesRendered = false;

            if (_startZone is null && _endZone is null)
                Console.WriteLine($"[{Name}] No zones defined for map '{map}' in {_zonesYamlPath}.");
            else
                Console.WriteLine(
                    $"[{Name}] Loaded zones for '{map}': start={(_startZone is null ? "none" : "set")}, " +
                    $"end={(_endZone is null ? "none" : "set")}, checkpoints={_checkpointZones.Count}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnStartupServer failed: {ex}");
        }
    }

    public override bool OnClientConnect(ClientConnectEvent args)
    {
        try
        {
            _slotToSteamId[args.Slot] = args.SteamId;
            // Accessing pawn.Position before the pawn is fully initialized
            // segfaults in native code; wait 5s before ticking this player.
            _slotReadyAt[args.Slot] = Environment.TickCount64 + 5000;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnClientConnect failed: {ex}");
        }
        return true;
    }

    public override void OnClientDisconnect(ClientDisconnectedEvent args)
    {
        try
        {
            _engine?.Remove(args.Slot);
            _timerHud?.Remove(args.Slot);
            _autoSpawn?.OnDisconnect(args.Slot);
            _slotToSteamId.Remove(args.Slot);
            _slotReadyAt.Remove(args.Slot);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnClientDisconnect failed: {ex}");
        }
    }

    private void TickPlayers()
    {
        if (_engine is null) return;

        try
        {
            long now = Environment.TickCount64;

            foreach (var controller in Players.GetAll())
            {
                int slot = controller.EntityIndex - 1;

                if (_slotReadyAt.TryGetValue(slot, out var readyAt) && now < readyAt)
                    continue;

                var pawn = controller.GetHeroPawn();
                if (pawn is null) continue;

                // Zone markers are rendered only after the first player is fully
                // connected — the entity system isn't ready at server startup.
                if (!_zonesRendered && _renderer is not null)
                {
                    _zonesRendered = true;
                    try
                    {
                        if (_startZone is not null) _renderer.Render(_startZone);
                        if (_endZone is not null) _renderer.Render(_endZone);
                        foreach (var cp in _checkpointZones) _renderer.Render(cp);
                        Console.WriteLine($"[{Name}] Zone markers rendered.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{Name}] Zone render failed: {ex}");
                    }
                }

                _autoSpawn?.Tick(controller, pawn);

                var run = _engine.GetRun(slot);
                var finished = _engine.Tick(slot, pawn.Position, now, OnCheckpointHit);

                _timerHud?.Tick(slot, pawn, run, now);

                if (finished is null) continue;

                OnRunFinished(controller, finished.Value);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] TickPlayers failed: {ex}");
        }
    }

    private void OnCheckpointHit(int slot, CheckpointSplit split)
    {
        var formatted = TimeFormatter.FormatTime(split.ElapsedMs);
        Chat.PrintToChat(slot, $"[{Name}] {split.Name} — {formatted}");
    }

    private void OnRunFinished(CCitadelPlayerController player, FinishedRun run)
    {
        int slot = player.EntityIndex - 1;
        _slotToSteamId.TryGetValue(slot, out var steamId);
        string map = Server.MapName;
        string playerName = player.PlayerName;

        _metrics?.SendRunFinished(
            steamId: (long)steamId,
            map: map,
            timeMs: run.ElapsedMs,
            playerName: playerName);

        foreach (var split in run.Splits)
        {
            _metrics?.SendCheckpointTime(
                steamId: (long)steamId,
                map: map,
                checkpointName: split.Name,
                timeMs: split.ElapsedMs,
                playerName: playerName);
        }

        var formatted = TimeFormatter.FormatTime(run.ElapsedMs);
        Chat.PrintToChat(slot, $"[LockTimer] finished in {formatted}");
    }

    [ChatCommand("zones")]
    public HookResult OnZonesStatus(ChatCommandContext ctx)
    {
        var map = Server.MapName;
        string start = _startZone is null ? "none" : "set";
        string end   = _endZone   is null ? "none" : "set";
        Chat.PrintToChat(ctx.Message.SenderSlot,
            $"[{Name}] {map}: start={start} end={end} checkpoints={_checkpointZones.Count}");

        _renderer?.ClearAll();
        if (_startZone is not null) _renderer?.Render(_startZone);
        if (_endZone   is not null) _renderer?.Render(_endZone);
        foreach (var cp in _checkpointZones) _renderer?.Render(cp);
        return HookResult.Handled;
    }

    [ChatCommand("reset")]
    public HookResult OnReset(ChatCommandContext ctx)
    {
        int slot = ctx.Message.SenderSlot;
        _engine?.Remove(slot);
        _autoSpawn?.ResetRun(ctx.Controller);
        Chat.PrintToChat(slot, $"[{Name}] run reset");
        return HookResult.Handled;
    }

    [ChatCommand("pos")]
    public HookResult OnPos(ChatCommandContext ctx)
    {
        var pawn = ctx.Controller?.GetHeroPawn();
        if (pawn is null) return HookResult.Handled;
        int sender = ctx.Message.SenderSlot;
        var p = pawn.Position;
        Chat.PrintToChat(sender, $"[{Name}] pos: ({p.X:F1}, {p.Y:F1}, {p.Z:F1})");
        PrintZone(sender, "start", _startZone, p);
        PrintZone(sender, "end", _endZone, p);
        for (int i = 0; i < _checkpointZones.Count; i++)
            PrintZone(sender, _checkpointNames[i], _checkpointZones[i], p);
        return HookResult.Handled;
    }

    private void PrintZone(int slot, string label, Zone? zone, System.Numerics.Vector3 p)
    {
        if (zone is null) return;
        Chat.PrintToChat(slot,
            $"[{Name}] {label}: ({zone.Min.X:F1},{zone.Min.Y:F1},{zone.Min.Z:F1}) -> " +
            $"({zone.Max.X:F1},{zone.Max.Y:F1},{zone.Max.Z:F1}) in={zone.Contains(p)}");
    }

    private static string Env(string key, string fallback) =>
        Environment.GetEnvironmentVariable($"DEADWORKS_ENV_{key}") ?? fallback;

    private static string? EnvOrNull(string key) =>
        Environment.GetEnvironmentVariable($"DEADWORKS_ENV_{key}");
}
