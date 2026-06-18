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
    private string _playerCountFile = null!;

    public override void OnLoad(bool isReload)
    {
        _apiBase = Env("API_BASE", "https://api.deadlock-api.com");
        _secret = EnvRequired("SECRET");
        _gameMode = EnvRequired("GAME_MODE");
        _region = EnvRequired("REGION");
        _ip = EnvRequired("IP");
        _port = ushort.Parse(EnvRequired("PORT"));
        _intervalSeconds = int.Parse(Env("INTERVAL_SECONDS", "30"));
        _playerCountFile = Env("PLAYER_COUNT_FILE", "/tmp/player_count");

        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")} " +
            $"(server={_serverId}, mode={_gameMode}, region={_region}, " +
            $"endpoint={_ip}:{_port}, interval={_intervalSeconds}s, " +
            $"count-file={_playerCountFile})");
    }

    public override void OnStartupServer()
    {
        var regionId = ResolveRegionId(_region);
        ConVar.Find("sv_region")?.SetInt(regionId);
        Console.WriteLine($"[{Name}] sv_region={regionId} (from \"{_region}\")");

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
        var playerCount = GetCurrentPlayerCount();
        WritePlayerCountFile(playerCount);

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
                    hostname = ConVar.Find("hostname")?.GetString() ?? "",
                    current_player_count = (uint)playerCount,
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

    // Atomic write so the pre-update hook (watchtower) never reads a torn value.
    private void WritePlayerCountFile(int count)
    {
        try
        {
            var tmp = _playerCountFile + ".tmp";
            File.WriteAllText(tmp, count.ToString() + "\n");
            File.Move(tmp, _playerCountFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{Name}] Failed to write player count file " +
                $"({_playerCountFile}): {ex.Message}");
        }
    }

    private static readonly (int Id, string[] Aliases)[] RegionAliases =
    {
        (0,   new[] { "0", "us-east", "useast", "use", "na-east", "naeast", "nae", "east", "us", "na", "usa", "america", "north-america", "northamerica" }),
        (1,   new[] { "1", "us-west", "uswest", "usw", "na-west", "nawest", "naw", "west" }),
        (2,   new[] { "2", "sa", "south-america", "southamerica", "br", "brazil", "latam", "latin-america" }),
        (3,   new[] { "3", "eu", "europe", "eu-west", "euwest", "eu-east", "eueast", "euw", "eue" }),
        (4,   new[] { "4", "asia", "as", "sea", "southeast-asia", "southeastasia", "cn", "china", "jp", "japan", "kr", "korea" }),
        (5,   new[] { "5", "au", "oce", "oceania", "australia", "anz" }),
        (6,   new[] { "6", "me", "middle-east", "middleeast", "mena" }),
        (7,   new[] { "7", "af", "africa" }),
        (255, new[] { "255", "world", "any", "all", "global" }),
    };

    private static int ResolveRegionId(string input)
    {
        var key = (input ?? "").Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        if (string.IsNullOrEmpty(key)) return -1;
        foreach (var (id, aliases) in RegionAliases)
            if (Array.IndexOf(aliases, key) >= 0) return id;
        return -1;
    }

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
