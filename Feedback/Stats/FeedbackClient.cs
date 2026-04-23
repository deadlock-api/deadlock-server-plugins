using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace Feedback.Stats;

// Fire-and-forget PostHog /capture/ client: never await from a game thread.
// Silent no-op when POSTHOG_KEY / STATS_SALT unset so dev runs creds-free.
internal static class FeedbackClient
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

    public static void Configure()
    {
        try
        {
            var host = Env("POSTHOG_HOST") ?? "https://us.i.posthog.com";
            var key = Env("POSTHOG_KEY");
            var salt = Env("STATS_SALT");
            var serverId = Env("STATS_SERVER_ID");
            _gameMode = Env("GAME_MODE") ?? "unknown";
            _region = Env("REGION") ?? "";

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(salt))
            {
                _enabled = false;
                Console.WriteLine("[Feedback] PostHog disabled: POSTHOG_KEY or STATS_SALT missing.");
                return;
            }

            _host = host.TrimEnd('/');
            _apiKey = key;
            // STATS_SALT must match what other plugins use so hashed distinct_ids
            // line up across events from the same player.
            _saltBytes = Encoding.UTF8.GetBytes(salt);
            _serverId = string.IsNullOrWhiteSpace(serverId) ? Guid.NewGuid().ToString("N") : serverId;
            _enabled = true;
            Console.WriteLine($"[Feedback] PostHog enabled: host={_host} server={_serverId} mode={_gameMode} region={_region}");
        }
        catch (Exception ex)
        {
            _enabled = false;
            Console.WriteLine($"[Feedback] PostHog disabled: Configure failed: {ex.Message}");
        }
    }

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
            Console.WriteLine($"[Feedback] HashSteamId failed: {ex.Message}");
            return null;
        }
    }

    public static void Capture(string eventName, string? distinctId = null, IDictionary<string, object?>? props = null)
    {
        if (!_enabled) return;
        // Task.Run so payload allocation, DateTime formatting, and the DNS/connection
        // sync prefix of HttpClient.SendAsync all happen off the game thread.
        _ = Task.Run(() => SendAsync(eventName, distinctId ?? _serverId!, props));
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
                Console.WriteLine($"[Feedback] capture '{eventName}' HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Feedback] capture '{eventName}' failed: {ex.Message}");
        }
    }

    private static string? Env(string key) =>
        Environment.GetEnvironmentVariable($"DEADWORKS_ENV_{key}");
}
