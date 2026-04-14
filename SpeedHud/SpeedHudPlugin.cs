using DeadworksManaged.Api;

namespace SpeedHud;

public class SpeedHudPlugin : DeadworksPluginBase
{
    public override string Name => "SpeedHud";

    private readonly SpeedHud _hud = new();
    private readonly Dictionary<int, long> _slotReadyAt = new();
    private IHandle? _tickTimer;

    public override void OnLoad(bool isReload)
    {
        try
        {
            _tickTimer = Timer.Every(100.Milliseconds(), Tick);
            Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}.");
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
            Console.WriteLine($"[{Name}] Unloaded.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnUnload failed: {ex}");
        }
    }

    public override bool OnClientConnect(ClientConnectEvent args)
    {
        // Accessing pawn.Position before the pawn is fully initialized
        // segfaults in native code; wait 5s before ticking this player.
        _slotReadyAt[args.Slot] = Environment.TickCount64 + 5000;
        return true;
    }

    public override void OnClientDisconnect(ClientDisconnectedEvent args)
    {
        try
        {
            _hud.Remove(args.Slot);
            _slotReadyAt.Remove(args.Slot);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnClientDisconnect failed: {ex}");
        }
    }

    private void Tick()
    {
        try
        {
            long now = Environment.TickCount64;
            foreach (var controller in Players.GetAll())
            {
                int slot = controller.EntityIndex - 1;
                if (_slotReadyAt.TryGetValue(slot, out var readyAt) && now < readyAt) continue;

                var pawn = controller.GetHeroPawn();
                if (pawn is null) continue;

                _hud.Tick(slot, pawn);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] Tick failed: {ex}");
        }
    }

    [ChatCommand("speed")]
    public HookResult OnSpeed(ChatCommandContext ctx)
    {
        var pawn = ctx.Controller?.GetHeroPawn();
        if (pawn is null) return HookResult.Handled;
        int slot = ctx.Message.SenderSlot;
        bool enabled = _hud.Toggle(slot, pawn);
        Chat.PrintToChat(slot, $"[{Name}] speed HUD {(enabled ? "enabled" : "disabled")}");
        return HookResult.Handled;
    }
}
