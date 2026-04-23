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
        // Engine-native sweep: clears disconnected players from any teams.
        // Bracketed with sv_cheats because the concommand is cheat-flagged.
        Server.ExecuteCommand("sv_cheats 1");
        Server.ExecuteCommand("citadel_kick_disconnected_players");
        Server.ExecuteCommand("sv_cheats 0");
    }

    public override void OnUnload()
    {
        Console.WriteLine($"[{Name}] Unloaded");
    }
}
