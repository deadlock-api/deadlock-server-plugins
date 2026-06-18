using DeadworksManaged.Api;

namespace Hostname;

public class HostnamePlugin : DeadworksPluginBase
{
    public override string Name => "Hostname";

    private const string EnvKey = "DEADWORKS_ENV_HOSTNAME";

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}");
    }

    public override void OnStartupServer()
    {
        var value = Environment.GetEnvironmentVariable(EnvKey);
        if (string.IsNullOrWhiteSpace(value))
        {
            Console.WriteLine($"[{Name}] {EnvKey} not set, leaving engine-default hostname in place");
            return;
        }

        // Strip embedded double-quotes so the quoted argument can't be escaped out of.
        var sanitized = value.Replace("\"", "");
        Server.ExecuteCommand($"hostname \"{sanitized}\"");
        Console.WriteLine($"[{Name}] hostname = \"{sanitized}\"");
    }

    public override void OnUnload()
    {
        Console.WriteLine($"[{Name}] Unloaded");
    }
}
