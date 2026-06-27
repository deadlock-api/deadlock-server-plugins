using DeadworksManaged.Api;

namespace CaptureTheFlag;

public partial class CaptureTheFlagPlugin
{
    [Command("flag", Description = "Show the flag's current state and location")]
    public void CmdFlag(CCitadelPlayerController caller)
    {
        string msg = _flag switch
        {
            FlagState.Neutral => "[CTF] Flag: neutral at center.",
            FlagState.Dropped => $"[CTF] Flag: dropped, returns in {Math.Max(0, _dropDeadline - GlobalVars.CurTime):F0}s if unclaimed.",
            FlagState.Carried => $"[CTF] Flag: carried by {CarrierName()} ({TeamName(_carrierTeam)}).",
            FlagState.Capturing => $"[CTF] Flag: {TeamName(_carrierTeam)} capturing — {Math.Max(0, _captureDeadline - GlobalVars.CurTime):F0}s left.",
            _ => "[CTF] Flag: unknown.",
        };
        Chat.PrintToChat(caller, msg);
    }

    [Command("score", Description = "Show the current capture score")]
    public void CmdScore(CCitadelPlayerController caller)
    {
        Chat.PrintToChat(caller, $"[CTF] Amber {_amberCaptures} – {_sapphireCaptures} Sapphire  (first to {Config.CapturesToWin})");
    }

    [Command("help", Description = "Show available CaptureTheFlag commands")]
    public void CmdHelp(CCitadelPlayerController caller)
    {
        Chat.PrintToChat(caller, "[CTF] !flag — flag state    !score — current score    !stuck — respawn");
        Chat.PrintToChat(caller, "[CTF] Grab the flag, escort it into the enemy base, and hold to capture. The carrier can't shoot.");
    }

    private string CarrierName() =>
        (_carrierEnt >= 0 ? CBaseEntity.FromIndex<CCitadelPlayerPawn>(_carrierEnt)?.Controller?.PlayerName : null) ?? "someone";
}
