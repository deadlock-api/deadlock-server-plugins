using DeadworksManaged.Api;

namespace FlexSlotUnlock;

public class FlexSlotUnlockPlugin : DeadworksPluginBase
{
    public override string Name => "FlexSlotUnlock";

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}");
    }

    public override void OnStartupServer()
    {
        Unlock();
    }

    public override void OnClientFullConnect(ClientFullConnectEvent args)
    {
        // Per-team CCitadelTeam entities only exist once a player is on the team,
        // so a startup-only unlock no-ops for teams that haven't been populated yet.
        // Re-run on every join — idempotent.
        Timer.Once(1.Seconds(), Unlock).CancelOnMapChange();
    }

    private static void Unlock()
    {
        Server.ExecuteCommand("sv_cheats 1");
        Server.ExecuteCommand("citadel_unlock_flex_slots");
        Server.ExecuteCommand("sv_cheats 0");
    }

    public override void OnUnload()
    {
        Console.WriteLine($"[{Name}] Unloaded");
    }
}
