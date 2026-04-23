using DeadworksManaged.Api;
using Feedback.Stats;

namespace Feedback;

public class FeedbackPlugin : DeadworksPluginBase
{
    public override string Name => "Feedback";

    // PostHog event string is truncated at 255 chars by the backend; keep
    // messages below that and chop here so the caller sees the same cutoff.
    private const int MaxMessageLength = 240;

    public override void OnLoad(bool isReload)
    {
        FeedbackClient.Configure();
        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}");
    }

    public override void OnUnload()
    {
        Console.WriteLine($"[{Name}] Unloaded");
    }

    [Command("feedback", Description = "Send feedback to the server admins")]
    public void CmdFeedback(CCitadelPlayerController caller, params string[] messageParts)
    {
        if (messageParts.Length == 0)
            throw new CommandException("[Feedback] Usage: !feedback <message>");

        var message = string.Join(' ', messageParts).Trim();
        if (message.Length == 0)
            throw new CommandException("[Feedback] Message is empty.");

        bool truncated = message.Length > MaxMessageLength;
        if (truncated) message = message[..MaxMessageLength];

        var playerName = caller.PlayerName ?? "";
        var steamId = caller.PlayerSteamId;

        if (!FeedbackClient.Enabled)
        {
            // Creds unset (dev): log locally so feedback isn't silently dropped.
            Console.WriteLine($"[Feedback] steam={steamId} name={playerName}: {message}");
            Chat.PrintToChat(caller.Slot, "[Feedback] Thanks! Your feedback was logged locally.");
            return;
        }

        var distinctId = FeedbackClient.HashSteamId(steamId);
        FeedbackClient.Capture("feedback_submitted", distinctId, new Dictionary<string, object?>
        {
            ["message"] = message,
            ["player_name"] = playerName,
            ["truncated"] = truncated,
        });
        Chat.PrintToChat(caller.Slot, "[Feedback] Thanks! Your feedback was recorded.");
    }
}
