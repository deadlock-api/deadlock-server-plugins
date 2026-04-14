using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace LockTimer.Records;

public sealed class MetricsClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string _apiBase;
    private readonly string? _secret;
    private readonly string _serverId;
    private readonly string _gameMode;
    private readonly string _region;

    public MetricsClient(string apiBase, string? secret, string serverId, string gameMode, string region)
    {
        _apiBase  = apiBase.TrimEnd('/');
        _secret   = secret;
        _serverId = serverId;
        _gameMode = gameMode;
        _region   = region;
    }

    public void SendRunFinished(long steamId, string map, int timeMs, string playerName)
    {
        // Fire-and-forget: the game tick must not block on network IO.
        _ = SendAsync(steamId, map, timeMs, playerName);
    }

    private async Task SendAsync(long steamId, string map, int timeMs, string playerName)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{_apiBase}/v1/servers/metrics")
            {
                Content = JsonContent.Create(new
                {
                    account_id = steamId,
                    game_mode = _gameMode,
                    game_mode_version = (string?)null,
                    map = map,
                    metadata = new Dictionary<string, string>
                    {
                        ["player_name"] = playerName,
                    },
                    metric_name = "locktimer_run_time_ms",
                    metric_value = timeMs,
                    region = _region,
                    server_id = _serverId,
                }),
            };
            if (!string.IsNullOrEmpty(_secret))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _secret);

            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                Console.WriteLine($"[LockTimer] metrics POST {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[LockTimer] metrics POST failed: {ex.Message}");
        }
    }
}
