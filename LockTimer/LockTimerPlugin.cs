using System.IO;
using System.Linq;
using DeadworksManaged.Api;
using LockTimer.Commands;
using LockTimer.Data;
using LockTimer.Hud;
using LockTimer.Records;
using LockTimer.Timing;
using LockTimer.Zones;

namespace LockTimer;

public class LockTimerPlugin : DeadworksPluginBase
{
    public override string Name => "LockTimer";

    private LockTimerDb? _db;
    private ZoneRepository? _zones;
    private RecordRepository? _records;
    private ZoneRenderer? _renderer;
    private TimerEngine? _engine;
    private ZoneEditor? _editor;
    private InteractiveEditor? _interactive;
    private ChatCommands? _commands;
    private SpeedHud? _speedHud;
    private TimerHud? _timerHud;
    private readonly Dictionary<int, ulong> _slotToSteamId = new();
    private readonly Dictionary<int, long> _slotReadyAt = new();
    private readonly Dictionary<int, long> _editConfirmCooldown = new();
    private IHandle? _tickTimer;

    private bool _zonesRendered;
    private Zone? _startZone;
    private Zone? _endZone;


    public override void OnLoad(bool isReload)
    {
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "LockTimer");
            Directory.CreateDirectory(dir);
            var dbPath = Path.Combine(dir, "locktimer.db");

            _db       = LockTimerDb.Open(dbPath);
            _zones    = new ZoneRepository(_db.Connection);
            _records  = new RecordRepository(_db.Connection);
            _renderer    = new ZoneRenderer();
            _engine      = new TimerEngine();
            _editor      = new ZoneEditor(_zones, _engine);
            _interactive = new InteractiveEditor(_editor);
            _commands    = new ChatCommands(_editor, _interactive, _renderer, _records, _engine);
            _speedHud = new SpeedHud();
            _timerHud = new TimerHud();

            // Use Timer.Every instead of OnGameFrame to avoid per-tick native interop
            // overhead that causes thread starvation and client timeouts during connection.
            _tickTimer = Timer.Every(100.Milliseconds(), TickPlayers);

            Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}. DB: {dbPath}");
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
            _db?.Dispose();
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
            if (_zones is null || _engine is null || _renderer is null) return;

            _renderer.ClearAll();
            _engine.ResetAll();

            var map = Server.MapName;
            if (string.IsNullOrEmpty(map)) return;

            var zones = _zones.GetForMap(map);
            var start = zones.FirstOrDefault(z => z.Kind == ZoneKind.Start);
            var end   = zones.FirstOrDefault(z => z.Kind == ZoneKind.End);
            _engine.SetZones(start, end);
            _commands?.SetSavedZones(start, end);
            _startZone = start;
            _endZone = end;
            _zonesRendered = false;

            Console.WriteLine($"[{Name}] Loaded {zones.Count} zone(s) for {map}.");
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
            // Delay ticking this player for 5 seconds to let the pawn fully initialize.
            // Accessing pawn.Position before initialization causes a native segfault.
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
            _interactive?.Remove(args.Slot);
            _editConfirmCooldown.Remove(args.Slot);
            _speedHud?.Remove(args.Slot);
            _timerHud?.Remove(args.Slot);
            _slotToSteamId.Remove(args.Slot);
            _slotReadyAt.Remove(args.Slot);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnClientDisconnect failed: {ex}");
        }
    }

    public override void OnAbilityAttempt(AbilityAttemptEvent args)
    {
        if (_interactive?.IsEditing(args.PlayerSlot) != true) return;

        // Block all combat inputs while in editing mode
        args.Block(InputButton.Attack | InputButton.Attack2);
        args.BlockAllAbilities();
        args.BlockAllItems();

        // Update the cached aim position every frame. The physics raycast only
        // works on the game thread (here), not from Timer callbacks.
        var pawn = args.Controller?.GetHeroPawn();
        if (pawn is not null)
            _interactive.UpdateAim(args.PlayerSlot, pawn);

        // Only react to attack press (rising edge)
        if (!args.IsChanged(InputButton.Attack) || !args.IsHeld(InputButton.Attack)) return;

        // Cooldown prevents double-fire: OnAbilityAttempt fires multiple times
        // per frame (once per ability slot) with the same ChangedButtons state.
        long now = Environment.TickCount64;
        if (_editConfirmCooldown.TryGetValue(args.PlayerSlot, out var until) && now < until)
            return;
        _editConfirmCooldown[args.PlayerSlot] = now + 500;

        var outcome = _interactive.Confirm(args.PlayerSlot);
        switch (outcome)
        {
            case ConfirmOutcome.Corner1Set:
                Chat.PrintToChat(args.PlayerSlot,
                    "[LockTimer] Corner 1 placed. Aim at the opposite corner and shoot.");
                break;
            case ConfirmOutcome.StartZoneReady:
                _editConfirmCooldown.Remove(args.PlayerSlot);
                AutoSaveZone(args.PlayerSlot, ZoneKind.Start);
                break;
            case ConfirmOutcome.EndZoneReady:
                _editConfirmCooldown.Remove(args.PlayerSlot);
                AutoSaveZone(args.PlayerSlot, ZoneKind.End);
                break;
        }
    }

    private void AutoSaveZone(int slot, ZoneKind kind)
    {
        if (_editor is null || _engine is null || _renderer is null) return;

        var zone = _editor.SaveSingleZone(kind, Server.MapName, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        if (zone is null)
        {
            Chat.PrintToChat(slot, $"[LockTimer] {kind} zone has zero volume — not saved.");
            return;
        }

        // Update cached zones and engine
        if (kind == ZoneKind.Start)
            _startZone = zone;
        else
            _endZone = zone;

        _engine.SetZones(_startZone, _endZone);
        _commands?.SetSavedZones(_startZone, _endZone);

        // Re-render all zones
        _renderer.ClearAll();
        if (_startZone is not null) _renderer.Render(_startZone);
        if (_endZone is not null) _renderer.Render(_endZone);

        Chat.PrintToChat(slot, $"[LockTimer] {kind} zone saved!");
    }

    private void TickPlayers()
    {
        if (_engine is null || _records is null) return;

        try
        {
            long now = Environment.TickCount64;

            foreach (var controller in Players.GetAll())
            {
                int slot = controller.EntityIndex - 1;

                // Skip players whose pawn may not be fully initialized yet
                if (_slotReadyAt.TryGetValue(slot, out var readyAt) && now < readyAt)
                    continue;

                var pawn = controller.GetHeroPawn();
                if (pawn is null) continue;

                // Render zone markers once the first player is fully connected.
                // Can't do this at server startup — entity system isn't ready yet.
                if (!_zonesRendered && _renderer is not null)
                {
                    _zonesRendered = true;
                    try
                    {
                        if (_startZone is not null) _renderer.Render(_startZone);
                        if (_endZone is not null) _renderer.Render(_endZone);
                        Console.WriteLine($"[{Name}] Zone markers rendered.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[{Name}] Zone render failed: {ex}");
                    }
                }

                _speedHud?.Tick(slot, pawn);
                _interactive?.Tick(slot);

                var run = _engine.GetRun(slot);
                var finished = _engine.Tick(slot, pawn.Position, now);

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

    private void OnRunFinished(CCitadelPlayerController player, FinishedRun run)
    {
        if (_records is null) return;

        // SteamId comes from the slot→steamid map populated by OnClientConnect.
        int slot = player.EntityIndex - 1;
        if (!_slotToSteamId.TryGetValue(slot, out var steamId))
            return; // player has no tracked SteamId yet — skip the record write

        var result = _records.UpsertIfFaster(
            steamId: (long)steamId,
            map: Server.MapName,
            timeMs: run.ElapsedMs,
            playerName: player.PlayerName,
            nowUnix: DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var formatted = TimeFormatter.FormatTime(run.ElapsedMs);
        string msg;
        if (result.Changed && result.PreviousMs is null)
            msg = $"[LockTimer] {player.PlayerName} finished in {formatted} (new PB!)";
        else if (result.Changed)
            msg = $"[LockTimer] {player.PlayerName} finished in {formatted} " +
                  $"(new PB! prev {TimeFormatter.FormatTime(result.PreviousMs!.Value)})";
        else
            msg = $"[LockTimer] {player.PlayerName} finished in {formatted} " +
                  $"(pb {TimeFormatter.FormatTime(result.PreviousMs!.Value)})";

        // Chat.SayToAll does not exist; use Chat.PrintToChatAll (Chat.cs line 25).
        Chat.PrintToChatAll(msg);
    }

    // --- Chat command wrappers ---
    // The plugin loader scans this class's methods for [ChatCommand] attributes
    // (PluginLoader.ChatCommands.cs line 59). Each wrapper delegates to _commands.

    [ChatCommand("start")]
    public HookResult OnStartInteractive(ChatCommandContext ctx)
        => _commands?.OnStartInteractive(ctx) ?? HookResult.Continue;

    [ChatCommand("end")]
    public HookResult OnEndInteractive(ChatCommandContext ctx)
        => _commands?.OnEndInteractive(ctx) ?? HookResult.Continue;

    [ChatCommand("cancel")]
    public HookResult OnCancelEdit(ChatCommandContext ctx)
        => _commands?.OnCancelEdit(ctx) ?? HookResult.Continue;

    [ChatCommand("start1")]
    public HookResult OnStart1(ChatCommandContext ctx)
        => _commands?.OnStart1(ctx) ?? HookResult.Continue;

    [ChatCommand("start2")]
    public HookResult OnStart2(ChatCommandContext ctx)
        => _commands?.OnStart2(ctx) ?? HookResult.Continue;

    [ChatCommand("end1")]
    public HookResult OnEnd1(ChatCommandContext ctx)
        => _commands?.OnEnd1(ctx) ?? HookResult.Continue;

    [ChatCommand("end2")]
    public HookResult OnEnd2(ChatCommandContext ctx)
        => _commands?.OnEnd2(ctx) ?? HookResult.Continue;

    [ChatCommand("savezones")]
    public HookResult OnSaveZones(ChatCommandContext ctx)
        => _commands?.OnSaveZones(ctx) ?? HookResult.Continue;

    [ChatCommand("delzones")]
    public HookResult OnDelZones(ChatCommandContext ctx)
        => _commands?.OnDelZones(ctx) ?? HookResult.Continue;

    [ChatCommand("zones")]
    public HookResult OnZonesStatus(ChatCommandContext ctx)
        => _commands?.OnZonesStatus(ctx) ?? HookResult.Continue;

    [ChatCommand("pb")]
    public HookResult OnPb(ChatCommandContext ctx)
    {
        if (_commands is null) return HookResult.Continue;
        int slot = ctx.Message.SenderSlot;
        long sid = _slotToSteamId.TryGetValue(slot, out var s) ? (long)s : 0;
        return _commands.OnPb(ctx, sid);
    }

    [ChatCommand("top")]
    public HookResult OnTop(ChatCommandContext ctx)
        => _commands?.OnTop(ctx) ?? HookResult.Continue;

    [ChatCommand("reset")]
    public HookResult OnReset(ChatCommandContext ctx)
        => _commands?.OnReset(ctx) ?? HookResult.Continue;

    [ChatCommand("pos")]
    public HookResult OnPos(ChatCommandContext ctx)
    {
        var pawn = ctx.Controller?.GetHeroPawn();
        if (pawn is null) return HookResult.Handled;
        var p = pawn.Position;
        Chat.PrintToChat(ctx.Message.SenderSlot,
            $"[LockTimer] pos: ({p.X:F1}, {p.Y:F1}, {p.Z:F1})");
        if (_startZone is not null)
        {
            var s = _startZone;
            Chat.PrintToChat(ctx.Message.SenderSlot,
                $"[LockTimer] start: ({s.Min.X:F1},{s.Min.Y:F1},{s.Min.Z:F1}) -> ({s.Max.X:F1},{s.Max.Y:F1},{s.Max.Z:F1}) in={s.Contains(p)}");
        }
        if (_endZone is not null)
        {
            var e = _endZone;
            Chat.PrintToChat(ctx.Message.SenderSlot,
                $"[LockTimer] end: ({e.Min.X:F1},{e.Min.Y:F1},{e.Min.Z:F1}) -> ({e.Max.X:F1},{e.Max.Y:F1},{e.Max.Z:F1}) in={e.Contains(p)}");
        }
        return HookResult.Handled;
    }

    [ChatCommand("speed")]
    public HookResult OnSpeed(ChatCommandContext ctx)
    {
        if (_speedHud is null) return HookResult.Continue;
        var pawn = ctx.Controller?.GetHeroPawn();
        if (pawn is null) return HookResult.Handled;
        int slot = ctx.Message.SenderSlot;
        bool enabled = _speedHud.Toggle(slot, pawn);
        Chat.PrintToChat(slot, $"[LockTimer] speed HUD {(enabled ? "enabled" : "disabled")}");
        return HookResult.Handled;
    }
}
