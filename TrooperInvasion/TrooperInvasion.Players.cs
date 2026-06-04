using DeadworksManaged.Api;
using TrooperInvasion.Stats;

namespace TrooperInvasion;

public partial class TrooperInvasionPlugin
{
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

        // Auto-pick the least-present available hero.
        int minCount = int.MaxValue;
        var leastUsed = new List<Heroes>();
        foreach (var h in Enum.GetValues<Heroes>())
        {
            var data = h.GetHeroData();
            if (data?.AvailableInGame != true) continue;
            int count = usage.GetValueOrDefault(data.HeroID);
            if (count < minCount) { minCount = count; leastUsed.Clear(); leastUsed.Add(h); }
            else if (count == minCount) leastUsed.Add(h);
        }
        var hero = leastUsed[Random.Shared.Next(leastUsed.Count)];
        controller.SelectHero(hero);

        Console.WriteLine($"[TI] Slot {args.Slot} -> team {HumanTeam}, hero {hero.ToHeroName()}");

        _stats.RecordJoin(controller.Slot);
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
        _stats.SamplePlayerCount(HumanPlayerCount());

        // Deferred so the chat UI is settled before the lines land.
        int welcomeSlot = controller.Slot;
        Timer.Once(1.Ticks(), () =>
        {
            Chat.PrintToChat(welcomeSlot, "[TI] Welcome to Trooper Invasion — all humans defend the Amber Patron vs engine-spawned troopers.");
            Chat.PrintToChat(welcomeSlot,
                _wavesActive && _waveNum > 0
                    ? $"[TI] Currently Round {_roundNum} Wave {_waveNum}/{WaveTuning.RoundLength} — kill troopers for gold, don't let the Patron die."
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

    public override void OnClientDisconnect(ClientDisconnectedEvent args)
    {
        var controller = args.Controller;
        if (controller == null) return;

        int slot = controller.Slot;
        _starterGoldSeeded.Remove(slot);
        _voteSkipSlots.Remove(slot);
        if (controller.TeamNum == HumanTeam && _humanCount > 0) _humanCount--;
        int remaining = _humanCount;

        // Emit player_left before removing the controller — PlayerSteamId needs a
        // live controller, and the duration needs the stashed join timestamp.
        int sessionDuration = _stats.EndSession(slot);
        if (StatsClient.Enabled)
        {
            var hashed = StatsClient.HashSteamId(controller.PlayerSteamId);
            StatsClient.Capture("ti_player_left", hashed, new Dictionary<string, object?>
            {
                ["session_duration_s"] = sessionDuration,
                ["wave"] = _waveNum,
                ["round"] = _roundNum,
                ["was_mid_round"] = _wavesActive,
                ["players_remaining"] = remaining,
            });
            _stats.SamplePlayerCount(remaining);
        }

        // Last human leaving: full session reset on top of DisarmWaves.
        if (remaining == 0)
        {
            // Emissions are no-ops if a victory/defeat already fired.
            _stats.EmitRoundSummary("abandoned", _roundNum, _waveNum);
            _stats.EmitSessionOutcome("abandoned", _waveNum, _roundNum);
            DisarmWaves("last player disconnected");
            _roundNum = 1;
            _modeOver = false;
            _humanPatronDowns = 0;
            _enemyPatronDowns = 0;
            _humanPatronDownAt = null;
            _enemyPatronDownAt = null;
            _starterGoldSeeded.Clear();
            _stats.Reset();
        }
    }
}
