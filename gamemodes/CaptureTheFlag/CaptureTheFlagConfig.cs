namespace CaptureTheFlag;

// Bound via [PluginConfig]; persisted as CaptureTheFlag/CaptureTheFlag.jsonc and
// hot-reloadable with dw_reloadconfig (re-applies ConVars and the poll interval).
public class CaptureTheFlagConfig
{
    public int CapturesToWin { get; set; } = 3;       // round wins needed to take the match (best-of-5)
    public int RoundsPerMatch { get; set; } = 5;       // max rounds before the match ends regardless
    public float RoundSeconds { get; set; } = 480f;    // round time cap (8 min); on timeout nobody scores
    public float IntermissionSeconds { get; set; } = 5f;

    // Troopers get a little tougher each round (a trooper's health is multiplied by
    // 1 + (round-1)*TrooperHealthScalePerRound, capped at MaxTrooperHealthScale). Kept MUCH
    // lower than TrooperInvasion's PvE curve — here troopers are just lane ambiance in a PvP mode.
    public float TrooperHealthScalePerRound { get; set; } = 0.15f; // r1=1x, r2=1.15x … r5=1.6x
    public float MaxTrooperHealthScale { get; set; } = 2f;
    public float AfkKickSeconds { get; set; } = 600f;  // kick players who don't move for this long

    // Per-round economy. Souls scale from ItemSlots×ItemTierMinPrice in round 1 to
    // ItemSlots×ItemTierMaxPrice in the final round — i.e. ~fully tier-1 early, ~fully tier-4 by
    // the last round of the best-of-N. Ability points scale the same way over the rounds.
    public int ItemSlots { get; set; } = 12;
    public int ItemTierMinPrice { get; set; } = 800;   // tier 1
    public int ItemTierMaxPrice { get; set; } = 6400;  // tier 4
    public int AbilityPointsFirstRound { get; set; } = 1;
    public int AbilityPointsLastRound { get; set; } = 20;
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
