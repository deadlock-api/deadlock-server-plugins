using DeadworksManaged.Api;

namespace DisconnectCleanup;

public class DisconnectCleanupPlugin : DeadworksPluginBase
{
    public override string Name => "DisconnectCleanup";

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}");
    }

    public override void OnClientDisconnect(ClientDisconnectedEvent args)
    {
        // Directly remove the disconnecting player's pawn and controller via the
        // managed API — avoids the cheat-flagged citadel_kick_disconnected_players
        // concommand (no sv_cheats toggle, and scoped to just this client).
        var controller = args.Controller;
        if (controller == null) return;
        controller.GetHeroPawn()?.Remove();
        controller.Remove();
    }

    public override void OnUnload()
    {
        Console.WriteLine($"[{Name}] Unloaded");
    }
}
