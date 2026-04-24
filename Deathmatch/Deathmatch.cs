using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json.Serialization;
using DeadworksManaged.Api;
using Deathmatch.Stats;

namespace Deathmatch;

public class DeathmatchConfig
{
}

public class DeathmatchPlugin : DeadworksPluginBase
{
    public override string Name => "Deathmatch";

    private static readonly HashSet<string> MapNpcsToRemove = new() {
        "npc_boss_tier1",         // Guardian
		"npc_boss_tier2",         // Walker
		"npc_boss_tier3",         // Base Guardian / Shrine
		"npc_barrack_boss",       // Patron
		"npc_base_defense_sentry",
        "npc_trooper_boss",
    };

    private const float SpawnProtectionSeconds = 3f;
    private readonly Dictionary<int, float> _invulnerableUntil = new();

    private readonly Dictionary<(int team, int lane), List<Vector3>> _walkersByTeamLane = new();
    private readonly List<(Vector3 pos, int team, int? lane)> _rawWalkers = new();
    private bool _laneFromSchema;
    private Vector3 _mapCenter;
    private MaskTrace _worldMask = MaskTrace.Solid;
    private readonly Dictionary<int, Vector3> _lastDeathPos = new();

    private const float RoundSeconds = 180f;
    private static readonly int[] _laneCycle = { 1, 3, 6 };   // Yellow, Green, Purple — skip Blue (4)
    private int _activeLaneIdx;
    private int ActiveLane => _laneCycle[_activeLaneIdx];
    private int NextLane => _laneCycle[(_activeLaneIdx + 1) % _laneCycle.Length];
    private readonly Dictionary<int, int> _killsThisRound = new();
    private float _roundStart = -1f;

    private static readonly SchemaAccessor<float> _gameStartTime = new("CCitadelGameRules"u8, "m_flGameStartTime"u8);
    private static readonly SchemaAccessor<float> _levelStartTime = new("CCitadelGameRules"u8, "m_fLevelStartTime"u8);
    private static readonly SchemaAccessor<float> _roundStartTime = new("CCitadelGameRules"u8, "m_flRoundStartTime"u8);
    private static readonly SchemaAccessor<float> _matchClockAtLastUpdate = new("CCitadelGameRules"u8, "m_flMatchClockAtLastUpdate"u8);
    private static readonly SchemaAccessor<int> _matchClockUpdateTick = new("CCitadelGameRules"u8, "m_nMatchClockUpdateTick"u8);
    private static readonly SchemaAccessor<uint> _eGameState = new("CCitadelGameRules"u8, "m_eGameState"u8);
    private static readonly SchemaAccessor<uint> _eLaneColor = new("CNPC_TrooperBoss"u8, "m_eLaneColor"u8);

    private static readonly HttpClient _rankHttp = new() { Timeout = TimeSpan.FromSeconds(5) };
    // Value.Rank = NaN is a tombstone (404 / unranked). Both real values and tombstones
    // expire after RankCacheTtlMs.
    private static readonly ConcurrentDictionary<uint, (float Rank, long ExpiresAt)> _rankCache = new();
    private static readonly ConcurrentDictionary<uint, byte> _rankFetchInFlight = new();
    // Environment.TickCount64 at which we'll allow another fetch for this account — set on
    // transient failures so a full API outage doesn't trigger a 5s timeout on every join.
    private static readonly ConcurrentDictionary<uint, long> _rankFetchCooldownUntil = new();
    private const int RankFetchCooldownMs = 5 * 60 * 1000;
    private const long RankCacheTtlMs = 24L * 60 * 60 * 1000;
    private const string RankApiBase = "https://api.deadlock-api.com";
    private const float MedianRank = 33f;
    private const float RebalanceThreshold = 4f;
    private const float MinRankDataCoverage = 0.67f;
    private const int MaxSwapsPerWindow = 3;
    private int _rotationsSinceRebalance;

    private sealed record RankResponse([property: JsonPropertyName("raw_score")] float raw_score);

    // Maintained by OnClientFullConnect/Disconnect. Drives session gating so
    // rotation ticks on an empty server don't emit zero-player round events.
    private int _humanCount;

    // Null _sessionStartUtc means no active session — gates session-outcome
    // emission so last-disconnect doesn't double-fire across reconnects.
    private DateTime? _sessionStartUtc;
    private int _peakPlayers;
    private long _playerCountSampleSum;
    private int _playerCountSampleCount;
    private int _dmRoundNum;
    private readonly Dictionary<int, DateTime> _playerJoinTimes = new();

    private sealed class RoundPlayerStats
    {
        public string Name = "Unknown";
        public string? HashedSteamId;
        public int HeroId;
        public int TeamNum;
        public int Kills;
        public int Deaths;
    }
    private readonly Dictionary<int, RoundPlayerStats> _roundStatsBySlot = new();

    [PluginConfig]
    public DeathmatchConfig Config { get; set; } = new();

    public override void OnLoad(bool isReload)
    {
        StatsClient.Configure();
        Console.WriteLine(isReload ? "Deathmatch reloaded!" : "Deathmatch loaded!");
    }

    public override void OnStartupServer()
    {
        ConVar.Find("citadel_npc_spawn_enabled")?.SetInt(0);
        ConVar.Find("citadel_allow_purchasing_anywhere")?.SetInt(1);
        ConVar.Find("citadel_player_spawn_time_max_respawn_time")?.SetInt(2);

        int removed = 0;
        _rawWalkers.Clear();
        foreach (var ent in Entities.All)
        {
            if (ent.DesignerName == WalkerDesigner)
                _rawWalkers.Add((ent.Position, ent.TeamNum, TryReadLaneColor(ent)));
            if (MapNpcsToRemove.Contains(ent.DesignerName)) { ent.Remove(); removed++; }
        }
        RebuildWalkerBuckets();
        int total = _walkersByTeamLane.Values.Sum(v => v.Count);
        var breakdown = string.Join(", ", _walkersByTeamLane.OrderBy(kv => kv.Key.team).ThenBy(kv => kv.Key.lane)
            .Select(kv => $"t{kv.Key.team}/{LaneName(kv.Key.lane)}x{kv.Value.Count}"));
        Console.WriteLine($"[DM] Removed {removed} map NPCs at startup, captured {total} Walker spawn points ({breakdown}) via {(_laneFromSchema ? "schema" : "bearing heuristic")}");
        RecomputeMapCenter();

        Timer.Every(1.Ticks(), ScaleAbilityCooldowns);
        Timer.Every(1.Ticks(), TickMatchClock);

        _humanCount = 0;
        _dmRoundNum = 0;
        _roundStatsBySlot.Clear();
        ResetSessionStats();
    }

    private void ResetSessionStats()
    {
        _sessionStartUtc = null;
        _peakPlayers = 0;
        _playerCountSampleSum = 0;
        _playerCountSampleCount = 0;
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
    // _sessionStartUtc is cleared. Mirrors TrooperInvasion.
    private void EmitSessionOutcome(string outcome)
    {
        if (!StatsClient.Enabled || _sessionStartUtc == null) return;
        int duration = (int)(DateTime.UtcNow - _sessionStartUtc.Value).TotalSeconds;
        double avg = _playerCountSampleCount > 0
            ? (double)_playerCountSampleSum / _playerCountSampleCount
            : 0;
        StatsClient.Capture("dm_session_outcome", null, new Dictionary<string, object?>
        {
            ["outcome"] = outcome,
            ["rounds"] = _dmRoundNum,
            ["duration_s"] = duration,
            ["peak_players"] = _peakPlayers,
            ["avg_players"] = Math.Round(avg, 2),
        });
        _sessionStartUtc = null;
    }

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
        stats.TeamNum = controller.TeamNum;
        return stats;
    }

    // No-op when empty so double-fire paths (rotation → last-disconnect
    // fallback) don't emit a blank header. Clears the tracker before return.
    private void EmitRoundSummary(string outcome, int team2Kills, int team3Kills, int activeLane)
    {
        if (!StatsClient.Enabled || _roundStatsBySlot.Count == 0)
        {
            _roundStatsBySlot.Clear();
            return;
        }

        int round = _dmRoundNum;
        foreach (var (_, s) in _roundStatsBySlot)
        {
            StatsClient.Capture("dm_round_player_summary", s.HashedSteamId, new Dictionary<string, object?>
            {
                ["round"] = round,
                ["outcome"] = outcome,
                ["kills"] = s.Kills,
                ["deaths"] = s.Deaths,
                ["hero_id"] = s.HeroId,
                ["team"] = s.TeamNum,
                ["team2_kills"] = team2Kills,
                ["team3_kills"] = team3Kills,
                ["active_lane"] = activeLane,
            });
        }
        _roundStatsBySlot.Clear();
    }

    private void EmitRoundStarted()
    {
        if (!StatsClient.Enabled) return;
        StatsClient.Capture("dm_round_started", null, new Dictionary<string, object?>
        {
            ["round"] = _dmRoundNum,
            ["players"] = _humanCount,
            ["active_lane"] = ActiveLane,
        });
    }

    private void TickMatchClock()
    {
        if (!GameRules.IsValid) return;
        var ptr = GameRules.Pointer;

        if (_roundStart < 0f)
            _roundStart = GlobalVars.CurTime;

        float elapsed = GlobalVars.CurTime - _roundStart;
        if (elapsed >= RoundSeconds)
        {
            OnRotationTick();
            _roundStart = GlobalVars.CurTime;
            elapsed = 0f;
        }

        // HUD clock: client computes `game_clock ≈ m_flMatchClockAtLastUpdate +
        // (CurTick - m_nMatchClockUpdateTick) * IntervalPerTick`, so both fields have to be
        // written together each tick — otherwise the client extrapolates from a stale anchor
        // and the number keeps climbing even when we pin the float. Writing them both with
        // `tick = now` makes the extrapolation delta zero, so the displayed clock equals
        // `elapsed`. `m_flGameStartTime` is also anchored to `now - elapsed` so server-side
        // consumers (GameRules.GameClock) agree.
        float pausedOffset = GameRules.TotalPausedTicks * GlobalVars.IntervalPerTick;
        float anchor = GlobalVars.CurTime - elapsed - pausedOffset;
        _gameStartTime.Set(ptr, anchor);
        _levelStartTime.Set(ptr, anchor);
        _roundStartTime.Set(ptr, anchor);
        _matchClockAtLastUpdate.Set(ptr, elapsed);
        _matchClockUpdateTick.Set(ptr, GlobalVars.TickCount);

        // Keep the match live forever: if the engine ever transitions past GameInProgress,
        // force it back so the "team X won" screen can't stick.
        if ((EGameState)_eGameState.Get(ptr) != EGameState.GameInProgress)
            _eGameState.Set(ptr, (uint)EGameState.GameInProgress);
    }

    [GameEventHandler("gameover_msg")]
    public HookResult OnGameoverMsg(GameoverMsgEvent args)
    {
        Console.WriteLine($"[DM] Suppressed gameover_msg (winning_team={args.WinningTeam})");
        return HookResult.Stop;
    }

    [GameEventHandler("round_end")]
    public HookResult OnRoundEnd(RoundEndEvent args)
    {
        Console.WriteLine($"[DM] Suppressed round_end");
        return HookResult.Stop;
    }

    private const string WalkerDesigner = "npc_boss_tier2";
    private static readonly HashSet<int> ValidLanes = new() { 1, 3, 4, 6 };

    private static int? TryReadLaneColor(CBaseEntity ent)
    {
        try
        {
            var raw = (int)_eLaneColor.Get(ent.Handle);
            return ValidLanes.Contains(raw) ? raw : (int?)null;
        }
        catch { return null; }
    }

    private void RebuildWalkerBuckets()
    {
        _walkersByTeamLane.Clear();
        if (_rawWalkers.Count == 0) return;

        bool allHaveLane = _rawWalkers.All(w => w.lane.HasValue);
        _laneFromSchema = allHaveLane;
        if (allHaveLane)
        {
            foreach (var (pos, team, lane) in _rawWalkers)
                AddToBucket(team, lane!.Value, pos);
            return;
        }

        // Fallback: sort each team's walkers by bearing around the approximate map center
        // and assign lanes in the canonical cycle [Yellow, Blue, Green, Purple]. Every walker
        // is bucketed — unknown schema lane is not a reason to drop a spawn point.
        var approxCenter = new Vector3(
            _rawWalkers.Average(w => w.pos.X),
            _rawWalkers.Average(w => w.pos.Y),
            _rawWalkers.Average(w => w.pos.Z));
        int[] orderedLanes = { 1, 4, 3, 6 };
        foreach (var byTeam in _rawWalkers.GroupBy(w => w.team))
        {
            var sorted = byTeam
                .OrderBy(w => MathF.Atan2(w.pos.Y - approxCenter.Y, w.pos.X - approxCenter.X))
                .ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                int lane = orderedLanes[i % orderedLanes.Length];
                AddToBucket(sorted[i].team, lane, sorted[i].pos);
            }
        }
    }

    private bool AddToBucket(int team, int lane, Vector3 pos)
    {
        if (pos == Vector3.Zero) return false;
        var key = (team, lane);
        if (!_walkersByTeamLane.TryGetValue(key, out var list))
        {
            list = new List<Vector3>();
            _walkersByTeamLane[key] = list;
        }
        list.Add(pos);
        return true;
    }

    private IEnumerable<Vector3> AllWalkers() => _walkersByTeamLane.Values.SelectMany(v => v);

    private IEnumerable<Vector3> TeamWalkers(int team) =>
        _walkersByTeamLane.Where(kv => kv.Key.team == team).SelectMany(kv => kv.Value);

    private IReadOnlyList<Vector3> TeamLaneWalkers(int team, int lane) =>
        _walkersByTeamLane.TryGetValue((team, lane), out var l) ? l : Array.Empty<Vector3>();

    private void RecomputeMapCenter()
    {
        int count = 0;
        var sum = Vector3.Zero;
        foreach (var p in AllWalkers()) { sum += p; count++; }
        if (count > 0) _mapCenter = sum / count;
    }

    public override void OnEntitySpawned(EntitySpawnedEvent e)
    {
        var ent = e.Entity;
        if (ent.DesignerName == WalkerDesigner && ent.Position != Vector3.Zero)
        {
            _rawWalkers.Add((ent.Position, ent.TeamNum, TryReadLaneColor(ent)));
            RebuildWalkerBuckets();
            RecomputeMapCenter();
        }
        if (MapNpcsToRemove.Contains(ent.DesignerName))
            ent.Remove();
    }

    private static string LaneName(int lane) => lane switch
    {
        1 => "Yellow",
        3 => "Green",
        4 => "Blue",
        6 => "Purple",
        _ => $"?{lane}",
    };

    private static string TeamName(int team) => team switch
    {
        2 => "Amber",
        3 => "Sapphire",
        _ => $"Team{team}",
    };

    public override void OnClientFullConnect(ClientFullConnectEvent args)
    {
        var controller = args.Controller;
        if (controller == null) return;

        EnsureRankQueued((uint)controller.PlayerSteamId);

        var capturedTeams = _walkersByTeamLane.Keys.Select(k => k.team).Distinct().ToArray();
        var playTeams = capturedTeams.Length > 0 ? capturedTeams : new[] { 2, 3 };
        var teamCounts = playTeams.ToDictionary(t => t, _ => 0);
        var teamRanks = playTeams.ToDictionary(t => t, _ => 0f);
        var usage = new Dictionary<int, int>();
        foreach (var p in Players.GetAll())
        {
            if (p.EntityIndex == controller.EntityIndex) continue;
            if (teamCounts.ContainsKey(p.TeamNum))
            {
                teamCounts[p.TeamNum]++;
                teamRanks[p.TeamNum] += ReadRank((uint)p.PlayerSteamId);
            }
            int id = p.PlayerDataGlobal.HeroID;
            if (id > 0) usage[id] = usage.GetValueOrDefault(id) + 1;
        }

        float joinerRank = ReadRank((uint)controller.PlayerSteamId);
        float weakestPostJoinSum = float.PositiveInfinity;
        foreach (var t in playTeams)
        {
            float s = teamRanks[t] + joinerRank;
            if (s < weakestPostJoinSum) weakestPostJoinSum = s;
        }
        var weakestTeams = playTeams.Where(t => teamRanks[t] + joinerRank == weakestPostJoinSum).ToArray();
        int assignedTeam = weakestTeams[Random.Shared.Next(weakestTeams.Length)];
        controller.ChangeTeam(assignedTeam);

        var available = Enum.GetValues<Heroes>()
            .Select(h => (hero: h, count: usage.GetValueOrDefault(h.GetHeroData()?.HeroID ?? 0), inGame: h.GetHeroData()?.AvailableInGame == true))
            .Where(x => x.inGame)
            .ToArray();

        int min = available.Min(x => x.count);
        var leastPresent = available.Where(x => x.count == min).Select(x => x.hero).ToArray();

        var hero = leastPresent[Random.Shared.Next(leastPresent.Length)];
        controller.SelectHero(hero);
        Console.WriteLine($"[DM] Slot {args.Slot} -> team {assignedTeam} " +
            $"(counts: {string.Join(",", teamCounts.OrderBy(kv => kv.Key).Select(kv => $"t{kv.Key}={kv.Value}"))}; " +
            $"ranks: {string.Join(",", teamRanks.OrderBy(kv => kv.Key).Select(kv => $"t{kv.Key}={kv.Value:F0}"))}), " +
            $"hero {hero.ToHeroName()} ({leastPresent.Length} tied at count {min})");

        bool wasEmpty = _humanCount == 0;
        _humanCount++;
        _playerJoinTimes[controller.Slot] = DateTime.UtcNow;
        EnsureSessionStarted();
        // First joiner reopens a fresh round counter. Subsequent joiners attach
        // to the in-progress round — dm_round_started only fires once per
        // rotation cycle (either here on 0→1, or from OnRotationTick).
        if (wasEmpty)
        {
            _dmRoundNum++;
            _roundStatsBySlot.Clear();
            EmitRoundStarted();
        }
        SamplePlayerCount(_humanCount);
        if (StatsClient.Enabled)
        {
            var hashed = StatsClient.HashSteamId(controller.PlayerSteamId);
            float rank = ReadRank((uint)controller.PlayerSteamId);
            StatsClient.Capture("dm_player_joined", hashed, new Dictionary<string, object?>
            {
                ["team"] = assignedTeam,
                ["hero_id"] = hero.GetHeroData()?.HeroID ?? 0,
                ["current_round"] = _dmRoundNum,
                ["players"] = _humanCount,
                ["rank"] = Math.Round(rank, 2),
            });
        }
    }

    public override HookResult OnTakeDamage(TakeDamageEvent args)
    {
        var v = args.Entity;
        bool protect = _invulnerableUntil.TryGetValue(v.EntityIndex, out var until) && GlobalVars.CurTime < until;
        if (protect)
        {
            args.Info.Damage = 0f;
            args.Info.TotalledDamage = 0f;
        }
        return HookResult.Continue;
    }


    private void ApplySpawnRitual(CCitadelPlayerPawn? pawn)
    {
        MaxUpgradeSignatureAbilities(pawn);
        GrantSpawnProtection(pawn);
        pawn?.SetCurrency(ECurrencyType.EGold, 999_999);
    }

    [GameEventHandler("player_hero_changed")]
    public HookResult OnPlayerHeroChanged(PlayerHeroChangedEvent args)
    {
        var pawn = args.Userid?.As<CCitadelPlayerPawn>();
        ApplySpawnRitual(pawn);
        return HookResult.Continue;
    }

    [GameEventHandler("player_death")]
    public HookResult OnPlayerDeath(PlayerDeathEvent args)
    {
        var pawn = args.UseridPawn?.As<CCitadelPlayerPawn>();
        if (pawn != null)
            _lastDeathPos[pawn.EntityIndex] = new Vector3(args.VictimX, args.VictimY, args.VictimZ);

        var attackerBase = args.AttackerController;
        var victimPawn = args.UseridPawn;
        bool scored = attackerBase != null && victimPawn != null
            && attackerBase.EntityIndex != victimPawn.EntityIndex
            && args.AttackerPawn != null
            && args.AttackerPawn.TeamNum != victimPawn.TeamNum;

        var attackerCtrl = scored ? args.AttackerPawn?.As<CCitadelPlayerPawn>()?.Controller : null;
        RoundPlayerStats? attackerStats = null;
        if (scored)
        {
            int idx = attackerBase!.EntityIndex;
            _killsThisRound[idx] = _killsThisRound.GetValueOrDefault(idx) + 1;
            attackerStats = EnsureRoundStats(attackerCtrl);
            if (attackerStats != null) attackerStats.Kills++;
        }

        var victimCtrl = victimPawn?.As<CCitadelPlayerPawn>()?.Controller;
        var victimStats = EnsureRoundStats(victimCtrl);
        if (victimStats != null) victimStats.Deaths++;

        if (StatsClient.Enabled && victimCtrl != null)
        {
            StatsClient.Capture("dm_player_died", victimStats?.HashedSteamId, new Dictionary<string, object?>
            {
                ["round"] = _dmRoundNum,
                ["hero_id"] = victimCtrl.PlayerDataGlobal.HeroID,
                ["team"] = victimCtrl.TeamNum,
                ["active_lane"] = ActiveLane,
                ["attacker_hashed"] = attackerStats?.HashedSteamId,
                ["attacker_hero_id"] = attackerCtrl?.PlayerDataGlobal.HeroID ?? 0,
                ["attacker_team"] = attackerCtrl?.TeamNum ?? 0,
            });
        }
        return HookResult.Continue;
    }

    [GameEventHandler("player_respawned")]
    public HookResult OnPlayerRespawned(PlayerRespawnedEvent args)
    {
        var pawn = args.Userid?.As<CCitadelPlayerPawn>();
        if (pawn != null)
        {
            bool hadPriorDeath = _lastDeathPos.TryGetValue(pawn.EntityIndex, out var dp);
            var death = hadPriorDeath ? dp : pawn.Position;
            _lastDeathPos.Remove(pawn.EntityIndex);
            var engineSpawn = pawn.Position;
            var target = PickSpawnPoint(pawn, death);
            if (target is Vector3 t)
            {
                pawn.Teleport(position: t);
                Console.WriteLine($"[DM] Respawn ent#{pawn.EntityIndex}/t{pawn.TeamNum}: death={death:F0} engine={engineSpawn:F0} -> {t:F0}");
            }
            else
            {
                Console.WriteLine($"[DM] Respawn ent#{pawn.EntityIndex}/t{pawn.TeamNum}: no candidates, engine spawn at {engineSpawn:F0}");
            }
        }
        ApplySpawnRitual(pawn);
        return HookResult.Continue;
    }

    private void OnRotationTick()
    {
        int team2Kills = 0, team3Kills = 0;
        int mvpIdx = -1, mvpKills = 0;
        foreach (var (idx, kills) in _killsThisRound)
        {
            var c = CBaseEntity.FromIndex<CCitadelPlayerController>(idx);
            if (c == null) continue;
            if (c.TeamNum == 2) team2Kills += kills;
            else if (c.TeamNum == 3) team3Kills += kills;
            if (kills > mvpKills) { mvpKills = kills; mvpIdx = idx; }
        }

        string outcome;
        string title;
        if (team2Kills == team3Kills) { outcome = "draw"; title = "Draw!"; }
        else
        {
            int winner = team2Kills > team3Kills ? 2 : 3;
            outcome = winner == 2 ? "amber" : "sapphire";
            title = $"{TeamName(winner)} Team Wins!";
        }

        string desc;
        var nextLaneName = LaneName(NextLane);
        if (mvpIdx >= 0)
        {
            var mvp = CBaseEntity.FromIndex<CCitadelPlayerController>(mvpIdx);
            var mvpName = mvp?.PlayerName ?? "?";
            desc = $"Amber {team2Kills} - {team3Kills} Sapphire  |  Top killer: {mvpName} ({mvpKills})  |  next: {nextLaneName} lane";
        }
        else
        {
            desc = $"No kills  |  next: {nextLaneName} lane";
        }

        NetMessages.Send(new CCitadelUserMsg_HudGameAnnouncement
        {
            TitleLocstring = title,
            DescriptionLocstring = desc,
        }, RecipientFilter.All);
        Console.WriteLine($"[DM] Rotation: {title} — {desc}");

        // Stats: emit outcome + per-player summary for the round that just
        // ended. Gate on _humanCount so empty-server rotation ticks stay silent.
        if (StatsClient.Enabled && _humanCount > 0 && _dmRoundNum > 0)
        {
            StatsClient.Capture("dm_round_outcome", null, new Dictionary<string, object?>
            {
                ["round"] = _dmRoundNum,
                ["outcome"] = outcome,
                ["team2_kills"] = team2Kills,
                ["team3_kills"] = team3Kills,
                ["players"] = _humanCount,
                ["active_lane"] = ActiveLane,
                ["next_lane"] = NextLane,
            });
        }
        EmitRoundSummary(outcome, team2Kills, team3Kills, ActiveLane);

        // Alive players keep fighting in the old lane; they drift into the new lane on
        // their next respawn because PickSpawnPoint targets ActiveLane.
        _activeLaneIdx = (_activeLaneIdx + 1) % _laneCycle.Length;
        _killsThisRound.Clear();

        // New round — bump counter before EmitRoundStarted so the event carries
        // the upcoming round number (not the one we just summarised).
        if (_humanCount > 0)
        {
            _dmRoundNum++;
            SamplePlayerCount(_humanCount);
            EmitRoundStarted();
        }

        _rotationsSinceRebalance++;
        if (_rotationsSinceRebalance >= _laneCycle.Length)
        {
            _rotationsSinceRebalance = 0;
            TryRebalanceTeams();
        }
    }

    private void GrantSpawnProtection(CCitadelPlayerPawn? pawn)
    {
        if (pawn == null) return;
        int idx = pawn.EntityIndex;
        _invulnerableUntil[idx] = GlobalVars.CurTime + SpawnProtectionSeconds;
        var mp = pawn.ModifierProp;
        if (mp != null)
        {
            mp.SetModifierState(EModifierState.Invulnerable, true);
            mp.SetModifierState(EModifierState.BulletInvulnerable, true);
        }
        Timer.Once(((int)(SpawnProtectionSeconds * 1000)).Milliseconds(), () =>
        {
            if (!_invulnerableUntil.TryGetValue(idx, out var until) || GlobalVars.CurTime + 0.05f < until)
                return;
            _invulnerableUntil.Remove(idx);
            var mp2 = CBaseEntity.FromIndex<CCitadelPlayerPawn>(idx)?.ModifierProp;
            if (mp2 != null)
            {
                mp2.SetModifierState(EModifierState.Invulnerable, false);
                mp2.SetModifierState(EModifierState.BulletInvulnerable, false);
            }
        });
    }

    private Vector3? PickSpawnPoint(CCitadelPlayerPawn respawning, Vector3 deathPos)
    {
        int ownTeam = respawning.TeamNum;
        IReadOnlyList<Vector3> candidates = TeamLaneWalkers(ownTeam, ActiveLane);
        if (candidates.Count == 0)
        {
            var teamAny = TeamWalkers(ownTeam).ToArray();
            candidates = teamAny.Length > 0 ? teamAny : AllWalkers().ToArray();
        }
        if (candidates.Count == 0)
        {
            Console.WriteLine($"[DM] PickSpawnPoint: no walkers captured (raw={_rawWalkers.Count} buckets={_walkersByTeamLane.Count}) — engine spawn will be used");
            return null;
        }

        var enemies = new List<CCitadelPlayerPawn>();
        foreach (var p in Players.GetAllPawns())
        {
            if (p == null || p.EntityIndex == respawning.EntityIndex || !p.IsAlive || p.TeamNum == ownTeam) continue;
            var cp = p.As<CCitadelPlayerPawn>();
            if (cp != null) enemies.Add(cp);
        }

        const float LineOfFireDeg = 20f;
        const float LineOfFireRange = 2500f;
        float cosThreshold = MathF.Cos(LineOfFireDeg * MathF.PI / 180f);

        var ranked = new List<(Vector3 pos, float score, bool inFire)>(candidates.Count);
        foreach (var c in candidates)
        {
            bool inFire = false;
            foreach (var other in enemies)
            {
                var eye = other.Position + new Vector3(0, 0, 64);
                var toCand = c - eye;
                float dist = toCand.Length();
                if (dist > LineOfFireRange || dist < 1f) continue;
                var aim = AimForward(other.EyeAngles);
                float cosAng = Vector3.Dot(toCand / dist, aim);
                if (cosAng < cosThreshold) continue;
                var vis = Trace.Ray(eye, c, _worldMask, other);
                if (vis.Fraction > 0.98f) { inFire = true; break; }
            }
            ranked.Add((c, ScoreCandidate(c, deathPos, enemies), inFire));
        }

        var pool = ranked.Where(r => !r.inFire).ToList();
        if (pool.Count == 0) pool = ranked;
        pool.Sort((a, b) => b.score.CompareTo(a.score));
        int topN = Math.Min(3, pool.Count);
        return pool[Random.Shared.Next(topN)].pos;
    }

    private float ScoreCandidate(Vector3 c, Vector3 deathPos, IReadOnlyList<CCitadelPlayerPawn> enemies)
    {
        const float DistScale = 5000f;
        float deathTerm = MathF.Min(1f, Vector3.Distance(c, deathPos) / DistScale);

        // Trapezoidal proximity score around the nearest enemy:
        // 0 at the enemy, ramping up to 1 at IdealMin (too close is bad),
        // flat 1 in [IdealMin, IdealMax] (fight-ready distance),
        // gently decaying past IdealMax so farther candidates are still picked if nothing better
        // exists, but a closer camp wins when all else is equal. No hard filter.
        const float IdealMin = 1500f;
        const float IdealMax = 3500f;
        const float FarFalloff = 6000f;
        float playerTerm = 1f;
        float offAxisTerm = 1f;
        if (enemies.Count > 0)
        {
            float minDist = float.MaxValue;
            float minAng = float.MaxValue;
            foreach (var p in enemies)
            {
                float d = Vector3.Distance(c, p.Position);
                if (d < minDist) minDist = d;
                float a = MinAngularOffset(p, c);
                if (a < minAng) minAng = a;
            }
            if (minDist < IdealMin) playerTerm = minDist / IdealMin;
            else if (minDist < IdealMax) playerTerm = 1f;
            else playerTerm = MathF.Max(0f, 1f - (minDist - IdealMax) / FarFalloff);
            offAxisTerm = minAng / MathF.PI;
        }

        float centerTerm = 1f - MathF.Min(1f, Vector3.Distance(c, _mapCenter) / DistScale);

        return 1.0f * deathTerm
             + 1.5f * playerTerm
             + 0.75f * centerTerm
             + 0.5f * offAxisTerm;
    }

    private static Vector3 AimForward(Vector3 eyeAngles)
    {
        float pitch = eyeAngles.X * MathF.PI / 180f;
        float yaw = eyeAngles.Y * MathF.PI / 180f;
        return new Vector3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
    }

    private static float MinAngularOffset(CCitadelPlayerPawn p, Vector3 target)
    {
        var eye = p.Position + new Vector3(0, 0, 64);
        var to = target - eye;
        float len = to.Length();
        if (len < 1f) return 0f;
        float cos = Vector3.Dot(to / len, AimForward(p.EyeAngles));
        return MathF.Acos(Math.Clamp(cos, -1f, 1f));
    }

    private static void MaxUpgradeSignatureAbilities(CCitadelPlayerPawn? pawn)
    {
        if (pawn == null) return;
        foreach (var ability in pawn.AbilityComponent.Abilities)
        {
            if (ability.AbilitySlot < EAbilitySlot.Signature1 || ability.AbilitySlot > EAbilitySlot.Signature4) continue;
            ability.UpgradeBits = ability.UpgradeBits | 0b11111;
        }
    }


    private const float CooldownScale = 0.5f;
    // Shifting the whole [start, end] window backward (rather than only lowering end) survives
    // game-side recomputations like end = start + vdataDuration; we re-shift whenever the current
    // values don't match what we last wrote.
    private readonly Dictionary<nint, (float Start, float End)> _writtenCooldowns = new();
    private readonly HashSet<nint> _cooldownSweep = new();
    private readonly List<nint> _cooldownStale = new();

    private void ScaleAbilityCooldowns()
    {
        _cooldownSweep.Clear();
        foreach (var controller in Players.GetAll())
        {
            var pawn = controller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
            if (pawn == null) continue;
            foreach (var ability in pawn.AbilityComponent.Abilities)
            {
                float start = ability.CooldownStart;
                float end = ability.CooldownEnd;
                float duration = end - start;
                if (duration <= 0.001f) continue;

                _cooldownSweep.Add(ability.Handle);
                var prev = _writtenCooldowns.GetValueOrDefault(ability.Handle);
                bool alreadyOurs = MathF.Abs(prev.Start - start) < 0.01f && MathF.Abs(prev.End - end) < 0.01f;
                if (alreadyOurs) continue;

                float shift = (1f - CooldownScale) * duration;
                float newStart = start - shift;
                float newEnd = end - shift;
                ability.CooldownStart = newStart;
                ability.CooldownEnd = newEnd;
                _writtenCooldowns[ability.Handle] = (newStart, newEnd);
            }
        }

        _cooldownStale.Clear();
        foreach (var key in _writtenCooldowns.Keys)
            if (!_cooldownSweep.Contains(key)) _cooldownStale.Add(key);
        foreach (var key in _cooldownStale)
            _writtenCooldowns.Remove(key);
    }

    private static readonly string[] _helpLines = {
        "[DM] !help — show this message",
        "[DM] !hero <name> — queue hero swap for next respawn (fuzzy match, e.g. !hero grey -> Grey Talon)",
        "[DM] !stuck / !suicide — kill yourself to respawn",
        "[DM] !feedback <message> — send feedback to the server admins",
    };

    [Command("help", Description = "Show available Deathmatch commands")]
    public void CmdHelp(CCitadelPlayerController caller)
    {
        foreach (var line in _helpLines)
            Chat.PrintToChat(caller.Slot, line);
    }

    [Command("stuck", Description = "Kill yourself to respawn")]
    [Command("suicide", Description = "Kill yourself to respawn")]
    public void CmdStuck(CCitadelPlayerController caller)
    {
        var pawn = caller.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn == null || !pawn.IsAlive)
            throw new CommandException("[DM] Not alive.");

        // Spawn protection zeroes damage and sets Invulnerable modifier bits — clear both so
        // the suicide damage actually kills the player instead of being absorbed.
        _invulnerableUntil.Remove(pawn.EntityIndex);
        var mp = pawn.ModifierProp;
        if (mp != null)
        {
            mp.SetModifierState(EModifierState.Invulnerable, false);
            mp.SetModifierState(EModifierState.BulletInvulnerable, false);
        }
        pawn.Hurt(999_999f);
    }

    private static void EnsureRankQueued(uint accountId)
    {
        if (accountId == 0) return;
        long now = Environment.TickCount64;
        if (_rankCache.TryGetValue(accountId, out var cached) && now < cached.ExpiresAt) return;
        if (_rankFetchCooldownUntil.TryGetValue(accountId, out var until) && now < until) return;
        if (!_rankFetchInFlight.TryAdd(accountId, 0)) return;
        _ = FetchRankAsync(accountId);
    }

    private static bool TryReadFreshRank(uint accountId, out float rank)
    {
        rank = 0f;
        if (!_rankCache.TryGetValue(accountId, out var cached)) return false;
        if (Environment.TickCount64 >= cached.ExpiresAt) return false;
        if (float.IsNaN(cached.Rank)) return false;
        rank = cached.Rank;
        return true;
    }

    private static float ReadRank(uint accountId) =>
        TryReadFreshRank(accountId, out var r) ? r : MedianRank;

    private static async Task FetchRankAsync(uint accountId)
    {
        bool succeeded = false;
        long expiresAt = Environment.TickCount64 + RankCacheTtlMs;
        try
        {
            var resp = await _rankHttp.GetAsync($"{RankApiBase}/v1/players/{accountId}/rank-predict");
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                _rankCache[accountId] = (float.NaN, expiresAt);
                succeeded = true;
                return;
            }
            if (!resp.IsSuccessStatusCode) return;
            var body = await resp.Content.ReadFromJsonAsync<RankResponse>();
            if (body != null && !float.IsNaN(body.raw_score))
            {
                _rankCache[accountId] = (Math.Clamp(body.raw_score, 1f, 66f), expiresAt);
                succeeded = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DM] rank fetch {accountId} failed: {ex.Message}");
        }
        finally
        {
            if (!succeeded)
                _rankFetchCooldownUntil[accountId] = Environment.TickCount64 + RankFetchCooldownMs;
            _rankFetchInFlight.TryRemove(accountId, out _);
        }
    }

    private readonly record struct RebalanceEntry(int Idx, uint AccountId, float Rank, bool KnownRank);

    private void TryRebalanceTeams()
    {
        var teamA = new List<RebalanceEntry>();
        var teamB = new List<RebalanceEntry>();
        foreach (var ctrl in Players.GetAll())
        {
            List<RebalanceEntry>? bucket = ctrl.TeamNum switch { 2 => teamA, 3 => teamB, _ => null };
            if (bucket == null) continue;
            uint aid = (uint)ctrl.PlayerSteamId;
            EnsureRankQueued(aid);
            bool known = TryReadFreshRank(aid, out var r);
            bucket.Add(new RebalanceEntry(ctrl.EntityIndex, aid, known ? r : MedianRank, known));
        }

        int total = teamA.Count + teamB.Count;
        if (total < 2 || teamA.Count == 0 || teamB.Count == 0) return;

        int knownCount = teamA.Count(x => x.KnownRank) + teamB.Count(x => x.KnownRank);
        if ((float)knownCount / total < MinRankDataCoverage)
        {
            Console.WriteLine($"[DM] rebalance skipped: only {knownCount}/{total} ranks known");
            return;
        }

        float sumA = teamA.Sum(x => x.Rank);
        float sumB = teamB.Sum(x => x.Rank);
        float avgDiff = MathF.Abs(sumA / teamA.Count - sumB / teamB.Count);
        if (avgDiff < RebalanceThreshold)
        {
            Console.WriteLine($"[DM] teams balanced (avgDiff={avgDiff:F2}, sumA={sumA:F1} sumB={sumB:F1})");
            return;
        }

        var swappedIdxs = new HashSet<int>();
        int swapCount = 0;
        for (int iter = 0; iter < MaxSwapsPerWindow; iter++)
        {
            float currentDiff = MathF.Abs(sumA - sumB);
            float bestDiff = currentDiff;
            int bestA = -1, bestB = -1;
            for (int i = 0; i < teamA.Count; i++)
            {
                if (swappedIdxs.Contains(teamA[i].Idx)) continue;
                for (int j = 0; j < teamB.Count; j++)
                {
                    if (swappedIdxs.Contains(teamB[j].Idx)) continue;
                    float newA = sumA - teamA[i].Rank + teamB[j].Rank;
                    float newB = sumB - teamB[j].Rank + teamA[i].Rank;
                    float d = MathF.Abs(newA - newB);
                    if (d < bestDiff - 0.001f)
                    {
                        bestDiff = d;
                        bestA = i;
                        bestB = j;
                    }
                }
            }
            if (bestA < 0) break;

            var a = teamA[bestA];
            var b = teamB[bestB];
            string nameA = GetControllerName(a.Idx);
            string nameB = GetControllerName(b.Idx);
            bool okA = ApplyTeamSwap(a.Idx, 3);
            bool okB = ApplyTeamSwap(b.Idx, 2);
            if (!okA || !okB) break;

            swappedIdxs.Add(a.Idx);
            swappedIdxs.Add(b.Idx);
            teamA.RemoveAt(bestA);
            teamB.RemoveAt(bestB);
            teamA.Add(b);
            teamB.Add(a);
            sumA = sumA - a.Rank + b.Rank;
            sumB = sumB - b.Rank + a.Rank;
            swapCount++;

            Chat.PrintToChatAll($"[DM] Rebalance: {nameA} → {TeamName(3)}");
            Chat.PrintToChatAll($"[DM] Rebalance: {nameB} → {TeamName(2)}");
        }

        if (swapCount == 0)
        {
            Console.WriteLine($"[DM] rebalance: no improving swap found (avgDiff={avgDiff:F2})");
            return;
        }

        float finalAvgDiff = MathF.Abs(sumA / teamA.Count - sumB / teamB.Count);
        NetMessages.Send(new CCitadelUserMsg_HudGameAnnouncement
        {
            TitleLocstring = "Team Rebalance",
            DescriptionLocstring = $"Swapped {swapCount} player(s)  |  Amber {sumA:F0} ↔ Sapphire {sumB:F0}",
        }, RecipientFilter.All);
        Console.WriteLine($"[DM] rebalance: {swapCount} swap(s), sumA={sumA:F1} sumB={sumB:F1} avgDiff={avgDiff:F2}->{finalAvgDiff:F2}");

        if (StatsClient.Enabled)
        {
            StatsClient.Capture("dm_team_rebalance", null, new Dictionary<string, object?>
            {
                ["round"] = _dmRoundNum,
                ["swaps"] = swapCount,
                ["avg_diff_before"] = Math.Round(avgDiff, 2),
                ["avg_diff_after"] = Math.Round(finalAvgDiff, 2),
                ["sum_amber"] = Math.Round(sumA, 1),
                ["sum_sapphire"] = Math.Round(sumB, 1),
                ["players"] = _humanCount,
            });
        }
    }

    private static string GetControllerName(int idx) =>
        CBaseEntity.FromIndex<CCitadelPlayerController>(idx)?.PlayerName ?? "?";

    private bool ApplyTeamSwap(int idx, int newTeam)
    {
        var ctrl = CBaseEntity.FromIndex<CCitadelPlayerController>(idx);
        if (ctrl == null) return false;
        ctrl.ChangeTeam(newTeam);
        var pawn = ctrl.GetHeroPawn()?.As<CCitadelPlayerPawn>();
        if (pawn != null && pawn.IsAlive)
        {
            // Mirror !suicide: clear spawn protection so the killing damage actually lands.
            _invulnerableUntil.Remove(pawn.EntityIndex);
            var mp = pawn.ModifierProp;
            if (mp != null)
            {
                mp.SetModifierState(EModifierState.Invulnerable, false);
                mp.SetModifierState(EModifierState.BulletInvulnerable, false);
            }
            pawn.Hurt(999_999f);
        }
        return true;
    }

    public override void OnClientDisconnect(ClientDisconnectedEvent args)
    {
        var controller = args.Controller;
        if (controller == null) return;

        int slot = controller.Slot;
        int killsThisRound = _killsThisRound.GetValueOrDefault(controller.EntityIndex);
        _killsThisRound.Remove(controller.EntityIndex);
        _roundStatsBySlot.Remove(slot);
        var pawn = controller.GetHeroPawn();
        if (pawn != null)
        {
            _lastDeathPos.Remove(pawn.EntityIndex);
            _invulnerableUntil.Remove(pawn.EntityIndex);
        }

        // 2/3 are the playable teams; spectators/unassigned don't count toward
        // session state (matches the team filter in OnClientFullConnect).
        bool countedPlayer = controller.TeamNum == 2 || controller.TeamNum == 3;
        if (countedPlayer && _humanCount > 0) _humanCount--;
        int remaining = _humanCount;

        if (StatsClient.Enabled && countedPlayer)
        {
            int sessionDuration = 0;
            if (_playerJoinTimes.TryGetValue(slot, out var joinTs))
                sessionDuration = (int)(DateTime.UtcNow - joinTs).TotalSeconds;
            var hashed = StatsClient.HashSteamId(controller.PlayerSteamId);
            StatsClient.Capture("dm_player_left", hashed, new Dictionary<string, object?>
            {
                ["session_duration_s"] = sessionDuration,
                ["round"] = _dmRoundNum,
                ["kills_this_round"] = killsThisRound,
                ["team"] = controller.TeamNum,
                ["players_remaining"] = remaining,
            });
            SamplePlayerCount(remaining);
        }
        _playerJoinTimes.Remove(slot);

        if (remaining == 0)
        {
            EmitSessionOutcome("abandoned");
            ResetSessionStats();
            _roundStatsBySlot.Clear();
            // Next session starts at round 1 — keep the counter tied to the
            // session lifecycle so the dm_session_outcome.rounds field stays
            // meaningful.
            _dmRoundNum = 0;
        }
    }

    public override void OnUnload()
    {
        Console.WriteLine("Deathmatch unloaded!");
    }
}
