using DeadworksManaged.Api;

namespace StuckCommand;

// Standalone !stuck / !suicide command: kill your own hero to respawn out of a
// stuck spot. Gamemode-agnostic, so it's composed into any gamemode via
// gamemodes.json rather than copied per-mode.
public class StuckCommandPlugin : DeadworksPluginBase
{
    public override string Name => "StuckCommand";

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}");
    }

    [Command("stuck", Description = "Kill yourself to respawn")]
    [Command("suicide", Description = "Kill yourself to respawn")]
    public void CmdStuck(CCitadelPlayerController caller)
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null || !pawn.IsAlive)
            throw new CommandException("[Stuck] Not alive.");
        pawn.Hurt(999_999f);
    }

    public override void OnUnload()
    {
        Console.WriteLine($"[{Name}] Unloaded");
    }
}
