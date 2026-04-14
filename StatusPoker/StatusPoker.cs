using System.Net.Http.Json;
using System.Net.Http.Headers;
using DeadworksManaged.Api;

namespace StatusPoker;

public class StatusPoker : DeadworksPluginBase
{
    public override string Name => "StatusPoker";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private System.Threading.Timer? _pokerTimer;
    private CancellationTokenSource? _cts;

    private string _apiBase = null!;
    private string _secret = null!;
    private readonly string _serverId = Guid.NewGuid().ToString();
    private string _gameMode = null!;
    private string _region = null!;
    private string _ip = null!;
    private ushort _port;
    private int _intervalSeconds;

    public override void OnLoad(bool isReload)
    {
        _apiBase = Env("API_BASE", "https://api.deadlock-api.com");
        _secret = EnvRequired("SECRET");
        _gameMode = EnvRequired("GAME_MODE");
        _region = EnvRequired("REGION");
        _ip = EnvRequired("IP");
        _port = ushort.Parse(EnvRequired("PORT"));
        _intervalSeconds = int.Parse(Env("INTERVAL_SECONDS", "30"));

        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")} " +
            $"(server={_serverId}, mode={_gameMode}, region={_region}, " +
            $"endpoint={_ip}:{_port}, interval={_intervalSeconds}s)");
    }

    public override void OnStartupServer()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        _pokerTimer?.Dispose();
        _pokerTimer = new System.Threading.Timer(
            _ => _ = SendPokeAndReschedule(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
        Console.WriteLine($"[{Name}] Server ready, poking every {_intervalSeconds}s");
    }

    private async Task SendPokeAndReschedule()
    {
        try
        {
            await SendPoke(_cts!.Token);
        }
        finally
        {
            try
            {
                _pokerTimer?.Change(TimeSpan.FromSeconds(_intervalSeconds), Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task SendPoke(CancellationToken ct)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBase}/v1/servers/status")
            {
                Content = JsonContent.Create(new
                {
                    server_id = _serverId,
                    game_mode = _gameMode,
                    region = _region,
                    ip = _ip,
                    port = _port,
                    current_player_count = (uint)GetCurrentPlayerCount(),
                }),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);

            var response = await Http.SendAsync(request, ct);
            Console.WriteLine($"[{Name}] POST {response.StatusCode}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.WriteLine($"[{Name}] POST failed: {ex.Message}");
        }
    }

    private static int GetCurrentPlayerCount() => Players.GetAll().Count();

    public override void OnUnload()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _pokerTimer?.Dispose();
        Console.WriteLine($"[{Name}] Unloaded!");
    }

    private static string Env(string key, string fallback) =>
        Environment.GetEnvironmentVariable($"DEADWORKS_ENV_{key}") ?? fallback;

    private static string EnvRequired(string key) =>
        Environment.GetEnvironmentVariable($"DEADWORKS_ENV_{key}")
        ?? throw new InvalidOperationException($"Missing required env var DEADWORKS_ENV_{key}");
}
