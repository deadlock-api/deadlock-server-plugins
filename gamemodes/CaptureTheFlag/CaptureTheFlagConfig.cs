namespace CaptureTheFlag;

// Bound via [PluginConfig]; persisted as CaptureTheFlag/CaptureTheFlag.jsonc and
// hot-reloadable with dw_reloadconfig (re-applies ConVars and the poll interval).
public class CaptureTheFlagConfig
{
    public int CapturesToWin { get; set; } = 3;       // round wins needed to take the match (best-of-5)
    public int RoundsPerMatch { get; set; } = 5;       // max rounds before the match ends regardless
    public float RoundSeconds { get; set; } = 180f;    // round time cap; on timeout nobody scores
    public float IntermissionSeconds { get; set; } = 5f;
    public float AfkKickSeconds { get; set; } = 600f;  // kick players who don't move for this long
    public float CaptureHoldSeconds { get; set; } = 10f;
    public float RespawnDelaySeconds { get; set; } = 5f;
    public float FlagResetSeconds { get; set; } = 30f;
    public float PickupRadius { get; set; } = 150f;
    public int PollIntervalTicks { get; set; } = 4;
    public float WarmupSeconds { get; set; } = 5f;
    public float MatchEndDelaySeconds { get; set; } = 15f;
    public float BaseZoneRadius { get; set; } = 700f;

    // The flag is a prop wearing the game's Idol/Urn model, spawned at center. The real idol
    // pickup entity (CCitadelItemPickupIdol) CANNOT be spawned here — its factory is gated
    // behind the objective system and creating it hard-crashes the server — so a prop_dynamic
    // with the urn model is used instead. IdolClassName may override the prop class but only
    // from the vetted SafeFlagClasses allowlist; anything else is refused. UseIdolEntity=false
    // disables the visible entity entirely (mode still plays via the simulated flag).
    public bool UseIdolEntity { get; set; } = true;
    public string IdolClassName { get; set; } = "";

    // Attempt to spawn the REAL idol pickup (CCitadelItemPickupIdol) from deferred timing for the
    // native urn model + minimap marker. If its factory isn't registered this can hard-crash the
    // server; set false to skip straight to the safe prop fallback.
    public bool UseNativeIdol { get; set; } = true;

    // Carrier weapon lock (the escort mechanic). Confirmed working: the carrier cannot
    // primary/alt fire while holding the flag, so the flag must be escorted.
    public bool CarrierDisableFire { get; set; } = true;

    // Carrier move-speed buff (the "Deadlock twist"). Applied via AddModifier on pickup
    // and removed on drop. DISABLED by default because the exact move-speed modifier
    // VData name must be confirmed on a live server. To enable: set CarrierSpeedModifier
    // to a verified modifier whose VData auto-registers CarrierSpeedProperty (a
    // MODIFIER_VALUE_MOVEMENT_SPEED_MAX_PERCENT property), then dw_reloadconfig. If the
    // modifier can't be applied it is logged once and the mode continues unaffected.
    public string CarrierSpeedModifier { get; set; } = "";
    public string CarrierSpeedProperty { get; set; } = "BonusMoveSpeedPercent";
    public float CarrierSpeedBonusPercent { get; set; } = 50f;
}
