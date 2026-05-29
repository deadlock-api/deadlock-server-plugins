using DeadworksManaged.Api;
using TrooperInvasion.Stats;

namespace TrooperInvasion;

internal sealed class RoundPlayerStats
{
    public string Name = "Unknown";
    public string? HashedSteamId;
    public int HeroId;
    public int Kills;
    public int Bounty;
    public int Deaths;
}

// Per-session and per-round telemetry accumulation. Holds no state the scheduler
// reads back; the plugin owns wave/round numbers and passes them in at emit time.
internal sealed class SessionStats
{
    // Null _sessionStartUtc means no active session — gates session-outcome
    // emission so EndMode → last-disconnect doesn't double-fire.
    private DateTime? _sessionStartUtc;
    private int _peakPlayers;
    private long _playerCountSampleSum;
    private int _playerCountSampleCount;
    private int _deathsThisWave;
    private readonly Dictionary<int, DateTime> _playerJoinTimes = new();
    private readonly Dictionary<int, RoundPlayerStats> _roundStatsBySlot = new();

    public int DeathsPrevWave { get; private set; }

    public void Reset()
    {
        _sessionStartUtc = null;
        _peakPlayers = 0;
        _playerCountSampleSum = 0;
        _playerCountSampleCount = 0;
        _deathsThisWave = 0;
        DeathsPrevWave = 0;
        _playerJoinTimes.Clear();
    }

    public void ClearRoundStats() => _roundStatsBySlot.Clear();

    public void EnsureStarted()
    {
        if (_sessionStartUtc == null) _sessionStartUtc = DateTime.UtcNow;
    }

    public void SamplePlayerCount(int count)
    {
        if (count > _peakPlayers) _peakPlayers = count;
        _playerCountSampleSum += count;
        _playerCountSampleCount++;
    }

    public void RecordDeath() => _deathsThisWave++;

    // Roll this wave's death tally into DeathsPrevWave for the next wave's
    // telemetry, then reset the running counter.
    public void RotateWaveDeaths()
    {
        DeathsPrevWave = _deathsThisWave;
        _deathsThisWave = 0;
    }

    public void RecordJoin(int slot) => _playerJoinTimes[slot] = DateTime.UtcNow;

    // Returns the session duration in seconds (0 if the join wasn't tracked)
    // and forgets the slot.
    public int EndSession(int slot)
    {
        int duration = 0;
        if (_playerJoinTimes.TryGetValue(slot, out var joinTs))
            duration = (int)(DateTime.UtcNow - joinTs).TotalSeconds;
        _playerJoinTimes.Remove(slot);
        return duration;
    }

    // Single-shot: further calls after outcome is emitted become no-ops because
    // _sessionStartUtc is cleared. Prevents EndMode → last-disconnect
    // double-emission when the defeat cascade triggers players to leave.
    public void EmitSessionOutcome(string outcome, int waveNum, int roundNum)
    {
        if (!StatsClient.Enabled || _sessionStartUtc == null) return;
        int duration = (int)(DateTime.UtcNow - _sessionStartUtc.Value).TotalSeconds;
        double avg = _playerCountSampleCount > 0
            ? (double)_playerCountSampleSum / _playerCountSampleCount
            : 0;
        StatsClient.Capture("ti_session_outcome", null, new Dictionary<string, object?>
        {
            ["outcome"] = outcome,
            ["highest_wave"] = waveNum,
            ["highest_round"] = roundNum,
            // Cumulative wave count across rounds — useful for "survived N
            // waves total" comparisons independent of round-length tuning.
            ["total_waves"] = Math.Max(0, roundNum - 1) * WaveTuning.RoundLength + waveNum,
            ["duration_s"] = duration,
            ["peak_players"] = _peakPlayers,
            ["avg_players"] = Math.Round(avg, 2),
        });
        _sessionStartUtc = null;
    }

    // Null controller → null so callers skip attribution without NPE.
    public RoundPlayerStats? EnsureRoundStats(CCitadelPlayerController? controller)
    {
        if (controller == null) return null;
        int slot = controller.Slot;
        if (!_roundStatsBySlot.TryGetValue(slot, out var stats))
        {
            stats = new RoundPlayerStats();
            _roundStatsBySlot[slot] = stats;
        }
        stats.Name = controller.PlayerName ?? stats.Name;
        if (stats.HashedSteamId == null && StatsClient.Enabled)
            stats.HashedSteamId = StatsClient.HashSteamId(controller.PlayerSteamId);
        stats.HeroId = controller.PlayerDataGlobal.HeroID;
        return stats;
    }

    // No-op when empty so double-fire paths (victory → last-disconnect
    // fallback) don't emit a blank header. Clears the tracker before return.
    public void EmitRoundSummary(string outcome, int roundNum, int waveNum)
    {
        if (_roundStatsBySlot.Count == 0) return;

        var ordered = _roundStatsBySlot
            .OrderByDescending(kv => kv.Value.Kills)
            .ThenByDescending(kv => kv.Value.Bounty)
            .ToList();

        Chat.PrintToChatAll($"[TI] Round {roundNum} summary ({waveNum} wave{(waveNum == 1 ? "" : "s")}, {outcome}):");
        int rank = 1;
        foreach (var (_, s) in ordered)
        {
            Chat.PrintToChatAll(
                $"[TI]   {rank}. {s.Name} — {s.Kills} kills · {s.Bounty} souls · {s.Deaths} death{(s.Deaths == 1 ? "" : "s")}");
            rank++;
        }

        if (StatsClient.Enabled)
        {
            foreach (var (_, s) in ordered)
            {
                StatsClient.Capture("ti_round_player_summary", s.HashedSteamId, new Dictionary<string, object?>
                {
                    ["round"] = roundNum,
                    ["waves"] = waveNum,
                    ["outcome"] = outcome,
                    ["kills"] = s.Kills,
                    ["bounty"] = s.Bounty,
                    ["deaths"] = s.Deaths,
                    ["hero_id"] = s.HeroId,
                });
            }
        }

        _roundStatsBySlot.Clear();
    }
}
