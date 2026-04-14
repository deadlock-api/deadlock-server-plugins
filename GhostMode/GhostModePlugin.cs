using DeadworksManaged.Api;

namespace GhostMode;

public class GhostModePlugin : DeadworksPluginBase
{
    public override string Name => "GhostMode";

    private readonly PlayerIsolation _isolation = new();
    private readonly DamageBlocker _damage = new();

    private Heroes _defaultHero;
    private int _defaultTeam;

    public override void OnLoad(bool isReload)
    {
        try
        {
            var heroName = Env("DEFAULT_HERO", "Haze");
            if (!Enum.TryParse<Heroes>(heroName, ignoreCase: true, out _defaultHero))
            {
                Console.WriteLine($"[{Name}] Unknown DEFAULT_HERO '{heroName}', using Haze.");
                _defaultHero = Heroes.Haze;
            }

            var teamStr = Env("DEFAULT_TEAM", "2");
            if (!int.TryParse(teamStr, out _defaultTeam)) _defaultTeam = 2;

            Console.WriteLine(
                $"[{Name}] {(isReload ? "Reloaded" : "Loaded")} (team={_defaultTeam}, hero={_defaultHero}).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnLoad failed: {ex}");
        }
    }

    public override void OnUnload() => Console.WriteLine($"[{Name}] Unloaded.");

    public override void OnStartupServer()
    {
        try
        {
            ServerConfig.Apply();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnStartupServer failed: {ex}");
        }
    }

    public override void OnClientFullConnect(ClientFullConnectEvent args)
    {
        try
        {
            var controller = args.Controller;
            if (controller is null) return;
            controller.ChangeTeam(_defaultTeam);
            controller.SelectHero(_defaultHero);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnClientFullConnect failed: {ex}");
        }
    }

    public override HookResult OnTakeDamage(TakeDamageEvent args)
    {
        try
        {
            return _damage.Handle(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnTakeDamage failed: {ex}");
            return HookResult.Continue;
        }
    }

    public override void OnCheckTransmit(CheckTransmitEvent args)
    {
        try
        {
            _isolation.Handle(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] OnCheckTransmit failed: {ex}");
        }
    }

    private static string Env(string key, string fallback) =>
        Environment.GetEnvironmentVariable($"DEADWORKS_ENV_{key}") ?? fallback;
}
