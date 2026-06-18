using DeadworksManaged.Api;

namespace TeamChangeBlock;

public class TeamChangeBlockPlugin : DeadworksPluginBase
{
    public override string Name => "TeamChangeBlock";

    private const string CmdChangeTeam = "changeteam";
    private const string CmdJoinTeam = "jointeam";

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}");
    }

    public override HookResult OnClientConCommand(ClientConCommandEvent e)
    {
        if (e.Command == CmdChangeTeam || e.Command == CmdJoinTeam)
            return HookResult.Stop;
        return HookResult.Continue;
    }

    public override void OnUnload()
    {
        Console.WriteLine($"[{Name}] Unloaded");
    }
}
