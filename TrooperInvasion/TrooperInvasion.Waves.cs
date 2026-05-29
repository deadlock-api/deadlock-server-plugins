using DeadworksManaged.Api;
using TrooperInvasion.Stats;

namespace TrooperInvasion;

public partial class TrooperInvasionPlugin
{
    private void SetWaveTimer(double seconds, Action body)
    {
        _pendingWaveTimer?.Cancel();
        _pendingWaveTimer = Timer.Once(seconds.Seconds(), () =>
        {
            _pendingWaveTimer = null;
            body();
        });
    }

    // ConVar.Find().SetInt crashes mid-game (see RunWave); ExecuteCommand is the
    // only runtime-safe path, gated by _spawnEnabled to skip redundant dispatches.
    private void SetSpawnEnabled(bool on)
    {
        if (_spawnEnabled == on) return;
        _spawnEnabled = on;
        Server.ExecuteCommand($"citadel_trooper_spawn_enabled {(on ? 1 : 0)}");
    }

    private void ArmWaves()
    {
        if (_modeOver || _wavesActive) return;
        _wavesActive = true;
        Console.WriteLine($"[TI] Wave scheduler armed (round {_roundNum}).");
        AnnounceHud($"ROUND {_roundNum}", $"First wave in {FirstWaveGraceSeconds:0}s — defend the Patron!");
        _stats.EnsureStarted();
        if (StatsClient.Enabled)
        {
            StatsClient.Capture("ti_round_started", null, new Dictionary<string, object?>
            {
                ["round"] = _roundNum,
                ["players"] = HumanPlayerCount(),
            });
        }
        SetWaveTimer(FirstWaveGraceSeconds, () => { if (_wavesActive) RunWave(); });
    }

    private void DisarmWaves(string reason, bool cullTroopers = true)
    {
        _wavesActive = false;
        _pendingWaveTimer?.Cancel(); _pendingWaveTimer = null;
        _pendingBurstEnd?.Cancel(); _pendingBurstEnd = null;
        SetSpawnEnabled(false);
        if (cullTroopers) CullAllTroopers();
        // Reset so the next session starts from a fresh wave-1 onboarding ramp.
        _waveNum = 0;
        _voteSkipSlots.Clear();
        _stats.ClearRoundStats();
        Console.WriteLine($"[TI] Wave scheduler paused ({reason}). {(cullTroopers ? "Troopers culled, " : "Troopers kept alive, ")}wave counter reset.");
    }

    private void ScheduleNextWave()
    {
        if (_modeOver || !_wavesActive) return;
        float interval = WaveTuning.ComputeWaveInterval(HumanPlayerCount());
        SetWaveTimer(interval, () => { if (_wavesActive) RunWave(); });
    }

    private void BeginIntermission(float postBurstDelaySeconds)
    {
        int completed = _waveNum;
        // Must fire while _wavesActive is still true, else RunWave's continuation
        // guard would cancel us.
        AnnounceHud($"ROUND {_roundNum} CLEARED", $"{completed} waves survived — fresh round in {IntermissionSeconds:0}s");
        _stats.EmitRoundSummary("cleared", _roundNum, _waveNum);
        Console.WriteLine($"[TI] Round {_roundNum} complete at wave {completed}. Intermission {IntermissionSeconds:0}s.");

        if (postBurstDelaySeconds <= 0f)
            FinishRound();
        else
            SetWaveTimer(postBurstDelaySeconds, FinishRound);
    }

    // Pulled out of BeginIntermission so manual round-end paths (voteskip) can
    // call it directly without a racy 0-delay timer hop.
    private void FinishRound()
    {
        // Live troopers are deliberately kept so players can fight leftovers
        // during the breather.
        DisarmWaves($"round {_roundNum} complete", cullTroopers: false);
        _roundNum++;
        _starterGoldSeeded.Clear();

        SetWaveTimer(IntermissionSeconds, () =>
        {
            if (HumanPlayerCount() > 0) ArmWaves();
            else Console.WriteLine("[TI] Intermission ended with empty server — staying dormant until a player joins.");
        });
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

        int cap = WaveTuning.ComputeTrooperCap(humans);
        ReconcileAliveTroopers();
        int alive = _aliveEnemyTroopers.Count;
        if (alive >= cap)
        {
            Chat.PrintToChatAll($"[TI] Wave skipped — {alive} troopers still alive (cap {cap})");
            Console.WriteLine($"[TI] Wave skipped: alive={alive} cap={cap} humans={humans}");
            // Still reschedule so kills eventually unblock the round.
            ScheduleNextWave();
            return;
        }

        _waveNum++;
        int goldReward = WaveTuning.TrooperGoldReward(_waveNum);
        int activeLanes = WaveTuning.ComputeActiveLanes(humans);
        float interval = WaveTuning.ComputeWaveInterval(humans);
        float burstSeconds = WaveTuning.ComputeBurstSeconds(_waveNum, humans);
        float healthScale = WaveTuning.ComputeHealthScale(_roundNum, _waveNum);

        _stats.SamplePlayerCount(humans);
        if (StatsClient.Enabled)
        {
            StatsClient.Capture("ti_wave_started", null, new Dictionary<string, object?>
            {
                ["wave"] = _waveNum,
                ["round"] = _roundNum,
                ["players"] = humans,
                ["deaths_prev_wave"] = _stats.DeathsPrevWave,
                ["alive_enemy_troopers"] = alive,
                ["active_lanes"] = activeLanes,
                ["gold_reward"] = goldReward,
                ["health_scale"] = Math.Round(healthScale, 2),
            });
        }
        _stats.RotateWaveDeaths();

        // Mid-game ConVar writes MUST go through Server.ExecuteCommand — the
        // ConVar.Find().Set* direct path crashed natively on !startwaves.
        Server.ExecuteCommand($"citadel_trooper_squad_size {WaveTuning.MaxSquadSize}");
        Server.ExecuteCommand($"citadel_trooper_gold_reward {goldReward}");
        Server.ExecuteCommand($"citadel_active_lane {WaveTuning.LaneBitmask(activeLanes)}");
        SetSpawnEnabled(true);

        AnnounceHud(
            $"WAVE {_waveNum} / {WaveTuning.RoundLength}",
            $"Round {_roundNum} — {activeLanes} lane{(activeLanes == 1 ? "" : "s")}, bounty {goldReward}, next in {interval:0}s");
        Console.WriteLine($"[TI] Round {_roundNum} Wave {_waveNum}: gold={goldReward} humans={humans} burst={burstSeconds:0.0}s nextIn={interval:0.0}s lanes={activeLanes} cap={cap} hp×{healthScale:0.00}");

        _pendingBurstEnd?.Cancel();
        _pendingBurstEnd = Timer.Once(((double)burstSeconds).Seconds(), () =>
        {
            _pendingBurstEnd = null;
            SetSpawnEnabled(false);
        });

        if (_waveNum >= WaveTuning.RoundLength)
            BeginIntermission(burstSeconds + 2f);
        else
            ScheduleNextWave();
    }

    // Round-boundary HUD toasts only — per-wave events stay in chat so the HUD
    // isn't spammed every few seconds at high player counts.
    private static void AnnounceHud(string title, string description)
    {
        var msg = new CCitadelUserMsg_HudGameAnnouncement
        {
            TitleLocstring = title,
            DescriptionLocstring = description,
        };
        NetMessages.Send(msg, RecipientFilter.All);
    }
}
