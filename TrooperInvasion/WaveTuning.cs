namespace TrooperInvasion;

// Pure difficulty tuning: balance constants and the curves derived from them.
// Every method is a pure function of (player count, wave, round) — no state.
internal static class WaveTuning
{
    // Engine caps squad at 8 ("Squad … is too big!!!" spew), so wave volume
    // comes from pulses × squad, not squad size. Cadence scales 1p → 20s, 32p → 5s.
    public const int MaxSquadSize = 8;
    public const float MinBurstSeconds = 1.0f;
    public const float MaxBurstSeconds = 8f;
    public const float MinPlayers = 1f;
    public const float MaxPlayers = 32f;

    // Rounds bound the wave counter (and catch-up gold) over long sessions.
    // Player-earned progression persists across rounds; only the horde counter resets.
    public const int RoundLength = 10;

    public const float SlowWaveIntervalSeconds = 10f;
    public const float FastWaveIntervalSeconds = 2f;
    public const int MinTrooperCap = 80;
    public const int MaxTrooperCap = 600;

    // R1W1=1.2x, R1W10=3x, R2W1=3.2x, R2W10=5x, R3W10=7x, R5W10=11x, cap=24x (~R12W10).
    public const float HealthScalePerRound = 2f;
    public const float HealthScalePerWave = 0.2f;
    public const float MaxHealthScale = 24f;

    // Respawn delay grows with wave/round: W1R1=3s, W10R1≈9s, W1R2=6s, W10R2≈12s, cap=15s.
    public const float BaseRespawnSeconds = 3f;
    public const float RespawnScalePerWave = 0.7f;
    public const float RespawnScalePerRound = 3f;
    public const float MaxRespawnSeconds = 15f;

    // Deadlock has only 3 lanes (Yellow=1, Blue=4, Purple=6).
    private static readonly int[] _laneMarkers = { 1, 4, 6 };

    // Must match engine-paid `citadel_trooper_gold_reward` (set per wave in
    // RunWave) or the round leaderboard drifts from deposited gold.
    public static int TrooperGoldReward(int wave) => 35 + wave * 5;

    private static float LerpByPlayers(int humans, float atMin, float atMax)
    {
        float clamped = Math.Clamp(humans, MinPlayers, MaxPlayers);
        float t = (clamped - MinPlayers) / (MaxPlayers - MinPlayers);
        return atMin + t * (atMax - atMin);
    }

    public static float ComputeWaveInterval(int humans) =>
        LerpByPlayers(humans, SlowWaveIntervalSeconds, FastWaveIntervalSeconds);

    // At least 2 players per active lane; Deadlock has only 3 lanes.
    public static int ComputeActiveLanes(int humans) => Math.Clamp(humans / 2, 1, 3);

    public static int LaneBitmask(int activeLanes)
    {
        int mask = 0;
        for (int i = 0; i < activeLanes && i < _laneMarkers.Length; i++)
            mask |= _laneMarkers[i];
        return mask;
    }

    public static int ComputeTrooperCap(int humans) =>
        (int)LerpByPlayers(humans, MinTrooperCap, MaxTrooperCap);

    public static float ComputeHealthScale(int roundNum, int waveNum) =>
        Math.Min(MaxHealthScale, 1f + (roundNum - 1) * HealthScalePerRound + waveNum * HealthScalePerWave);

    public static float ComputeRespawnDelay(int roundNum, int waveNum) =>
        Math.Min(MaxRespawnSeconds,
                 BaseRespawnSeconds
                 + (roundNum - 1) * RespawnScalePerRound
                 + (waveNum - 1) * RespawnScalePerWave);

    public static float ComputeBurstSeconds(int waveNum, int humans)
    {
        // Onboarding ramp for first three waves keeps wave-1 tiny even at high counts.
        float ramp = waveNum switch
        {
            1 => 0.35f,
            2 => 0.55f,
            3 => 0.8f,
            _ => 1f,
        };
        return LerpByPlayers(humans, MinBurstSeconds, MaxBurstSeconds) * ramp;
    }
}
