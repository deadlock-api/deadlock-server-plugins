using DeadworksManaged.Api;
using TrooperInvasion.Stats;

namespace TrooperInvasion;

public class TrooperInvasionConfig
{
}

public class TrooperInvasionPlugin : DeadworksPluginBase
{
    public override string Name => "TrooperInvasion";

    // Humans on Amber. Team 3 gets no human players; the engine still spawns team-3
    // troopers because we keep the spawn system capable of firing — we just gate it
    // ourselves per wave via the citadel_trooper_spawn_enabled ConVar.
    private const int HumanTeam = 2;
    private const int EnemyTeam = 3;

    private const string PatronDesigner = "npc_barrack_boss";

    // Starter stipend + a per-wave catch-up so a late joiner at wave N isn't
    // stuck in tier-0 while everyone else has earned progression.
    private const int StarterGold = 2500;
    private const int CatchUpGoldPerWave = 500;
    // Seeded once per player slot — persists across respawns so death costs you
    // the gold you had earned. Cleared on disconnect so a reconnect gets a fresh seed.
    private readonly HashSet<int> _starterGoldSeeded = new();

    // Wave pacing — ConVar-driven. Each wave enables trooper spawn for a burst
    // window, then disables it, so the engine fires spawn pulses for that window.
    // Engine caps squad size at 8 members ("Squad … is too big!!! Replacing last
    // member" spew when exceeded), so total wave volume comes from pulses × squad.
    // Wave cadence scales linearly with player count: 1p → 20s, 32p → 5s.
    private const int MaxSquadSize = 8;
    // Burst seconds scale linearly with player count; solo play gets small bursts.
    private const float MinBurstSeconds = 1.0f;
    private const float MaxBurstSeconds = 8f;
    private const float FirstWaveGraceSeconds = 10f;
    private const float MinPlayers = 1f;
    private const float MaxPlayers = 32f;
    // Rounds: every RoundLength waves the scheduler pauses for IntermissionSeconds
    // then auto-restarts at wave 1 with fresh onboarding ramp. Without this the
    // wave counter (and late-join catch-up gold) would grow unbounded over a long
    // session. Player-earned progression (items, AP, accumulated gold) persists
    // across rounds; only the horde counter resets.
    private const int RoundLength = 10;
    private const float IntermissionSeconds = 30f;
    // Post-victory/defeat cooldown before the mode auto-rearms round 1. Without
    // this, _modeOver latches true until the last human disconnects, leaving
    // survivors (and any new joiners) stuck in a silent server with no waves.
    private const float PostModeCooldownSeconds = 30f;
    private int _roundNum = 1;
    private const float SlowWaveIntervalSeconds = 20f;
    private const float FastWaveIntervalSeconds = 5f;
    // On-map enemy-trooper cap scales with players: solo can't be buried (small
    // cap), large groups have headroom that matches their kill rate.
    private const int MinTrooperCap = 80;
    private const int MaxTrooperCap = 600;
    // Trooper HP scaling. Rounds are the durability axis (waves add volume),
    // so the per-round step is chunky; the per-wave-within-round bump is
    // gentle, just enough that wave 10 feels tougher than wave 1 inside the
    // same round. Capped to avoid unbounded bullet-sponges after ~R12.
    //   R1W1:  1.00x   (vanilla 300 HP base)
    //   R1W10: 1.30x
    //   R2W1:  1.50x
    //   R3W1:  2.00x
    //   R5W10: 3.30x
    //   cap:   6.00x
    private const float HealthScalePerRound = 0.5f;
    private const float HealthScalePerWave = 0.03f;
    private const float MaxHealthScale = 6f;
    private readonly HashSet<int> _aliveEnemyTroopers = new();
    private static readonly string[] _trooperDesigners = { "npc_trooper", "npc_trooper_boss" };

    // Pending timers tracked so DisarmWaves can cancel them. Without this, a
    // disarm + rapid re-arm would leave stale grace/burst-end timers firing into
    // the new session, producing back-to-back waves or premature spawn cutoff.
    private IHandle? _pendingWaveTimer;
    private IHandle? _pendingBurstEnd;
    // Vote-skip ledger: controller Slots that voted to end the current round
    // early. Cleared on round boundaries (DisarmWaves) so each round gets a
    // fresh ballot.
    private const int VoteSkipPercent = 30;
    private readonly HashSet<int> _voteSkipSlots = new();
    // Auto-armed on first player join, auto-disarmed on last disconnect. The
    // !startwaves / !stopwaves commands still work as manual overrides.
    private bool _wavesActive;

    private int _waveNum;
    private bool _modeOver;

    // Session-scoped stats accumulators. A "session" spans from first-player-
    // joins-empty-server to last-player-leaves (or victory/defeat, whichever
    // comes first). Null _sessionStartUtc means no active session — gates
    // session-outcome emission so HandleDefeat → last-disconnect doesn't
    // double-fire.
    private DateTime? _sessionStartUtc;
    private int _peakPlayers;
    private long _playerCountSampleSum;
    private int _playerCountSampleCount;
    private int _deathsThisWave;
    private int _deathsPrevWave;
    private readonly Dictionary<int, DateTime> _playerJoinTimes = new();

    // Round-scoped per-player leaderboard. Kills + bounty are attributed in
    // OnEntityKilled (engine-paid bounty mirrors the plugin's goldReward
    // formula; boss bonus folds in via EBossKill). Emitted to chat +
    // PostHog at every round-ending path (cleared / victory / defeat /
    // abandoned); cleared by EmitRoundSummary and defensively by DisarmWaves
    // so a fresh round starts at zero.
    private sealed class RoundPlayerStats
    {
        public string Name = "Unknown";
        public string? HashedSteamId;
        public int HeroId;
        public int Kills;
        public int Bounty;
        public int Deaths;
    }
    private readonly Dictionary<int, RoundPlayerStats> _roundStatsBySlot = new();

    [PluginConfig]
    public TrooperInvasionConfig Config { get; set; } = new();

    public override void OnLoad(bool isReload)
    {
        StatsClient.Configure();
        Console.WriteLine(isReload ? "TrooperInvasion reloaded!" : "TrooperInvasion loaded!");
    }

    public override void OnStartupServer()
    {
        // All spawn/tuning ConVars applied here, BEFORE trooper subsystem starts
        // streaming. Mutating these at runtime (mid-frame, from !startwaves) crashed
        // the engine natively — likely because they re-seed spawn-interval tables
        // or resize per-lane buffers and the engine doesn't re-entrancy-guard it.
        try { ConVar.Find("citadel_trooper_spawn_enabled")?.SetInt(0); } catch (Exception ex) { Console.WriteLine($"[TI] convar trooper_spawn: {ex.Message}"); }
        try { ConVar.Find("citadel_allow_purchasing_anywhere")?.SetInt(1); } catch (Exception ex) { Console.WriteLine($"[TI] convar purchasing: {ex.Message}"); }
        try { ConVar.Find("citadel_player_spawn_time_max_respawn_time")?.SetInt(3); } catch (Exception ex) { Console.WriteLine($"[TI] convar respawn_time: {ex.Message}"); }
        try { ConVar.Find("citadel_allow_duplicate_heroes")?.SetInt(1); } catch (Exception ex) { Console.WriteLine($"[TI] convar dup_heroes: {ex.Message}"); }
        // Cap raised far above vanilla 25 so hordes aren't throttled by the per-lane
        // ceiling. Spawn intervals short so pulses fire continuously during the burst
        // window (the !startwaves toggle gates when spawning is eligible; the intervals
        // govern cadence within that window).
        // Per-lane cap kept moderate. With squad=8 × 4 lanes × 1s pulse × 4s burst
        // the engine emits ~128 troopers/wave before friendly-half removal halves
        // that on-map. Going much higher (2048) correlated with native AV crashes
        // during wave spawn, presumably because our OnEntitySpawned Remove() storm
        // collided with the engine's spawn iterator.
        try { ConVar.Find("citadel_trooper_max_per_lane")?.SetInt(256); } catch (Exception ex) { Console.WriteLine($"[TI] convar max_per_lane: {ex.Message}"); }
        try { ConVar.Find("citadel_trooper_spawn_interval_early")?.SetFloat(1f); } catch (Exception ex) { Console.WriteLine($"[TI] convar spawn_interval_early: {ex.Message}"); }
        try { ConVar.Find("citadel_trooper_spawn_interval_late")?.SetFloat(1f); } catch (Exception ex) { Console.WriteLine($"[TI] convar spawn_interval_late: {ex.Message}"); }
        try { ConVar.Find("citadel_trooper_spawn_interval_very_late")?.SetFloat(1f); } catch (Exception ex) { Console.WriteLine($"[TI] convar spawn_interval_very_late: {ex.Message}"); }
        try { ConVar.Find("citadel_trooper_gold_reward_bonus_per_minute")?.SetInt(0); } catch (Exception ex) { Console.WriteLine($"[TI] convar gold_bonus_per_min: {ex.Message}"); }
        try { ConVar.Find("citadel_trooper_spawn_wave_spread")?.SetFloat(2f); } catch (Exception ex) { Console.WriteLine($"[TI] convar wave_spread: {ex.Message}"); }
        try { ConVar.Find("citadel_trooper_spawn_initial")?.SetFloat(0f); } catch (Exception ex) { Console.WriteLine($"[TI] convar spawn_initial: {ex.Message}"); }

        // Fresh-map reset: drop any state that might be stale from the previous
        // map. Timers below use CancelOnMapChange so they self-cancel on map
        // transitions, but the managed state fields live on the plugin instance.
        _wavesActive = false;
        _modeOver = false;
        _waveNum = 0;
        _roundNum = 1;
        _pendingWaveTimer?.Cancel(); _pendingWaveTimer = null;
        _pendingBurstEnd?.Cancel(); _pendingBurstEnd = null;
        _aliveEnemyTroopers.Clear();
        _starterGoldSeeded.Clear();
        _voteSkipSlots.Clear();
        _roundStatsBySlot.Clear();
        ResetSessionStats();

        // Match-clock and GameState left to the engine — the HUD clock runs natively.
        // Patron death is intercepted in OnTakeDamage below so the engine never
        // transitions m_eGameState to PostGame (which would kick all clients).
        // Empty-server cleanup (spawn-disable + trooper cull) is handled inside
        // OnClientDisconnect the moment the last player leaves — no polling interval.
    }

    private void CullAllTroopers()
    {
        // Snapshot the enumeration first — Remove mutates the entity list.
        var victims = Entities.All.Where(e => _trooperDesigners.Contains(e.DesignerName)).Select(e => e.EntityIndex).ToArray();
        foreach (var idx in victims)
        {
            Timer.Once(1.Ticks(), () => CBaseEntity.FromIndex(idx)?.Remove());
        }
        _aliveEnemyTroopers.Clear();
    }

    private int HumanPlayerCount(int excludeEntityIndex = -1) =>
        Players.GetAll().Count(p => p.TeamNum == HumanTeam && p.EntityIndex != excludeEntityIndex);

    // Session-stats bookkeeping. Centralised so join/leave/wave-start/outcome
    // paths can call these without re-implementing the accumulator math.
    private void ResetSessionStats()
    {
        _sessionStartUtc = null;
        _peakPlayers = 0;
        _playerCountSampleSum = 0;
        _playerCountSampleCount = 0;
        _deathsThisWave = 0;
        _deathsPrevWave = 0;
        _playerJoinTimes.Clear();
    }

    private void EnsureSessionStarted()
    {
        if (_sessionStartUtc == null) _sessionStartUtc = DateTime.UtcNow;
    }

    private void SamplePlayerCount(int count)
    {
        if (count > _peakPlayers) _peakPlayers = count;
        _playerCountSampleSum += count;
        _playerCountSampleCount++;
    }

    // Single-shot: further calls after outcome is emitted become no-ops because
    // _sessionStartUtc is cleared. Prevents HandleDefeat → last-disconnect
    // double-emission when the defeat cascade triggers players to leave.
    private void EmitSessionOutcome(string outcome)
    {
        if (!StatsClient.Enabled || _sessionStartUtc == null) return;
        int duration = (int)(DateTime.UtcNow - _sessionStartUtc.Value).TotalSeconds;
        double avg = _playerCountSampleCount > 0
            ? (double)_playerCountSampleSum / _playerCountSampleCount
            : 0;
        StatsClient.Capture("ti_session_outcome", null, new Dictionary<string, object?>
        {
            ["outcome"] = outcome,
            ["highest_wave"] = _waveNum,
            ["highest_round"] = _roundNum,
            // Cumulative wave count across rounds — useful for "survived N
            // waves total" comparisons independent of round-length tuning.
            ["total_waves"] = Math.Max(0, _roundNum - 1) * RoundLength + _waveNum,
            ["duration_s"] = duration,
            ["peak_players"] = _peakPlayers,
            ["avg_players"] = Math.Round(avg, 2),
        });
        _sessionStartUtc = null;
    }

    // Creates-or-refreshes the per-round stat row for a human controller.
    // Null controller (disconnected / partial entity) → null so the caller
    // skips attribution without NPE.
    private RoundPlayerStats? EnsureRoundStats(CCitadelPlayerController? controller)
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

    // Broadcasts a ranked chat leaderboard for the round that just ended and
    // emits one ti_round_player_summary event per tracked player. No-op if
    // the dict is empty, so double-fire paths (victory emits the summary,
    // then last-disconnect tries again) don't produce blank "Round N summary:"
    // headers. Clears the tracker as the final step.
    private void EmitRoundSummary(string outcome, int round, int waves)
    {
        if (_roundStatsBySlot.Count == 0) return;

        var ordered = _roundStatsBySlot
            .OrderByDescending(kv => kv.Value.Kills)
            .ThenByDescending(kv => kv.Value.Bounty)
            .ToList();

        Chat.PrintToChatAll($"[TI] Round {round} summary ({waves} wave{(waves == 1 ? "" : "s")}, {outcome}):");
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
                    ["round"] = round,
                    ["waves"] = waves,
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

    private float ComputeWaveInterval(int humans)
    {
        // Linear interp: 1 player → SlowWaveIntervalSeconds, 32 → FastWaveIntervalSeconds.
        float clamped = Math.Clamp(humans, MinPlayers, MaxPlayers);
        float t = (clamped - MinPlayers) / (MaxPlayers - MinPlayers);
        return SlowWaveIntervalSeconds + t * (FastWaveIntervalSeconds - SlowWaveIntervalSeconds);
    }

    private void ArmWaves()
    {
        if (_modeOver || _wavesActive) return;
        _wavesActive = true;
        Console.WriteLine($"[TI] Wave scheduler armed (round {_roundNum}).");
        AnnounceHud($"ROUND {_roundNum}", $"First wave in {FirstWaveGraceSeconds:0}s — defend the Patron!");
        EnsureSessionStarted();
        StatsClient.Capture("ti_round_started", null, new Dictionary<string, object?>
        {
            ["round"] = _roundNum,
            ["players"] = HumanPlayerCount(),
        });
        _pendingWaveTimer?.Cancel();
        _pendingWaveTimer = Timer.Once(((int)(FirstWaveGraceSeconds * 1000)).Milliseconds(), () =>
        {
            _pendingWaveTimer = null;
            if (!_wavesActive) return;
            RunWave();
        });
    }

    private void DisarmWaves(string reason, bool cullTroopers = true)
    {
        _wavesActive = false;
        _pendingWaveTimer?.Cancel(); _pendingWaveTimer = null;
        _pendingBurstEnd?.Cancel(); _pendingBurstEnd = null;
        Server.ExecuteCommand("citadel_trooper_spawn_enabled 0");
        if (cullTroopers) CullAllTroopers();
        // Reset wave counter so the next session (new player joining later) starts
        // from a fresh onboarding ramp at wave 1, not mid-progression at wave N.
        _waveNum = 0;
        _voteSkipSlots.Clear();
        // Round ended one way or another — drop any leftover stats. Normal
        // round-clear/victory/defeat paths already cleared via EmitRoundSummary,
        // so this is defensive for manual !stopwaves and no-player disarms.
        _roundStatsBySlot.Clear();
        Console.WriteLine($"[TI] Wave scheduler paused ({reason}). {(cullTroopers ? "Troopers culled, " : "Troopers kept alive, ")}wave counter reset.");
    }

    private void ScheduleNextWave()
    {
        if (_modeOver || !_wavesActive) return;
        float interval = ComputeWaveInterval(HumanPlayerCount());
        _pendingWaveTimer?.Cancel();
        _pendingWaveTimer = Timer.Once(((int)(interval * 1000)).Milliseconds(), () =>
        {
            _pendingWaveTimer = null;
            if (!_wavesActive) return;
            RunWave();
        });
    }

    private void BeginIntermission(float postBurstDelaySeconds)
    {
        int completed = _waveNum;
        // The round-end announcement must fire while _wavesActive is still true
        // (otherwise RunWave's continuation guard would cancel us).
        AnnounceHud($"ROUND {_roundNum} CLEARED", $"{completed} waves survived — fresh round in {IntermissionSeconds:0}s");
        EmitRoundSummary("cleared", _roundNum, completed);
        Console.WriteLine($"[TI] Round {_roundNum} complete at wave {completed}. Intermission {IntermissionSeconds:0}s.");

        // Let the burst window finish before culling + announcing intermission start.
        _pendingWaveTimer?.Cancel();
        _pendingWaveTimer = Timer.Once(((int)(postBurstDelaySeconds * 1000)).Milliseconds(), () =>
        {
            _pendingWaveTimer = null;
            // Disarm resets _waveNum=0 but leaves any live troopers alone —
            // carrying them into the intermission/next round is deliberate so
            // players can still fight leftovers during the 30s breather.
            DisarmWaves($"round {_roundNum} complete", cullTroopers: false);
            _roundNum++;
            // Fresh seed for everyone currently on the server — new round, wave-1
            // catch-up (which is 0 bonus gold) will apply to next spawn ritual.
            _starterGoldSeeded.Clear();

            _pendingWaveTimer = Timer.Once(((int)(IntermissionSeconds * 1000)).Milliseconds(), () =>
            {
                _pendingWaveTimer = null;
                if (HumanPlayerCount() > 0) ArmWaves();
                else Console.WriteLine("[TI] Intermission ended with empty server — staying dormant until a player joins.");
            });
        });
    }

    private void BeginPostModeCooldown(string outcome)
    {
        // Clean up wave scheduler state (spawn off, cull troopers, cancel timers,
        // _waveNum=0). _modeOver stays true through the cooldown so new joiners'
        // ArmWaves calls no-op until we explicitly unlatch it below.
        DisarmWaves($"mode over: {outcome}");

        _pendingWaveTimer?.Cancel();
        _pendingWaveTimer = Timer.Once(((int)(PostModeCooldownSeconds * 1000)).Milliseconds(), () =>
        {
            _pendingWaveTimer = null;
            // Full session reset mirroring the last-player-disconnect path, minus
            // _playerJoinTimes (remaining humans' session durations keep counting
            // from their original join — their connection never dropped).
            _modeOver = false;
            _roundNum = 1;
            _waveNum = 0;
            _starterGoldSeeded.Clear();
            _peakPlayers = 0;
            _playerCountSampleSum = 0;
            _playerCountSampleCount = 0;
            _deathsThisWave = 0;
            _deathsPrevWave = 0;

            if (HumanPlayerCount() > 0)
            {
                Console.WriteLine("[TI] Post-mode cooldown ended — rearming round 1.");
                ArmWaves();
            }
            else
            {
                Console.WriteLine("[TI] Post-mode cooldown ended on empty server — staying dormant until a player joins.");
            }
        });
    }

    private static void AnnounceHud(string title, string description)
    {
        // HUD-toast style announcement shown to every player. Used for
        // round-boundary events only — per-wave announcements stay as chat so
        // the HUD isn't spammed every 5-20 seconds at high player counts.
        var msg = new CCitadelUserMsg_HudGameAnnouncement
        {
            TitleLocstring = title,
            DescriptionLocstring = description,
        };
        NetMessages.Send(msg, RecipientFilter.All);
    }

    private int ComputeActiveLanes(int humans)
    {
        // At least 2 players per active lane; clamped to [1, 3] — Deadlock has
        // only 3 lanes now (Yellow=1, Blue=4, Purple=6). Examples:
        //   1-3 players → 1 lane
        //   4-5 players → 2 lanes
        //   6+ players  → 3 lanes
        int lanes = Math.Clamp(humans / 2, 1, 3);
        return lanes;
    }

    private static readonly int[] _laneMarkers = { 1, 4, 6 };
    private static int LaneBitmask(int activeLanes)
    {
        int mask = 0;
        for (int i = 0; i < activeLanes && i < _laneMarkers.Length; i++)
            mask |= _laneMarkers[i];
        return mask;
    }

    private int ComputeTrooperCap(int humans)
    {
        float clampedP = Math.Clamp(humans, MinPlayers, MaxPlayers);
        float t = (clampedP - MinPlayers) / (MaxPlayers - MinPlayers);
        return (int)(MinTrooperCap + t * (MaxTrooperCap - MinTrooperCap));
    }

    private float ComputeHealthScale() =>
        Math.Min(MaxHealthScale, 1f + (_roundNum - 1) * HealthScalePerRound + _waveNum * HealthScalePerWave);

    private void ScaleTrooperHealth(CBaseEntity ent)
    {
        // Writing only m_iMaxHealth leaves the trooper at its vdata baseline
        // (m_nMaxHealth = 300 for npc_trooper, higher for npc_trooper_boss —
        // the boss's own vdata baseline carries through the multiply).
        // m_iHealth doesn't auto-clamp to the new max, so we set both.
        int baseMax = ent.MaxHealth;
        if (baseMax <= 0) return;
        int scaled = (int)(baseMax * ComputeHealthScale());
        ent.MaxHealth = scaled;
        ent.Health = scaled;
    }

    private float ComputeBurstSeconds(int waveNum, int humans)
    {
        // Player-scaled base: 1p → MinBurstSeconds (gentle), 32p → MaxBurstSeconds.
        float clampedP = Math.Clamp(humans, MinPlayers, MaxPlayers);
        float t = (clampedP - MinPlayers) / (MaxPlayers - MinPlayers);
        float playerScaled = MinBurstSeconds + t * (MaxBurstSeconds - MinBurstSeconds);
        // Onboarding ramp for first three waves keeps wave-1 tiny even at high counts.
        float ramp = waveNum switch
        {
            1 => 0.35f,
            2 => 0.55f,
            3 => 0.8f,
            _ => 1f,
        };
        return playerScaled * ramp;
    }

    private void RunWave()
    {
        if (_modeOver || !_wavesActive) return;

        int humans = HumanPlayerCount();
        if (humans == 0)
        {
            DisarmWaves("no players");
            return;
        }

        int cap = ComputeTrooperCap(humans);
        int alive = _aliveEnemyTroopers.Count;
        if (alive >= cap)
        {
            Chat.PrintToChatAll($"[TI] Wave skipped — {alive} troopers still alive (cap {cap})");
            Console.WriteLine($"[TI] Wave skipped: alive={alive} cap={cap} humans={humans}");
            // Still schedule another attempt so kills eventually unblock the round.
            ScheduleNextWave();
            return;
        }

        _waveNum++;
        int goldReward = 70 + _waveNum * 10;
        int activeLanes = ComputeActiveLanes(humans);
        float interval = ComputeWaveInterval(humans);
        float burstSeconds = ComputeBurstSeconds(_waveNum, humans);
        float healthScale = ComputeHealthScale();

        // Stats emission point: deaths-during-the-wave-that-just-ended roll
        // forward into this event's `deaths_prev_wave`, then reset the counter.
        // Sample player count here too — waves fire every 5–20s, which is
        // dense enough to give a reasonable avg_players estimate.
        SamplePlayerCount(humans);
        StatsClient.Capture("ti_wave_started", null, new Dictionary<string, object?>
        {
            ["wave"] = _waveNum,
            ["round"] = _roundNum,
            ["players"] = humans,
            ["deaths_prev_wave"] = _deathsPrevWave,
            ["alive_enemy_troopers"] = alive,
            ["active_lanes"] = activeLanes,
            ["gold_reward"] = goldReward,
            ["health_scale"] = Math.Round(healthScale, 2),
        });
        _deathsPrevWave = _deathsThisWave;
        _deathsThisWave = 0;

        // Route mid-game ConVar writes through Server.ExecuteCommand rather than
        // ConVar.Find().SetInt. The direct-Set path crashed the engine natively on
        // !startwaves — the console path goes through the engine's own CCvar
        // dispatch and is the known-stable surface (same as the `hostname` write
        // at startup and Deathmatch's pattern).
        Server.ExecuteCommand($"citadel_trooper_squad_size {MaxSquadSize}");
        Server.ExecuteCommand($"citadel_trooper_gold_reward {goldReward}");
        Server.ExecuteCommand($"citadel_active_lane {LaneBitmask(activeLanes)}");
        Server.ExecuteCommand("citadel_trooper_spawn_enabled 1");

        AnnounceHud(
            $"WAVE {_waveNum} / {RoundLength}",
            $"Round {_roundNum} — {activeLanes} lane{(activeLanes == 1 ? "" : "s")}, bounty {goldReward}, next in {interval:0}s");
        Console.WriteLine($"[TI] Round {_roundNum} Wave {_waveNum}: gold={goldReward} humans={humans} burst={burstSeconds:0.0}s nextIn={interval:0.0}s lanes={activeLanes} cap={cap} hp×{healthScale:0.00}");

        _pendingBurstEnd?.Cancel();
        _pendingBurstEnd = Timer.Once(((int)(burstSeconds * 1000)).Milliseconds(), () =>
        {
            _pendingBurstEnd = null;
            Server.ExecuteCommand("citadel_trooper_spawn_enabled 0");
        });

        // Round boundary: trigger intermission instead of scheduling another wave.
        if (_waveNum >= RoundLength)
            BeginIntermission(burstSeconds + 2f);
        else
            ScheduleNextWave();
    }

    public override void OnEntitySpawned(EntitySpawnedEvent args)
    {
        // Trooper spawn handler:
        //   - friendly (team-2): cull via deferred Remove (direct Remove inside
        //     OnEntitySpawned during heavy cascades AV'd — see
        //     `2026-04-22-onentityspawned-remove-deferral.md`)
        //   - enemy (team-3): track for the MaxAliveEnemyTroopers cap; also cull
        //     if the server is empty, so an engine-emitted initial trooper never
        //     lingers until the first player shows up.
        var ent = args.Entity;
        var designer = ent.DesignerName;
        if (designer != "npc_trooper" && designer != "npc_trooper_boss") return;
        int idx = ent.EntityIndex;

        if (ent.TeamNum == HumanTeam)
        {
            Timer.Once(1.Ticks(), () => CBaseEntity.FromIndex(idx)?.Remove());
            return;
        }

        // Strict enemy team — neutral/unassigned-team troopers (rare, from map
        // scripting) are left alone, neither tracked nor culled.
        if (ent.TeamNum != EnemyTeam) return;

        int humans = HumanPlayerCount();
        if (humans == 0)
        {
            Timer.Once(1.Ticks(), () => CBaseEntity.FromIndex(idx)?.Remove());
            return;
        }

        _aliveEnemyTroopers.Add(idx);
        ScaleTrooperHealth(ent);
        if (_aliveEnemyTroopers.Count >= ComputeTrooperCap(humans))
            Server.ExecuteCommand("citadel_trooper_spawn_enabled 0");
    }

    public override void OnEntityDeleted(EntityDeletedEvent args)
    {
        _aliveEnemyTroopers.Remove(args.Entity.EntityIndex);
    }

    [GameEventHandler("gameover_msg")]
    public HookResult OnGameoverMsg(GameoverMsgEvent args) => HookResult.Stop;

    [GameEventHandler("round_end")]
    public HookResult OnRoundEnd(RoundEndEvent args) => HookResult.Stop;

    // Intercepts the killing blow on either Patron. Letting the Patron reach 0 HP
    // causes the engine to flip m_eGameState → PostGame at the schema layer,
    // which kicks every client and makes the server refuse joins until map
    // reload. Zeroing the lethal damage + pinning HP to 1 avoids the death
    // entirely; we call HandleVictory/HandleDefeat ourselves to run the
    // cooldown → rearm flow. Non-lethal damage passes through so the HUD still
    // shows Patron HP ticking down through the round.
    public override HookResult OnTakeDamage(TakeDamageEvent args)
    {
        if (args.Entity.DesignerName != PatronDesigner) return HookResult.Continue;

        // Once mode is over, Patron is invulnerable through the cooldown window
        // so stray trooper hits can't re-trigger the defeat handler.
        if (_modeOver)
        {
            args.Info.Damage = 0f;
            return HookResult.Continue;
        }

        if (args.Entity.Health - args.Info.Damage > 0f) return HookResult.Continue;

        args.Info.Damage = 0f;
        args.Entity.Health = 1;
        if (args.Entity.TeamNum == HumanTeam) HandleDefeat();
        else                                   HandleVictory();
        return HookResult.Continue;
    }

    [GameEventHandler("entity_killed")]
    public HookResult OnEntityKilled(EntityKilledEvent args)
    {
        if (_modeOver) return HookResult.Continue;
        var killed = CBaseEntity.FromIndex(args.EntindexKilled);
        if (killed == null) return HookResult.Continue;

        // Patron "deaths" are handled in OnTakeDamage before the hit lands, so
        // npc_barrack_boss never actually reaches entity_killed.

        // Enemy trooper/boss kill attribution — feeds the round summary.
        // Bounty mirrors the engine-paid formula (citadel_trooper_gold_reward
        // is set to this per wave in RunWave); boss bonus is folded in inside
        // the boss-specific branch below.
        if ((killed.DesignerName == "npc_trooper" || killed.DesignerName == "npc_trooper_boss")
            && killed.TeamNum == EnemyTeam)
        {
            var trooperKiller = CBaseEntity.FromIndex<CCitadelPlayerPawn>(args.EntindexAttacker);
            if (trooperKiller != null && trooperKiller.TeamNum == HumanTeam)
            {
                var stats = EnsureRoundStats(trooperKiller.Controller);
                if (stats != null)
                {
                    stats.Kills++;
                    stats.Bounty += 70 + _waveNum * 10;
                }
            }
        }

        // Human-player death: feeds _deathsThisWave (scheduler telemetry),
        // per-slot round summary, and the per-death PostHog event.
        if (killed.TeamNum == HumanTeam)
        {
            var pawn = killed.As<CCitadelPlayerPawn>();
            var controller = pawn?.Controller;
            if (controller != null)
            {
                _deathsThisWave++;
                var stats = EnsureRoundStats(controller);
                if (stats != null) stats.Deaths++;
                if (StatsClient.Enabled)
                {
                    StatsClient.Capture("ti_player_died", stats?.HashedSteamId, new Dictionary<string, object?>
                    {
                        ["wave"] = _waveNum,
                        ["round"] = _roundNum,
                        ["hero_id"] = controller.PlayerDataGlobal.HeroID,
                    });
                }
            }
        }

        return HookResult.Continue;
    }

    private void HandleVictory()
    {
        _modeOver = true;
        Server.ExecuteCommand("citadel_trooper_spawn_enabled 0");
        AnnounceHud("VICTORY!", $"Sapphire Patron destroyed — survived {_waveNum} waves. Fresh round in {PostModeCooldownSeconds:0}s.");
        Console.WriteLine($"[TI] VICTORY at wave {_waveNum}");
        EmitRoundSummary("victory", _roundNum, _waveNum);
        EmitSessionOutcome("victory");
        BeginPostModeCooldown("victory");
    }

    private void HandleDefeat()
    {
        _modeOver = true;
        Server.ExecuteCommand("citadel_trooper_spawn_enabled 0");
        AnnounceHud("DEFEAT", $"Amber Patron has fallen at wave {_waveNum}. Fresh round in {PostModeCooldownSeconds:0}s.");
        Console.WriteLine($"[TI] DEFEAT at wave {_waveNum}");
        EmitRoundSummary("defeat", _roundNum, _waveNum);
        EmitSessionOutcome("defeat");
        BeginPostModeCooldown("defeat");
    }

    public override void OnClientFullConnect(ClientFullConnectEvent args)
    {
        var controller = args.Controller;
        if (controller == null) return;

        var usage = new Dictionary<int, int>();
        foreach (var p in Players.GetAll())
        {
            if (p.EntityIndex == controller.EntityIndex) continue;
            int id = p.PlayerDataGlobal.HeroID;
            if (id > 0) usage[id] = usage.GetValueOrDefault(id) + 1;
        }

        controller.ChangeTeam(HumanTeam);

        var available = Enum.GetValues<Heroes>()
            .Select(h => (hero: h, count: usage.GetValueOrDefault(h.GetHeroData()?.HeroID ?? 0), inGame: h.GetHeroData()?.AvailableInGame == true))
            .Where(x => x.inGame)
            .ToArray();

        int min = available.Min(x => x.count);
        var leastPresent = available.Where(x => x.count == min).Select(x => x.hero).ToArray();
        var hero = leastPresent[Random.Shared.Next(leastPresent.Length)];
        controller.SelectHero(hero);

        Console.WriteLine($"[TI] Slot {args.Slot} -> team {HumanTeam}, hero {hero.ToHeroName()}");

        // Stats: record join time for session_duration_s on leave, emit
        // player_joined, and sample the new player count. Session start itself
        // happens lazily in ArmWaves (just below) via EnsureSessionStarted.
        _playerJoinTimes[controller.Slot] = DateTime.UtcNow;
        if (StatsClient.Enabled)
        {
            var hashed = StatsClient.HashSteamId(controller.PlayerSteamId);
            StatsClient.Capture("ti_player_joined", hashed, new Dictionary<string, object?>
            {
                ["current_wave"] = _waveNum,
                ["current_round"] = _roundNum,
                ["hero_id"] = hero.GetHeroData()?.HeroID ?? 0,
            });
        }
        SamplePlayerCount(HumanPlayerCount());

        // One-shot welcome. The hostname advertises the mode but that's the
        // only cue a first-time joiner gets in-game — anchor them in the
        // current round/wave and point at !help. Deferred a tick so the chat
        // UI is settled before the lines land (same reasoning as DeferredSpawnRitual).
        int welcomeSlot = controller.Slot;
        Timer.Once(1.Ticks(), () =>
        {
            Chat.PrintToChat(welcomeSlot, "[TI] Welcome to Trooper Invasion — all humans defend the Amber Patron vs engine-spawned troopers.");
            Chat.PrintToChat(welcomeSlot,
                _wavesActive && _waveNum > 0
                    ? $"[TI] Currently Round {_roundNum} Wave {_waveNum}/{RoundLength} — kill troopers for gold, don't let the Patron die."
                    : "[TI] First wave begins shortly — kill troopers for gold, don't let the Patron die.");
            Chat.PrintToChat(welcomeSlot, "[TI] Type !help for commands (!hero, !voteskip, !stuck, !wave, …).");
        });

        // Auto-arm wave scheduler on first-player join. Idempotent: ArmWaves no-ops
        // if already active or mode-over.
        ArmWaves();
    }

    private void ApplySpawnRitual(CCitadelPlayerPawn? pawn)
    {
        // Guard every step — pawn can be mid-initialization when these events fire.
        // Empty model in server log ("for entity \"player\"") suggests hero assets
        // aren't fully settled yet, so we wrap each side effect.
        // Healing is handled by the HealOnSpawn plugin.
        try { SeedStarterGold(pawn); } catch (Exception ex) { Console.WriteLine($"[TI] SeedStarterGold: {ex.Message}"); }
    }

    private void SeedStarterGold(CCitadelPlayerPawn? pawn)
    {
        // One-time per-slot seed. Respawns don't re-seed — death should cost you the
        // souls you earned. Reconnects re-seed because OnClientDisconnect clears the slot.
        //
        // Catch-up: joining at wave N gets StarterGold + (N-1) * CatchUpGoldPerWave so a
        // late arrival isn't stuck in tier-0 vs veterans with accumulated progression.
        int slot = pawn?.Controller?.Slot ?? -1;
        if (slot < 0 || !_starterGoldSeeded.Add(slot)) return;
        int seed = StarterGold + Math.Max(0, _waveNum - 1) * CatchUpGoldPerWave;
        pawn?.SetCurrency(ECurrencyType.EGold, seed);
        Console.WriteLine($"[TI] Seeded slot {slot} with {seed} gold (wave={_waveNum})");
    }

    private void DeferredSpawnRitual(CCitadelPlayerPawn? pawn)
    {
        // Defer one tick so the engine finishes hero-asset setup before we touch the pawn.
        if (pawn == null) return;
        int idx = pawn.EntityIndex;
        Timer.Once(1.Ticks(), () =>
        {
            var live = CBaseEntity.FromIndex<CCitadelPlayerPawn>(idx);
            if (live == null) return;
            ApplySpawnRitual(live);
        });
    }

    [GameEventHandler("player_hero_changed")]
    public HookResult OnPlayerHeroChanged(PlayerHeroChangedEvent args)
    {
        DeferredSpawnRitual(args.Userid?.As<CCitadelPlayerPawn>());
        return HookResult.Continue;
    }

    [GameEventHandler("player_respawned")]
    public HookResult OnPlayerRespawned(PlayerRespawnedEvent args)
    {
        DeferredSpawnRitual(args.Userid?.As<CCitadelPlayerPawn>());
        return HookResult.Continue;
    }

    private static readonly string[] _helpLines = {
        "[TI] !help — show this message",
        "[TI] !hero <name> — swap hero (fuzzy match)",
        "[TI] !stuck / !suicide — kill yourself to respawn",
        "[TI] !wave — show current wave",
        "[TI] !startwaves — arm wave engine + begin scheduler",
        "[TI] !stopwaves — halt scheduler (and close spawn window)",
        "[TI] !nextwave — trigger one wave immediately (dev)",
        "[TI] !voteskip — vote to end the current round (>30% of players required)",
    };

    [Command("help", Description = "Show available TrooperInvasion commands")]
    public void CmdHelp(CCitadelPlayerController caller)
    {
        foreach (var line in _helpLines)
            Chat.PrintToChat(caller.Slot, line);
    }

    [Command("wave", Description = "Show current round and wave")]
    public void CmdWave(CCitadelPlayerController caller)
    {
        Chat.PrintToChat(caller.Slot, $"[TI] Round {_roundNum} Wave {_waveNum}/{RoundLength}{(_modeOver ? " (MODE OVER)" : "")}");
    }

    [Command("startwaves", Description = "Manually arm the wave scheduler")]
    public void CmdStartWaves(CCitadelPlayerController caller)
    {
        if (_modeOver) throw new CommandException("[TI] Mode is over.");
        if (_wavesActive) throw new CommandException("[TI] Waves already running.");
        ArmWaves();
    }

    [Command("stopwaves", Description = "Pause the auto wave scheduler")]
    public void CmdStopWaves(CCitadelPlayerController caller)
    {
        DisarmWaves("manual !stopwaves");
        Chat.PrintToChatAll("[TI] Wave scheduler halted.");
    }

    [Command("nextwave", Description = "Trigger the next wave immediately (dev)")]
    public void CmdNextWave(CCitadelPlayerController caller)
    {
        if (_modeOver)
            throw new CommandException("[TI] Mode is over.");
        bool wasActive = _wavesActive;
        _wavesActive = true;
        RunWave();
        _wavesActive = wasActive;
    }

    [Command("voteskip", Description = "Vote to end the current round early (>30% of players)")]
    public void CmdVoteSkip(CCitadelPlayerController caller)
    {
        if (_modeOver) throw new CommandException("[TI] Mode is over.");
        if (!_wavesActive) throw new CommandException("[TI] No active round to skip.");

        bool isFirst = _voteSkipSlots.Count == 0;
        if (!_voteSkipSlots.Add(caller.Slot))
            throw new CommandException("[TI] You already voted to skip this round.");

        int humans = HumanPlayerCount();
        int votes = _voteSkipSlots.Count;
        string who = caller.PlayerName ?? "A player";

        if (isFirst)
            Chat.PrintToChatAll($"[TI] {who} wants to skip round {_roundNum} — type !voteskip to agree (>{VoteSkipPercent}% needed)");

        int percent = humans > 0 ? votes * 100 / humans : 0;
        Chat.PrintToChatAll($"[TI] Vote skip: {votes}/{humans} ({percent}%)");

        // Strict > 30% — 1/3 passes, 1/4 doesn't.
        if (votes * 100 > humans * VoteSkipPercent)
        {
            Chat.PrintToChatAll($"[TI] Vote skip passed — ending round {_roundNum}");
            // Clear immediately so BeginIntermission's DisarmWaves doesn't
            // re-process an already-tallied ballot after the round boundary.
            _voteSkipSlots.Clear();
            BeginIntermission(0f);
        }
    }

    [Command("stuck", Description = "Kill yourself to respawn")]
    [Command("suicide", Description = "Kill yourself to respawn")]
    public void CmdStuck(CCitadelPlayerController caller)
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null || !pawn.IsAlive)
            throw new CommandException("[TI] Not alive.");
        pawn.Hurt(999_999f);
    }

    public override void OnClientDisconnect(ClientDisconnectedEvent args)
    {
        var controller = args.Controller;
        if (controller == null) return;

        int slot = controller.Slot;
        _starterGoldSeeded.Remove(slot);
        _voteSkipSlots.Remove(slot);

        // Stats: emit player_left before removing the controller — PlayerSteamId
        // needs a live controller, and session_duration_s needs the join
        // timestamp we stashed in OnClientFullConnect.
        if (StatsClient.Enabled)
        {
            int remaining = HumanPlayerCount(controller.EntityIndex);
            int sessionDuration = 0;
            if (_playerJoinTimes.TryGetValue(slot, out var joinTs))
                sessionDuration = (int)(DateTime.UtcNow - joinTs).TotalSeconds;
            var hashed = StatsClient.HashSteamId(controller.PlayerSteamId);
            StatsClient.Capture("ti_player_left", hashed, new Dictionary<string, object?>
            {
                ["session_duration_s"] = sessionDuration,
                ["wave"] = _waveNum,
                ["round"] = _roundNum,
                ["was_mid_round"] = _wavesActive,
                ["players_remaining"] = remaining,
            });
            SamplePlayerCount(remaining);
        }
        _playerJoinTimes.Remove(slot);

        var pawn = controller.GetHeroPawn();
        if (pawn != null)
            pawn.Remove();
        controller.Remove();

        // Last human leaving: full reset. DisarmWaves already does spawn-off +
        // trooper cull + _waveNum=0 + timer cancel; we additionally reset the
        // round counter, mode-over flag, and starter-gold ledger so the next
        // player to ever arrive starts a completely fresh session.
        if (HumanPlayerCount(controller.EntityIndex) == 0)
        {
            // Stats: session-outcome emission before the reset. If a victory
            // or defeat already fired, EmitSessionOutcome is a no-op
            // (_sessionStartUtc cleared). Otherwise this is an abandoned session.
            // Emit the per-player round summary too — nobody's on the server
            // to see the chat lines, but PostHog still captures the stats.
            EmitRoundSummary("abandoned", _roundNum, _waveNum);
            EmitSessionOutcome("abandoned");
            DisarmWaves("last player disconnected");
            _roundNum = 1;
            _modeOver = false;
            _starterGoldSeeded.Clear();
            ResetSessionStats();
        }
    }

    public override void OnUnload()
    {
        Console.WriteLine("TrooperInvasion unloaded!");
    }
}
