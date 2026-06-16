using DeadworksManaged.Api;
using TrooperInvasion.Stats;

namespace TrooperInvasion;

public partial class TrooperInvasionPlugin
{
    [GameEventHandler("gameover_msg")]
    public HookResult OnGameoverMsg(GameoverMsgEvent args)
    {
        if (!_modeOver)
        {
            // OnTakeDamage didn't intercept — the engine ran its native end
            // sequence. Kick off an immediate changelevel so the server doesn't
            // hang indefinitely with no players and no reset timer.
            Console.WriteLine("[TI] Safety: native gameover fired before EndMode — triggering immediate changelevel.");
            _modeOver = true;
            DoChangeLevel("safety-reset");
        }
        return HookResult.Stop;
    }

    [GameEventHandler("round_end")]
    public HookResult OnRoundEnd(RoundEndEvent args) => HookResult.Stop;

    // Intercepts the FINAL killing blow on either Patron. The Patron is a two-phase
    // boss: the first real death drops the Shrine form, which the engine revives
    // ~15-20s later as the mobile Patron — so phase 1 deaths are let through to the
    // engine untouched. Only the second (final) death flips m_eGameState → PostGame
    // (kicks every client, blocks joins until a map reload); we swallow that lethal
    // hit, pin HP to 1, and call EndMode ourselves. Non-lethal damage passes through
    // so the HUD still ticks down.
    public override HookResult OnTakeDamage(TakeDamageEvent args)
    {
        if (args.Entity.DesignerName != PatronDesigner) return HookResult.Continue;

        // Patron is invulnerable through the cooldown so stray hits can't
        // re-trigger the defeat handler.
        if (_modeOver)
            return HookResult.Stop;

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
            args.Entity.Health = args.Entity.MaxHealth;
            Console.WriteLine($"[TI] Absorbed scripted weaken-Patron event (team {args.Entity.TeamNum}) — Patron pinned at full HP.");
            return HookResult.Stop;
        }

        if (args.Entity.Health - args.Info.Damage > 0f) return HookResult.Continue;

        // Only a real player/trooper blow can drop a phase — any scripted/world
        // lethal hit (incl. the weaken hit handled above) must never kill the Patron.
        var attacker = args.Info.Attacker;
        bool realKill = attacker != null &&
            (attacker.As<CCitadelPlayerPawn>() != null || IsTrooperDesigner(attacker.DesignerName));
        if (!realKill)
        {
            args.Entity.Health = 1;
            return HookResult.Stop;
        }

        // Debounce the killing blow: a single death can land several lethal damage
        // events in one tick. Phase 2 is unreachable inside the window because the
        // boss is invulnerable for the whole ~15-20s transform, so anything within
        // PatronTransformDebounceSeconds of the last counted down is the same death.
        int team = args.Entity.TeamNum;
        var now = DateTime.UtcNow;
        DateTime? lastDown = team == HumanTeam ? _humanPatronDownAt : _enemyPatronDownAt;
        if (lastDown.HasValue && (now - lastDown.Value).TotalSeconds < PatronTransformDebounceSeconds)
        {
            args.Entity.Health = 1;
            return HookResult.Stop;
        }

        int downs = team == HumanTeam ? ++_humanPatronDowns : ++_enemyPatronDowns;
        if (team == HumanTeam) _humanPatronDownAt = now; else _enemyPatronDownAt = now;

        if (downs < PatronPhases)
        {
            // Phase 1 down: let the lethal blow through so the engine kills the Shrine
            // form and runs its transform to the next phase. Do NOT pin, do NOT end.
            Console.WriteLine($"[TI] Patron (team {team}) phase {downs}/{PatronPhases} down — engine reviving it as the next phase.");
            return HookResult.Continue;
        }

        // Final phase down: block the lethal hit so the engine never flips
        // m_eGameState → PostGame (which kicks everyone), then end the mode ourselves.
        // HookResult.Stop is required — Continue passes damage through to the engine
        // (even if zeroed), which still causes the entity kill and native game-over.
        args.Entity.Health = 1;
        EndMode(victory: team != HumanTeam);
        return HookResult.Stop;
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

        // A phase-1 Patron (npc_boss_tier3) death DOES reach here (OnTakeDamage lets
        // it through to transform); it is neither IsGuardianDesigner nor
        // IsTrooperDesigner and is not a player pawn, so every branch below skips it.
        // Watchers (npc_barrack_boss) likewise die normally and skip the bounty branch.
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
