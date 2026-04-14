using DeadworksManaged.Api;
using LockTimer.Records;
using LockTimer.Timing;
using LockTimer.Zones;

namespace LockTimer.Commands;

public sealed class ChatCommands
{
    private readonly ZoneEditor _editor;
    private readonly InteractiveEditor _interactive;
    private readonly ZoneRenderer _renderer;
    private readonly RecordRepository _records;
    private readonly TimerEngine _engine;
    private Zone? _savedStart;
    private Zone? _savedEnd;

    public ChatCommands(
        ZoneEditor editor,
        InteractiveEditor interactive,
        ZoneRenderer renderer,
        RecordRepository records,
        TimerEngine engine)
    {
        _editor      = editor;
        _interactive = interactive;
        _renderer    = renderer;
        _records     = records;
        _engine      = engine;
    }

    /// <summary>Called by the plugin after loading zones from DB so /zones can render them.</summary>
    public void SetSavedZones(Zone? start, Zone? end)
    {
        _savedStart = start;
        _savedEnd = end;
    }

    // CCitadelPlayerController.GetHeroPawn() returns CCitadelPlayerPawn? directly;
    // no .As<T>() cast needed and no .Pawn property exists on the controller.
    private static CCitadelPlayerPawn? PawnOf(ChatCommandContext ctx)
        => ctx.Controller?.GetHeroPawn();

    // Chat.SayToPlayer does not exist; use Chat.PrintToChat(int slot, string text).
    // SenderSlot is on ChatMessage, accessed via ctx.Message.SenderSlot.
    private static void Reply(ChatCommandContext ctx, string text)
        => Chat.PrintToChat(ctx.Message.SenderSlot, $"[LockTimer] {text}");

    [ChatCommand("start")]
    public HookResult OnStartInteractive(ChatCommandContext ctx)
    {
        var slot = ctx.Message.SenderSlot;
        _interactive.Begin(slot, ZoneKind.Start);
        Reply(ctx, "Aim at the first corner and shoot to place it.");
        return HookResult.Handled;
    }

    [ChatCommand("end")]
    public HookResult OnEndInteractive(ChatCommandContext ctx)
    {
        var slot = ctx.Message.SenderSlot;
        _interactive.Begin(slot, ZoneKind.End);
        Reply(ctx, "Aim at the first corner and shoot to place it.");
        return HookResult.Handled;
    }

    [ChatCommand("cancel")]
    public HookResult OnCancelEdit(ChatCommandContext ctx)
    {
        var slot = ctx.Message.SenderSlot;
        if (!_interactive.IsEditing(slot))
        {
            Reply(ctx, "Not currently editing a zone.");
            return HookResult.Handled;
        }
        _interactive.Cancel(slot);
        Reply(ctx, "Zone editing cancelled.");
        return HookResult.Handled;
    }

    [ChatCommand("start1")]
    public HookResult OnStart1(ChatCommandContext ctx)
    {
        var pawn = PawnOf(ctx); if (pawn is null) return HookResult.Handled;
        Reply(ctx, _editor.CaptureStart1(pawn).Message);
        return HookResult.Handled;
    }

    [ChatCommand("start2")]
    public HookResult OnStart2(ChatCommandContext ctx)
    {
        var pawn = PawnOf(ctx); if (pawn is null) return HookResult.Handled;
        Reply(ctx, _editor.CaptureStart2(pawn).Message);
        return HookResult.Handled;
    }

    [ChatCommand("end1")]
    public HookResult OnEnd1(ChatCommandContext ctx)
    {
        var pawn = PawnOf(ctx); if (pawn is null) return HookResult.Handled;
        Reply(ctx, _editor.CaptureEnd1(pawn).Message);
        return HookResult.Handled;
    }

    [ChatCommand("end2")]
    public HookResult OnEnd2(ChatCommandContext ctx)
    {
        var pawn = PawnOf(ctx); if (pawn is null) return HookResult.Handled;
        Reply(ctx, _editor.CaptureEnd2(pawn).Message);
        return HookResult.Handled;
    }

    [ChatCommand("savezones")]
    public HookResult OnSaveZones(ChatCommandContext ctx)
    {
        var result = _editor.SaveZones(Server.MapName, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Reply(ctx, result.Message);
        if (!result.Ok) return HookResult.Handled;

        _savedStart = result.Start;
        _savedEnd = result.End;
        _renderer.ClearAll();
        if (result.Start is not null) _renderer.Render(result.Start);
        if (result.End is not null) _renderer.Render(result.End);
        return HookResult.Handled;
    }

    [ChatCommand("delzones")]
    public HookResult OnDelZones(ChatCommandContext ctx)
    {
        _editor.DeleteZones(Server.MapName);
        _renderer.ClearAll();
        Reply(ctx, $"zones cleared for {Server.MapName}");
        return HookResult.Handled;
    }

    [ChatCommand("zones")]
    public HookResult OnZonesStatus(ChatCommandContext ctx)
    {
        var p = _editor.GetPendingStatus();
        Reply(ctx, $"pending: start1={p.Start1} start2={p.Start2} end1={p.End1} end2={p.End2}");

        // Re-render zone markers
        _renderer.ClearAll();
        if (_savedStart is not null) _renderer.Render(_savedStart);
        if (_savedEnd is not null) _renderer.Render(_savedEnd);
        return HookResult.Handled;
    }

    [ChatCommand("pb")]
    public HookResult OnPb(ChatCommandContext ctx, long steamId)
    {
        var pb = _records.GetPb(steamId, Server.MapName);
        Reply(ctx, pb is null
            ? "no PB yet"
            : $"your PB on {Server.MapName}: {TimeFormatter.FormatTime(pb.TimeMs)}");
        return HookResult.Handled;
    }

    [ChatCommand("top")]
    public HookResult OnTop(ChatCommandContext ctx)
    {
        var top = _records.GetTop(Server.MapName, limit: 10);
        if (top.Count == 0)
        {
            Reply(ctx, $"no records on {Server.MapName} yet");
            return HookResult.Handled;
        }
        for (int i = 0; i < top.Count; i++)
        {
            var r = top[i];
            Reply(ctx, $"{i + 1}. {r.PlayerName} {TimeFormatter.FormatTime(r.TimeMs)}");
        }
        return HookResult.Handled;
    }

    [ChatCommand("reset")]
    public HookResult OnReset(ChatCommandContext ctx)
    {
        var slot = ctx.Message.SenderSlot;
        _engine.Remove(slot);
        Reply(ctx, "run reset");
        return HookResult.Handled;
    }
}
