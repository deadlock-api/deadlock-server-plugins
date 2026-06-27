using DeadworksManaged.Api;

namespace CaptureTheFlag;

public partial class CaptureTheFlagPlugin
{
    [Command("flag", Description = "Show the flag's current state and location")]
    public void CmdFlag(CCitadelPlayerController caller)
    {
        string msg = _flag switch
        {
            FlagState.Neutral => "[CTF] Urn: loose (minimap).",
            FlagState.Dropped => $"[CTF] Urn: dropped, resets in {Math.Max(0, _dropDeadline - GlobalVars.CurTime):F0}s.",
            FlagState.Carried => $"[CTF] Urn: {CarrierName()} ({TeamName(_carrierTeam)}).",
            FlagState.Capturing => $"[CTF] Urn: {TeamName(_carrierTeam)} capturing — {Math.Max(0, _captureDeadline - GlobalVars.CurTime):F0}s.",
            _ => "[CTF] Urn: unknown.",
        };
        Chat.PrintToChat(caller, msg);
    }

    [Command("score", Description = "Show the current capture score")]
    public void CmdScore(CCitadelPlayerController caller)
    {
        Chat.PrintToChat(caller, $"[CTF] Amber {_amberCaptures} – {_sapphireCaptures} Sapphire (first to {Config.CapturesToWin})");
    }

    [Command("help", Description = "Show available CaptureTheFlag commands")]
    public void CmdHelp(CCitadelPlayerController caller)
    {
        Chat.PrintToChat(caller, "[CTF] !flag · !score · !stuck");
        Chat.PrintToChat(caller, "[CTF] Melee the urn → enemy base, hold to capture.");
    }

    private string CarrierName() =>
        (_carrierEnt >= 0 ? CBaseEntity.FromIndex<CCitadelPlayerPawn>(_carrierEnt)?.Controller?.PlayerName : null) ?? "someone";
}
