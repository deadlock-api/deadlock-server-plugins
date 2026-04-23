using DeadworksManaged.Api;
using TrooperInvasion.Stats;

namespace TrooperInvasion;

public class TrooperInvasionConfig
{
}

public class TrooperInvasionPlugin : DeadworksPluginBase
{
    public override string Name => "TrooperInvasion";

    private const int HumanTeam = 2;
    private const int EnemyTeam = 3;

    private const string PatronDesigner = "npc_barrack_boss";

    // Without this pin, the engine sits in PreGameWait and never spawns
    // npc_boss_tier1 / tier2 (Guardians / Walkers). Patron death is intercepted
    // in OnTakeDamage, so a single write at startup is enough.
    private static readonly SchemaAccessor<uint> _eGameState =
        new("CCitadelGameRules"u8, "m_eGameState"u8);

    private const int StarterGold = 2500;
    private const int CatchUpGoldPerWave = 500;
    // Once per slot: respawn keeps your earned souls; disconnect clears the slot.
    private readonly HashSet<int> _starterGoldSeeded = new();

    // Engine caps squad at 8 ("Squad … is too big!!!" spew), so wave volume
    // comes from pulses × squad, not squad size. Cadence scales 1p → 20s, 32p → 5s.
    private const int MaxSquadSize = 8;
    private const float MinBurstSeconds = 1.0f;
    private const float MaxBurstSeconds = 8f;
    private const float FirstWaveGraceSeconds = 10f;
    private const float MinPlayers = 1f;
    private const float MaxPlayers = 32f;
    // Rounds bound the wave counter (and catch-up gold) over long sessions.
    // Player-earned progression persists across rounds; only the horde counter resets.
    private const int RoundLength = 10;
    private const float IntermissionSeconds = 30f;
    // Without a cooldown, _modeOver latches true until last disconnect — survivors
    // would be stuck in a silent server with no waves.
    private const float PostModeCooldownSeconds = 30f;
    private int _roundNum = 1;
    private const float SlowWaveIntervalSeconds = 20f;
    private const float FastWaveIntervalSeconds = 5f;
    private const int MinTrooperCap = 80;
    private const int MaxTrooperCap = 600;
    // HP scaling capped to avoid bullet-sponges past ~R12. R1W1=1.0x, R2W1=1.5x,
    // R3W1=2.0x, R5W10=3.3x, cap=6.0x.
    private const float HealthScalePerRound = 0.5f;
    private const float HealthScalePerWave = 0.03f;
    private const float MaxHealthScale = 6f;
    private readonly HashSet<int> _aliveEnemyTroopers = new();
    private static readonly string[] _trooperDesigners = { "npc_trooper", "npc_trooper_boss" };
    private static bool IsTrooperDesigner(string designer) =>
        designer == "npc_trooper" || designer == "npc_trooper_boss";

    // Must match engine-paid `citadel_trooper_gold_reward` (set per wave in
    // RunWave) or the round leaderboard drifts from deposited gold.
    private static int TrooperGoldReward(int wave) => 70 + wave * 10;

    private IHandle? _pendingWaveTimer;
    private IHandle? _pendingBurstEnd;
    private const int VoteSkipPercent = 30;
    private readonly HashSet<int> _voteSkipSlots = new();
    private bool _wavesActive;

    private int _waveNum;
    private bool _modeOver;

    // Maintained by OnClientFullConnect (+1) and OnClientDisconnect (-1).
    // Avoids Players.GetAll().Count() in the hot OnEntitySpawned path.
    private int _humanCount;

    // Null _sessionStartUtc means no active session — gates session-outcome
    // emission so HandleDefeat → last-disconnect doesn't double-fire.
    private DateTime? _sessionStartUtc;
    private int _peakPlayers;
    private long _playerCountSampleSum;
    private int _playerCountSampleCount;
    private int _deathsThisWave;
    private int _deathsPrevWave;
    private readonly Dictionary<int, DateTime> _playerJoinTimes = new();

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
        // Applied BEFORE trooper subsystem starts streaming — runtime mutation
        // crashes natively (see 2026-04-22-trooper-convar-runtime-mutation.md).
        // max_per_lane 2048 correlated with AVs under the OnEntitySpawned Remove() storm.
        ConVar.Find("citadel_trooper_spawn_enabled")?.SetInt(0);
        ConVar.Find("citadel_allow_purchasing_anywhere")?.SetInt(1);
        ConVar.Find("citadel_player_spawn_time_max_respawn_time")?.SetInt(3);
        ConVar.Find("citadel_allow_duplicate_heroes")?.SetInt(1);
        ConVar.Find("citadel_trooper_max_per_lane")?.SetInt(256);
        ConVar.Find("citadel_trooper_spawn_interval_early")?.SetFloat(1f);
        ConVar.Find("citadel_trooper_spawn_interval_late")?.SetFloat(1f);
        ConVar.Find("citadel_trooper_spawn_interval_very_late")?.SetFloat(1f);
        ConVar.Find("citadel_trooper_gold_reward_bonus_per_minute")?.SetInt(0);
        ConVar.Find("citadel_trooper_spawn_wave_spread")?.SetFloat(2f);
        ConVar.Find("citadel_trooper_spawn_initial")?.SetFloat(0f);

        // Managed state lives on the plugin instance and survives map change.
        _wavesActive = false;
        _modeOver = false;
        _waveNum = 0;
        _roundNum = 1;
        _humanCount = 0;
        _pendingWaveTimer?.Cancel(); _pendingWaveTimer = null;
        _pendingBurstEnd?.Cancel(); _pendingBurstEnd = null;
        _aliveEnemyTroopers.Clear();
        _starterGoldSeeded.Clear();
        _voteSkipSlots.Clear();
        _roundStatsBySlot.Clear();
        ResetSessionStats();

        // One-shot pin. Deferred because GameRules isn't guaranteed networked
        // at OnStartupServer. No drift watcher: gameover_msg/round_end are
        // HookResult.Stop'd and Patron death is pre-empted in OnTakeDamage.
        PinGameInProgress(attemptsLeft: 10);
    }

    private void PinGameInProgress(int attemptsLeft)
    {
        if (attemptsLeft <= 0) return;
        Timer.Once(1000.Milliseconds(), () =>
        {
            if (!GameRules.IsValid)
            {
                PinGameInProgress(attemptsLeft - 1);
                return;
            }
            var ptr = GameRules.Pointer;
            var current = (EGameState)_eGameState.Get(ptr);
            if (current != EGameState.GameInProgress)
                _eGameState.Set(ptr, (uint)EGameState.GameInProgress);
            Console.WriteLine($"[TI] Pinned m_eGameState -> GameInProgress (was {current}).");
        });
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

    private int HumanPlayerCount() => _humanCount;

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

    // Null controller → null so callers skip attribution without NPE.
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

    // No-op when empty so double-fire paths (victory → last-disconnect
    // fallback) don't emit a blank header. Clears the tracker before return.
    private void EmitRoundSummary(string outcome)
    {
        if (_roundStatsBySlot.Count == 0) return;

        int round = _roundNum;
        int waves = _waveNum;
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

    private static float LerpByPlayers(int humans, float atMin, float atMax)
    {
        float clamped = Math.Clamp(humans, MinPlayers, MaxPlayers);
        float t = (clamped - MinPlayers) / (MaxPlayers - MinPlayers);
        return atMin + t * (atMax - atMin);
    }

    private float ComputeWaveInterval(int humans) =>
        LerpByPlayers(humans, SlowWaveIntervalSeconds, FastWaveIntervalSeconds);

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
        _pendingWaveTimer = Timer.Once(((double)FirstWaveGraceSeconds).Seconds(), () =>
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
        _pendingWaveTimer = Timer.Once(((double)interval).Seconds(), () =>
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
        EmitRoundSummary("cleared");
        Console.WriteLine($"[TI] Round {_roundNum} complete at wave {completed}. Intermission {IntermissionSeconds:0}s.");

        // Let the burst window finish before culling + announcing intermission start.
        _pendingWaveTimer?.Cancel();
        _pendingWaveTimer = Timer.Once(((double)postBurstDelaySeconds).Seconds(), () =>
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

            _pendingWaveTimer = Timer.Once(((double)IntermissionSeconds).Seconds(), () =>
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
        _pendingWaveTimer = Timer.Once(((double)PostModeCooldownSeconds).Seconds(), () =>
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
        // Round-boundary only — per-wave announcements stay as chat so the HUD
        // isn't spammed every 5–20 seconds at high player counts.
        var msg = new CCitadelUserMsg_HudGameAnnouncement
        {
            TitleLocstring = title,
            DescriptionLocstring = description,
        };
        NetMessages.Send(msg, RecipientFilter.All);
    }

    // At least 2 players per active lane; Deadlock has only 3 lanes
    // (Yellow=1, Blue=4, Purple=6).
    private int ComputeActiveLanes(int humans) => Math.Clamp(humans / 2, 1, 3);

    private static readonly int[] _laneMarkers = { 1, 4, 6 };
    private static int LaneBitmask(int activeLanes)
    {
        int mask = 0;
        for (int i = 0; i < activeLanes && i < _laneMarkers.Length; i++)
            mask |= _laneMarkers[i];
        return mask;
    }

    private int ComputeTrooperCap(int humans) =>
        (int)LerpByPlayers(humans, MinTrooperCap, MaxTrooperCap);

    private float ComputeHealthScale() =>
        Math.Min(MaxHealthScale, 1f + (_roundNum - 1) * HealthScalePerRound + _waveNum * HealthScalePerWave);

    private void ScaleTrooperHealth(CBaseEntity ent)
    {
        // m_iHealth doesn't auto-clamp to the new max, so we set both.
        int baseMax = ent.MaxHealth;
        if (baseMax <= 0) return;
        int scaled = (int)(baseMax * ComputeHealthScale());
        ent.MaxHealth = scaled;
        ent.Health = scaled;
    }

    private float ComputeBurstSeconds(int waveNum, int humans)
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
        int goldReward = TrooperGoldReward(_waveNum);
        int activeLanes = ComputeActiveLanes(humans);
        float interval = ComputeWaveInterval(humans);
        float burstSeconds = ComputeBurstSeconds(_waveNum, humans);
        float healthScale = ComputeHealthScale();

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

        // Mid-game ConVar writes MUST go through Server.ExecuteCommand — the
        // ConVar.Find().Set* direct path crashed natively on !startwaves.
        Server.ExecuteCommand($"citadel_trooper_squad_size {MaxSquadSize}");
        Server.ExecuteCommand($"citadel_trooper_gold_reward {goldReward}");
        Server.ExecuteCommand($"citadel_active_lane {LaneBitmask(activeLanes)}");
        Server.ExecuteCommand("citadel_trooper_spawn_enabled 1");

        AnnounceHud(
            $"WAVE {_waveNum} / {RoundLength}",
            $"Round {_roundNum} — {activeLanes} lane{(activeLanes == 1 ? "" : "s")}, bounty {goldReward}, next in {interval:0}s");
        Console.WriteLine($"[TI] Round {_roundNum} Wave {_waveNum}: gold={goldReward} humans={humans} burst={burstSeconds:0.0}s nextIn={interval:0.0}s lanes={activeLanes} cap={cap} hp×{healthScale:0.00}");

        _pendingBurstEnd?.Cancel();
        _pendingBurstEnd = Timer.Once(((double)burstSeconds).Seconds(), () =>
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
        // Friendly (team-2) troopers cull via deferred Remove — direct Remove
        // inside OnEntitySpawned AV'd under horde load. See
        // `2026-04-22-onentityspawned-remove-deferral.md`.
        var ent = args.Entity;
        if (!IsTrooperDesigner(ent.DesignerName)) return;
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

        // Patron "deaths" are intercepted in OnTakeDamage before landing, so
        // npc_barrack_boss never reaches entity_killed.
        if (IsTrooperDesigner(killed.DesignerName) && killed.TeamNum == EnemyTeam)
        {
            var trooperKiller = CBaseEntity.FromIndex<CCitadelPlayerPawn>(args.EntindexAttacker);
            if (trooperKiller != null && trooperKiller.TeamNum == HumanTeam)
            {
                var stats = EnsureRoundStats(trooperKiller.Controller);
                if (stats != null)
                {
                    stats.Kills++;
                    stats.Bounty += TrooperGoldReward(_waveNum);
                }
            }
        }

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

    private void EndMode(string outcome, string title, string description)
    {
        _modeOver = true;
        Server.ExecuteCommand("citadel_trooper_spawn_enabled 0");
        AnnounceHud(title, description);
        Console.WriteLine($"[TI] {outcome.ToUpperInvariant()} at wave {_waveNum}");
        EmitRoundSummary(outcome);
        EmitSessionOutcome(outcome);
        BeginPostModeCooldown(outcome);
    }

    private void HandleVictory() =>
        EndMode("victory", "VICTORY!",
            $"Sapphire Patron destroyed — survived {_waveNum} waves. Fresh round in {PostModeCooldownSeconds:0}s.");

    private void HandleDefeat() =>
        EndMode("defeat", "DEFEAT",
            $"Amber Patron has fallen at wave {_waveNum}. Fresh round in {PostModeCooldownSeconds:0}s.");

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
        _humanCount++;

        var available = Enum.GetValues<Heroes>()
            .Select(h => (hero: h, count: usage.GetValueOrDefault(h.GetHeroData()?.HeroID ?? 0), inGame: h.GetHeroData()?.AvailableInGame == true))
            .Where(x => x.inGame)
            .ToArray();

        int min = available.Min(x => x.count);
        var leastPresent = available.Where(x => x.count == min).Select(x => x.hero).ToArray();
        var hero = leastPresent[Random.Shared.Next(leastPresent.Length)];
        controller.SelectHero(hero);

        Console.WriteLine($"[TI] Slot {args.Slot} -> team {HumanTeam}, hero {hero.ToHeroName()}");

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

        // Deferred so the chat UI is settled before the lines land.
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

        // Idempotent — no-op if already active or mode-over.
        ArmWaves();
    }

    private void ApplySpawnRitual(CCitadelPlayerPawn? pawn)
    {
        // Pawn can be mid-init when these events fire (empty model spew in log).
        try { SeedStarterGold(pawn); } catch (Exception ex) { Console.WriteLine($"[TI] SeedStarterGold: {ex.Message}"); }
    }

    private void SeedStarterGold(CCitadelPlayerPawn? pawn)
    {
        // One-time per slot — death should cost you the souls you earned.
        // Catch-up at wave N: StarterGold + (N-1) * CatchUpGoldPerWave.
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
        if (controller.TeamNum == HumanTeam && _humanCount > 0) _humanCount--;
        int remaining = _humanCount;

        // Stats: emit player_left before removing the controller — PlayerSteamId
        // needs a live controller, and session_duration_s needs the join
        // timestamp we stashed in OnClientFullConnect.
        if (StatsClient.Enabled)
        {
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

        Server.ExecuteCommand("sv_cheats 1");
        Server.ExecuteCommand("citadel_kick_disconnected_players");
        Server.ExecuteCommand("sv_cheats 0");

        // Last human leaving: full session reset on top of DisarmWaves.
        if (remaining == 0)
        {
            // EmitSessionOutcome is a no-op if a victory/defeat already fired.
            EmitRoundSummary("abandoned");
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
