using DeadworksManaged.Api;
using TrooperInvasion.Stats;

namespace TrooperInvasion;

public partial class TrooperInvasionPlugin
{
    [GameEventHandler("gameover_msg")]
    public HookResult OnGameoverMsg(GameoverMsgEvent args) => HookResult.Stop;

    [GameEventHandler("round_end")]
    public HookResult OnRoundEnd(RoundEndEvent args) => HookResult.Stop;

    // Intercepts the killing blow on either Patron. Letting it reach 0 HP flips
    // m_eGameState → PostGame, which kicks every client and blocks joins until a
    // map reload. We zero the lethal damage, pin HP to 1, and call EndMode
    // ourselves. Non-lethal damage passes through so the HUD still ticks down.
    public override HookResult OnTakeDamage(TakeDamageEvent args)
    {
        if (args.Entity.DesignerName != PatronDesigner) return HookResult.Continue;

        // Patron is invulnerable through the cooldown so stray hits can't
        // re-trigger the defeat handler.
        if (_modeOver)
        {
            args.Info.Damage = 0f;
            return HookResult.Continue;
        }

        // A Walker / Watcher death fires an engine-scripted "weaken Patron"
        // hit whose magnitude exceeds Patron HP, with the attacker propagated from
        // whoever killed the sub-objective — so attacker-identity can't distinguish it
        // from a real killing blow. The only reliable signal is the time window
        // from the guardian's entity_killed event, gated on damage magnitude so
        // legitimate small hits during the window still pass through.
        DateTime? weakenAt = args.Entity.TeamNum == HumanTeam
            ? _humanPatronWeakenAt
            : _enemyPatronWeakenAt;
        if (weakenAt.HasValue
            && (DateTime.UtcNow - weakenAt.Value).TotalSeconds < GuardianWeakenWindowSeconds
            && args.Info.Damage > args.Entity.MaxHealth * 0.5f)
        {
            args.Info.Damage = 0f;
            args.Entity.Health = args.Entity.MaxHealth;
            Console.WriteLine($"[TI] Absorbed scripted weaken-Patron event (team {args.Entity.TeamNum}) — Patron pinned at full HP.");
            return HookResult.Continue;
        }

        if (args.Entity.Health - args.Info.Damage > 0f) return HookResult.Continue;

        // Always swallow the lethal damage so the Patron never reaches 0 HP.
        args.Info.Damage = 0f;
        args.Entity.Health = 1;

        // Only declare victory/defeat when a real player or trooper landed the
        // blow — the scripted weaken hit above must not count.
        var attacker = args.Info.Attacker;
        bool realKill = attacker != null &&
            (attacker.As<CCitadelPlayerPawn>() != null || IsTrooperDesigner(attacker.DesignerName));
        if (!realKill) return HookResult.Continue;

        EndMode(victory: args.Entity.TeamNum != HumanTeam);
        return HookResult.Continue;
    }

    [GameEventHandler("entity_killed")]
    public HookResult OnEntityKilled(EntityKilledEvent args)
    {
        if (_modeOver) return HookResult.Continue;
        var killed = CBaseEntity.FromIndex(args.EntindexKilled);
        if (killed == null) return HookResult.Continue;

        // Open the weaken-Patron absorb window — see OnTakeDamage. entity_killed
        // for the sub-objective (Walker/Watcher) fires before the scripted hit on
        // the Patron, so the timestamp is reliably set when the gate runs.
        if (IsGuardianDesigner(killed.DesignerName))
        {
            if (killed.TeamNum == HumanTeam) _humanPatronWeakenAt = DateTime.UtcNow;
            else if (killed.TeamNum == EnemyTeam) _enemyPatronWeakenAt = DateTime.UtcNow;
        }

        // The Patron (npc_boss_tier3) death is intercepted in OnTakeDamage, so it
        // never reaches here. Watchers (npc_barrack_boss) do die normally now — they
        // are not IsTrooperDesigner, so they skip the trooper-bounty branch below.
        if (IsTrooperDesigner(killed.DesignerName) && killed.TeamNum == EnemyTeam)
        {
            var trooperKiller = CBaseEntity.FromIndex<CCitadelPlayerPawn>(args.EntindexAttacker);
            if (trooperKiller != null && trooperKiller.TeamNum == HumanTeam)
            {
                var stats = _stats.EnsureRoundStats(trooperKiller.Controller);
                if (stats != null)
                {
                    stats.Kills++;
                    stats.Bounty += WaveTuning.TrooperGoldReward(_waveNum);
                }
            }
        }

        if (killed.TeamNum == HumanTeam)
        {
            var pawn = killed.As<CCitadelPlayerPawn>();
            var controller = pawn?.Controller;
            if (controller != null)
            {
                _stats.RecordDeath();
                var stats = _stats.EnsureRoundStats(controller);
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
            if (pawn != null && _wavesActive)
                pawn.RespawnTime = GlobalVars.CurTime + WaveTuning.ComputeRespawnDelay(_roundNum, _waveNum);
        }

        return HookResult.Continue;
    }

    private void EndMode(bool victory)
    {
        _modeOver = true;
        SetSpawnEnabled(false);
        string outcome = victory ? "victory" : "defeat";
        string title = victory ? "VICTORY!" : "DEFEAT";
        string description = victory
            ? $"Sapphire Patron destroyed — survived {_waveNum} waves. Fresh round in {PostModeCooldownSeconds:0}s."
            : $"Amber Patron has fallen at wave {_waveNum}. Fresh round in {PostModeCooldownSeconds:0}s.";
        AnnounceHud(title, description);
        Console.WriteLine($"[TI] {outcome.ToUpperInvariant()} at wave {_waveNum}");
        _stats.EmitRoundSummary(outcome, _roundNum, _waveNum);
        _stats.EmitSessionOutcome(outcome, _waveNum, _roundNum);
        BeginPostModeCooldown(outcome);
    }

    // changelevel <current map> is the only reliable full-engine reset: dead
    // guardians/walkers don't respawn, the Patron stays HP-pinned, and
    // citadel_match_end / mp_restartgame / street_brawl_reset don't apply in
    // dl_midtown standard mode. _modeOver stays true through the countdown so
    // stray hits can't re-enter EndMode; OnStartupServer re-inits after reload.
    private void BeginPostModeCooldown(string outcome)
    {
        DisarmWaves($"mode over: {outcome}");

        var notifications = new List<(int SecondsRemaining, string Message)>();
        for (int i = (int)PostModeCooldownSeconds; i >= 1; i--)
        {
            if (i == (int)PostModeCooldownSeconds || i == 20 || i == 10 || i <= 5)
                notifications.Add((i, $"[TI] Map restart in {i} second{(i == 1 ? "" : "s")}"));
        }
        notifications.Sort((a, b) => b.SecondsRemaining.CompareTo(a.SecondsRemaining));

        var totalSeconds = (int)PostModeCooldownSeconds;
        var notifIndex = 0;
        var elapsedSeconds = 0;

        _pendingWaveTimer?.Cancel();
        _pendingWaveTimer = Timer.Sequence(step =>
        {
            if (notifIndex < notifications.Count)
            {
                var (secondsRemaining, message) = notifications[notifIndex];
                var targetElapsed = totalSeconds - secondsRemaining;

                if (elapsedSeconds >= targetElapsed)
                {
                    Chat.PrintToChatAll(message);
                    Console.WriteLine($"[TI] {message}");
                    notifIndex++;

                    if (notifIndex < notifications.Count)
                    {
                        var nextSecondsRemaining = notifications[notifIndex].SecondsRemaining;
                        var waitSeconds = secondsRemaining - nextSecondsRemaining;
                        elapsedSeconds += waitSeconds;
                        return step.Wait(waitSeconds.Seconds());
                    }

                    DoChangeLevel(outcome);
                    return step.Done();
                }

                var waitUntilNext = targetElapsed - elapsedSeconds;
                elapsedSeconds = targetElapsed;
                return step.Wait(waitUntilNext.Seconds());
            }

            DoChangeLevel(outcome);
            return step.Done();
        }).CancelOnMapChange();
    }

    private static void DoChangeLevel(string outcome)
    {
        var map = Server.MapName;
        Console.WriteLine($"[TI] Post-mode reset ({outcome}) — changelevel {map}");
        Server.ExecuteCommand($"changelevel {map}");
    }
}
