using System.Linq;
using System.Numerics;
using DeadworksManaged.Api;

namespace CaptureTheFlag;

public partial class CaptureTheFlagPlugin
{
    private enum FlagState { Neutral, Carried, Dropped, Capturing }
    private enum MatchPhase { Warmup, RoundActive, Intermission, MatchEnd }

    private FlagState _flag = FlagState.Neutral;
    private MatchPhase _phase = MatchPhase.Warmup;
    private Vector3 _flagPos;

    private int _carrierSlot = -1;
    private int _carrierEnt = -1;
    private int _carrierTeam;
    private CBaseModifier? _carrierSpeedMod;
    private bool _speedModWarned;

    private float _dropDeadline;
    private float _captureDeadline;
    private int _outOfZoneTicks;
    private int _lastCountdownSecond = -1;

    private int _amberCaptures;
    private int _sapphireCaptures;
    private float _warmupDeadline = -1f;
    private float _matchEndDeadline;
    private float _roundDeadline;
    private float _intermissionDeadline;
    private int _roundsPlayed;
    private int _currentRound = 1; // 1-based number of the round in progress; drives economy + trooper scaling
    private int _humanCount;

    private bool IsCarried => _flag is FlagState.Carried or FlagState.Capturing;
    // The urn can be grabbed (by meleeing it) only while it's loose during a live round.
    private bool IsGrabbable => _phase == MatchPhase.RoundActive && _flag is FlagState.Neutral or FlagState.Dropped;
    private int _lastWarmupSecond = -1;

    private void ResetMatch()
    {
        _amberCaptures = 0;
        _sapphireCaptures = 0;
        _roundsPlayed = 0;
        _phase = MatchPhase.Warmup;
        _warmupDeadline = -1f;
        ResetFlagToCenter(announce: false);
        ArmWarmupIfPopulated();
    }

    private void ArmWarmupIfPopulated()
    {
        if (_phase == MatchPhase.Warmup && _humanCount > 0 && _warmupDeadline < 0f)
        {
            _warmupDeadline = GlobalVars.CurTime + Config.WarmupSeconds;
            _lastWarmupSecond = -1;
            Chat.PrintToChatAll($"[CTF] Warmup — round in {Config.WarmupSeconds:F0}s. First to {Config.CapturesToWin} wins.");
            AnnounceStage();
        }
    }

    private void Poll()
    {
        if (!GameRules.IsValid) return;

        // Defer the world scan + idol spawn until the map's entities exist (creating entities
        // in OnStartupServer crashes the engine). Hold off all round logic until it's done.
        if (!_worldInit) { TryInitWorld(); if (!_worldInit) return; }

        float now = GlobalVars.CurTime;
        CheckAfk(now);

        switch (_phase)
        {
            case MatchPhase.Warmup:
                if (_humanCount > 0 && _warmupDeadline >= 0f)
                {
                    AnnounceCountdownTo(_warmupDeadline, now);
                    if (now >= _warmupDeadline) StartRound();
                }
                break;
            case MatchPhase.RoundActive:
                PollRound(now);
                break;
            case MatchPhase.Intermission:
                AnnounceCountdownTo(_intermissionDeadline, now);
                if (now >= _intermissionDeadline) StartRound();
                break;
            case MatchPhase.MatchEnd:
                if (now >= _matchEndDeadline) { RebalanceTeams(); ResetMatch(); }
                break;
        }
    }

    private void StartRound()
    {
        _phase = MatchPhase.RoundActive;
        _currentRound = Math.Clamp(_roundsPlayed + 1, 1, Math.Max(1, Config.RoundsPerMatch));
        _roundDeadline = GlobalVars.CurTime + Config.RoundSeconds;
        _clockStart = GlobalVars.CurTime; // HUD clock counts this round
        RespawnAllInBase();
        ResetRoundNpcs();
        GrantRoundEconomy();
        ResetFlagToCenter(announce: false);
        AnnounceStage();
        Chat.PrintToChatAll($"[CTF] Round {_currentRound}/{Config.RoundsPerMatch} — melee the urn!");
    }

    // Every player starts each round with the same souls + ability points, scaling from low in
    // round 1 to ~12 tier-4 items' worth in the final round.
    private void GrantRoundEconomy()
    {
        int round = _currentRound;
        float frac = Config.RoundsPerMatch <= 1 ? 1f : (round - 1f) / (Config.RoundsPerMatch - 1);
        int perSlot = (int)MathF.Round(Lerp(Config.ItemTierMinPrice, Config.ItemTierMaxPrice, frac));
        int souls = Config.ItemSlots * perSlot;
        int ap = (int)MathF.Round(Lerp(Config.AbilityPointsFirstRound, Config.AbilityPointsLastRound, frac));

        foreach (var ctrl in Players.GetAll())
        {
            var pawn = ctrl.GetHeroPawn()?.As<CCitadelPlayerPawn>();
            if (pawn == null) continue;
            pawn.SetCurrency(ECurrencyType.EGold, souls);
            pawn.SetCurrency(ECurrencyType.EAbilityPoints, ap);
        }
        Announce($"Round {round}", $"{souls:N0} souls · {ap} AP · tier {1 + (int)MathF.Round(frac * 3)}");
        Console.WriteLine($"[CTF] Round {round} economy: {souls} souls, {ap} AP ({Config.ItemSlots}x{perSlot})");
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    // HUD countdown to a deadline (e.g. "Round starts in 3…"), once per second, from 5 down to 1.
    private void AnnounceCountdownTo(float deadline, float now)
    {
        int secs = (int)MathF.Ceiling(deadline - now);
        if (secs != _lastWarmupSecond && secs >= 1 && secs <= 5)
        {
            _lastWarmupSecond = secs;
            Announce($"Round in {secs}…", "Melee the urn (minimap) → enemy base.");
        }
    }

    // Teleport every player to one of their team's real spawn points at round start (falls back
    // to the base-zone centre only if no team spawn points were found).
    private void RespawnAllInBase()
    {
        foreach (var ctrl in Players.GetAll())
        {
            var pawn = ctrl.GetHeroPawn()?.As<CCitadelPlayerPawn>();
            if (pawn == null || !pawn.IsAlive) continue;

            int team = ctrl.TeamNum;
            Vector3 target;
            if (_teamSpawns.TryGetValue(team, out var spawns) && spawns.Count > 0 && _baseZones.TryGetValue(team, out var zone))
            {
                // Of the ~44 scattered team spawns, the normal fountain spawns are the cluster
                // nearest the base (Patron) — pick among the closest few, not a random one.
                var c = (zone.Min + zone.Max) * 0.5f;
                var baseSpawns = spawns.OrderBy(s => Vector3.DistanceSquared(s, c)).Take(6).ToList();
                target = baseSpawns[Random.Shared.Next(baseSpawns.Count)];
            }
            else if (_baseZones.TryGetValue(team, out var z2))
                target = (z2.Min + z2.Max) * 0.5f;
            else continue;

            pawn.Teleport(position: target);
        }
    }

    private void PollRound(float now)
    {
        // 3-minute round cap: if nobody captures in time, the round ends with no winner.
        if (now >= _roundDeadline) { EndRound(null, now); return; }

        switch (_flag)
        {
            // Pickup is melee-triggered (OnTakeDamage on the urn) — no proximity auto-grab here.
            case FlagState.Dropped:
                if (now >= _dropDeadline) ResetFlagToCenter(announce: true);
                break;

            case FlagState.Neutral:
                break;

            case FlagState.Carried:
            case FlagState.Capturing:
                PollCarrier(now);
                break;
        }
    }

    private void PollCarrier(float now)
    {
        var carrier = CBaseEntity.FromIndex<CCitadelPlayerPawn>(_carrierEnt);
        if (carrier == null || !carrier.IsAlive)
        {
            // Safety net — the death hook normally drops the flag first.
            DropFlag(_flagPos);
            return;
        }

        _flagPos = carrier.Position;
        bool inEnemyZone = _baseZones.TryGetValue(EnemyOf(_carrierTeam), out var zone) && zone.Contains(_flagPos);

        if (_flag == FlagState.Carried)
        {
            if (inEnemyZone) BeginCapture(now);
            return;
        }

        // Capturing
        if (inEnemyZone)
        {
            _outOfZoneTicks = 0;
            if (now >= _captureDeadline) { ScoreCapture(now); return; }
            AnnounceCountdown(now);
        }
        else if (++_outOfZoneTicks >= 2)
        {
            // Single-tick boundary oscillation is forgiven; two consecutive ticks out cancels.
            _flag = FlagState.Carried;
            _captureDeadline = 0f;
            _lastCountdownSecond = -1;
            Chat.PrintToChatAll("[CTF] Capture stopped — left the zone.");
            AnnounceStage();
        }
    }

    private void TryPickup()
    {
        float bestSq = Config.PickupRadius * Config.PickupRadius;
        CCitadelPlayerPawn? best = null;
        foreach (var pawn in Players.GetAllPawns())
        {
            if (pawn == null || !pawn.IsAlive) continue;
            float dx = pawn.Position.X - _flagPos.X;
            float dy = pawn.Position.Y - _flagPos.Y;
            float sq = dx * dx + dy * dy;
            if (sq <= bestSq) { bestSq = sq; best = pawn; }
        }
        if (best != null) Pickup(best);
    }

    private void Pickup(CCitadelPlayerPawn pawn)
    {
        _flag = FlagState.Carried;
        _carrierEnt = pawn.EntityIndex;
        _carrierSlot = pawn.Controller?.Slot ?? -1;
        _carrierTeam = pawn.TeamNum;
        _flagPos = pawn.Position;
        _captureDeadline = 0f;
        _outOfZoneTicks = 0;
        _lastCountdownSecond = -1;
        ApplyCarrierSpeed(pawn);

        string who = pawn.Controller?.PlayerName ?? "Someone";
        Chat.PrintToChatAll($"[CTF] {who} ({TeamName(_carrierTeam)}) grabbed the urn!");
        AnnounceStage();
    }

    private void BeginCapture(float now)
    {
        _flag = FlagState.Capturing;
        _captureDeadline = now + Config.CaptureHoldSeconds;
        _outOfZoneTicks = 0;
        _lastCountdownSecond = -1;
        Chat.PrintToChatAll($"[CTF] {TeamName(_carrierTeam)} capturing! Hold {Config.CaptureHoldSeconds:F0}s…");
        AnnounceStage();
    }

    private void AnnounceCountdown(float now)
    {
        int secondsLeft = (int)MathF.Ceiling(_captureDeadline - now);
        if (secondsLeft > 0 && secondsLeft <= 5 && secondsLeft != _lastCountdownSecond)
        {
            _lastCountdownSecond = secondsLeft;
            Chat.PrintToChatAll($"[CTF] Capturing… {secondsLeft}");
        }
    }

    private void ScoreCapture(float now) => EndRound(_carrierTeam, now);

    // Ends the current round: tally the winner (if any), announce the running score, and either
    // end the match (best-of-N reached) or go to a short intermission before the next round.
    private void EndRound(int? winner, float now)
    {
        var carrier = _carrierEnt >= 0 ? CBaseEntity.FromIndex<CCitadelPlayerPawn>(_carrierEnt) : null;
        ClearCarrier(carrier);
        _flag = FlagState.Neutral;
        _flagPos = PickSpawnPoint();
        _roundsPlayed++;

        string result;
        if (winner is int w)
        {
            if (w == Amber) _amberCaptures++; else if (w == Sapphire) _sapphireCaptures++;
            result = $"{TeamName(w)} scored!";
        }
        else result = "Round over — no capture.";

        Chat.PrintToChatAll($"[CTF] {result}  {_amberCaptures}–{_sapphireCaptures}  (R{_roundsPlayed}/{Config.RoundsPerMatch})");
        Announce(result, $"Amber {_amberCaptures} – {_sapphireCaptures} Sapphire · R{_roundsPlayed}/{Config.RoundsPerMatch}");

        bool matchOver = _amberCaptures >= Config.CapturesToWin
            || _sapphireCaptures >= Config.CapturesToWin
            || _roundsPlayed >= Config.RoundsPerMatch;
        if (matchOver) { EndMatch(now); return; }

        _phase = MatchPhase.Intermission;
        _intermissionDeadline = now + Config.IntermissionSeconds;
        _lastWarmupSecond = -1; // fresh 5→1 countdown into the next round
    }

    private void EndMatch(float now)
    {
        _phase = MatchPhase.MatchEnd;
        _matchEndDeadline = now + Config.MatchEndDelaySeconds;
        string title = _amberCaptures == _sapphireCaptures
            ? "Match Draw!"
            : $"{TeamName(_amberCaptures > _sapphireCaptures ? Amber : Sapphire)} wins the match!";
        Announce(title, $"Final: Amber {_amberCaptures} – {_sapphireCaptures} Sapphire");
        Chat.PrintToChatAll($"[CTF] {title}  {_amberCaptures}–{_sapphireCaptures}. Next match in {Config.MatchEndDelaySeconds:F0}s.");
    }

    // Even-split shuffle between matches (after a best-of-N). Simple count balance, not rank-based.
    private void RebalanceTeams()
    {
        var players = Players.GetAll().Where(c => c.TeamNum == Amber || c.TeamNum == Sapphire).ToList();
        if (players.Count < 2) return;
        for (int i = players.Count - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (players[i], players[j]) = (players[j], players[i]);
        }
        for (int i = 0; i < players.Count; i++)
        {
            int team = i % 2 == 0 ? Amber : Sapphire;
            if (players[i].TeamNum != team) players[i].ChangeTeam(team);
        }
        Chat.PrintToChatAll("[CTF] Teams rebalanced.");
    }

    // Carrier loses the flag without scoring (death, or carrier pawn lost). Drops at pos.
    private void DropFlag(Vector3 pos)
    {
        if (!IsCarried) return;
        int team = _carrierTeam;
        var carrier = CBaseEntity.FromIndex<CCitadelPlayerPawn>(_carrierEnt);
        ClearCarrier(carrier);
        _flag = FlagState.Dropped;
        _flagPos = pos;
        _dropDeadline = GlobalVars.CurTime + Config.FlagResetSeconds;
        Chat.PrintToChatAll($"[CTF] {TeamName(team)} dropped the urn! Resets in {Config.FlagResetSeconds:F0}s.");
        AnnounceStage();
    }

    private void ResetFlagToCenter(bool announce)
    {
        var carrier = _carrierEnt >= 0 ? CBaseEntity.FromIndex<CCitadelPlayerPawn>(_carrierEnt) : null;
        ClearCarrier(carrier);
        _flag = FlagState.Neutral;
        _flagPos = PickSpawnPoint();
        _dropDeadline = 0f;
        if (announce) { Chat.PrintToChatAll("[CTF] Urn reset — check minimap."); AnnounceStage(); }
    }

    private void ClearCarrier(CCitadelPlayerPawn? pawn)
    {
        RemoveCarrierSpeed(pawn);
        _carrierSlot = -1;
        _carrierEnt = -1;
        _carrierTeam = 0;
        _captureDeadline = 0f;
        _outOfZoneTicks = 0;
        _lastCountdownSecond = -1;
    }

    private void ApplyCarrierSpeed(CCitadelPlayerPawn pawn)
    {
        _carrierSpeedMod = null;
        if (string.IsNullOrWhiteSpace(Config.CarrierSpeedModifier)) return;
        try
        {
            _carrierSpeedMod = pawn.AddModifier(
                Config.CarrierSpeedModifier,
                new Dictionary<string, float> { [Config.CarrierSpeedProperty] = Config.CarrierSpeedBonusPercent });
            if (_carrierSpeedMod == null && !_speedModWarned)
            {
                _speedModWarned = true;
                Console.WriteLine($"[CTF] CarrierSpeedModifier '{Config.CarrierSpeedModifier}' could not be applied — verify the modifier name on the live server. Speed buff is skipped; the no-fire escort mechanic is unaffected.");
            }
        }
        catch (Exception ex)
        {
            if (!_speedModWarned) { _speedModWarned = true; Console.WriteLine($"[CTF] CarrierSpeedModifier error: {ex.Message}"); }
        }
    }

    private void RemoveCarrierSpeed(CCitadelPlayerPawn? pawn)
    {
        if (pawn != null)
        {
            if (_carrierSpeedMod != null) pawn.RemoveModifier(_carrierSpeedMod);
            else if (!string.IsNullOrWhiteSpace(Config.CarrierSpeedModifier)) pawn.RemoveModifier(Config.CarrierSpeedModifier);
        }
        _carrierSpeedMod = null;
    }

    private void Announce(string title, string desc)
    {
        NetMessages.Send(new CCitadelUserMsg_HudGameAnnouncement
        {
            TitleLocstring = title,
            DescriptionLocstring = desc,
        }, RecipientFilter.All);
    }

    // Broadcast a HUD announcement telling everyone what the current stage demands. Called on
    // each phase/flag-state transition. Score/win banners are handled separately.
    private void AnnounceStage()
    {
        string title, desc;
        if (_phase == MatchPhase.Warmup)
        {
            title = "Capture the Urn";
            desc = $"Melee the urn (minimap) → enemy base. First to {Config.CapturesToWin} wins.";
        }
        else
        {
            string carrier = TeamName(_carrierTeam);
            string enemy = TeamName(EnemyOf(_carrierTeam));
            switch (_flag)
            {
                case FlagState.Neutral:
                    title = "Grab the Urn";
                    desc = "Melee the urn (minimap), then run it to the enemy base.";
                    break;
                case FlagState.Dropped:
                    title = "Urn Dropped";
                    desc = $"Grab it (minimap) before it resets in {Config.FlagResetSeconds:F0}s.";
                    break;
                case FlagState.Carried:
                    title = $"{carrier} has the Urn";
                    desc = $"{carrier}: escort to {enemy} base.  {enemy}: kill the carrier!";
                    break;
                case FlagState.Capturing:
                    title = $"{carrier} Capturing!";
                    desc = $"Hold {enemy} base {Config.CaptureHoldSeconds:F0}s.  {enemy}: stop them!";
                    break;
                default:
                    return;
            }
        }
        Announce(title, desc);
    }
}
