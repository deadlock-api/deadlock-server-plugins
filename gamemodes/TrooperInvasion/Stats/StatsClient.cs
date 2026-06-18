using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace TrooperInvasion.Stats;

// PostHog /capture/ client. Fire-and-forget: never await from a game thread.
// Mirrors the discipline in LockTimer/Records/MetricsClient.cs — one shared
// HttpClient, single 10s timeout, catch-and-log, no retries. Silent no-op if
// either DEADWORKS_ENV_POSTHOG_KEY or DEADWORKS_ENV_STATS_SALT is unset, so the
// gamemode still works in dev without creds.
internal static class StatsClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static string? _host;
    private static string? _apiKey;
    private static byte[]? _saltBytes;
    private static string? _serverId;
    private static string? _gameMode;
    private static string? _region;
    private static bool _enabled;

    public static bool Enabled => _enabled;
    public static string? ServerId => _serverId;

    public static void Configure()
    {
        // Stats is best-effort: any bootstrap failure leaves _enabled=false so
        // the gamemode keeps running. Never throw out of Configure — the
        // plugin's OnLoad would propagate it.
        try
        {
            var host = Env("POSTHOG_HOST") ?? "https://us.i.posthog.com";
            var key = Env("POSTHOG_KEY");
            var salt = Env("STATS_SALT");
            var serverId = Env("STATS_SERVER_ID");
            _gameMode = Env("GAME_MODE") ?? "trooper-invasion";
            _region = Env("REGION") ?? "";

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(salt))
            {
                _enabled = false;
                Console.WriteLine("[TI.Stats] Disabled: POSTHOG_KEY or STATS_SALT missing.");
                return;
            }

            _host = host.TrimEnd('/');
            _apiKey = key;
            // STATS_SALT must be stable across restarts to keep cross-session player
            // grouping working. Rotating it resets all player identities.
            _saltBytes = Encoding.UTF8.GetBytes(salt);
            _serverId = string.IsNullOrWhiteSpace(serverId) ? Guid.NewGuid().ToString("N") : serverId;
            _enabled = true;
            Console.WriteLine($"[TI.Stats] Enabled: host={_host} server={_serverId} mode={_gameMode} region={_region}");
        }
        catch (Exception ex)
        {
            _enabled = false;
            Console.WriteLine($"[TI.Stats] Disabled: Configure failed: {ex.Message}");
        }
    }

    // HMAC-SHA256(salt, steamid_le_bytes) → 64-char lowercase hex. Returns null
    // if stats are disabled or hashing fails, so callers can decide whether to
    // emit at all.
    public static string? HashSteamId(ulong steamId)
    {
        try
        {
            if (_saltBytes == null) return null;
            var input = new byte[8];
            BitConverter.TryWriteBytes(input, steamId);
            using var hmac = new HMACSHA256(_saltBytes);
            var hash = hmac.ComputeHash(input);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TI.Stats] HashSteamId failed: {ex.Message}");
            return null;
        }
    }

    // distinctId null → event attributed to the server instance (aggregate-only events).
    // Wrapped so any synchronous failure (e.g. Task scheduler refusing the
    // background send) can't bubble into the caller's game-tick path.
    public static void Capture(string eventName, string? distinctId = null, IDictionary<string, object?>? props = null)
    {
        if (!_enabled) return;
        try
        {
            _ = SendAsync(eventName, distinctId ?? _serverId!, props);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TI.Stats] capture '{eventName}' dispatch failed: {ex.Message}");
        }
    }

    private static async Task SendAsync(string eventName, string distinctId, IDictionary<string, object?>? props)
    {
        try
        {
            var payload = new Dictionary<string, object?>
            {
                ["$lib"] = "deadlock-stats",
                ["server_id"] = _serverId,
                ["game_mode"] = _gameMode,
                ["region"] = _region,
            };
            if (props != null)
                foreach (var kv in props) payload[kv.Key] = kv.Value;

            var body = new
            {
                api_key = _apiKey,
                @event = eventName,
                distinct_id = distinctId,
                timestamp = DateTime.UtcNow.ToString("o"),
                properties = payload,
            };

            var request = new HttpRequestMessage(HttpMethod.Post, $"{_host}/capture/")
            {
                Content = JsonContent.Create(body),
            };
            var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                Console.WriteLine($"[TI.Stats] capture '{eventName}' HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[TI.Stats] capture '{eventName}' failed: {ex.Message}");
        }
    }

    private static string? Env(string key) =>
        Environment.GetEnvironmentVariable($"DEADWORKS_ENV_{key}");
}
