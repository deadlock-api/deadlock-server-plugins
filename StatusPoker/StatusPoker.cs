using System.Net.Http.Json;
using DeadworksManaged.Api;

namespace StatusPoker;

public class StatusPoker : DeadworksPluginBase
{
    public override string Name => "StatusPoker";

    private static readonly HttpClient Http = new();
    private System.Threading.Timer? _pokerTimer;

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}!");

        _pokerTimer = new System.Threading.Timer(_ => SendPoke(), null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        Console.WriteLine($"[{Name}] Poking every 10 seconds");
    }

    private async void SendPoke()
    {
        try
        {
            var response = await Http.PostAsJsonAsync(
                "https://postman-echo.com/post",
                new { hello = "world" });

            Console.WriteLine($"[{Name}] POST {response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] POST failed: {ex.Message}");
        }
    }

    public override void OnUnload()
    {
        _pokerTimer?.Dispose();
        Console.WriteLine($"[{Name}] Unloaded!");
    }
}
