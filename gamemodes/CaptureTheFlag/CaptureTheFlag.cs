using System.Numerics;
using DeadworksManaged.Api;

namespace CaptureTheFlag;

// One-Flag Capture-the-Flag, fully server-side. A single neutral flag is simulated
// in plugin state (no custom map entity): "grabbing" is a proximity check, "carrying"
// is a per-pawn marker, and all feedback is chat + HUD announcements. The carrier
// cannot fire their weapon (forcing an escort), drops the flag on death, and scores
// by holding it in the enemy base zone. First team to CapturesToWin wins the match.
//
// Implementation is split across partials:
//   CaptureTheFlag.Flag.cs     — flag state machine, poll loop, carrier effects
//   CaptureTheFlag.Players.cs  — join/leave/death, team balance, weapon-fire block
//   CaptureTheFlag.Commands.cs — !flag / !score / !help chat commands
public partial class CaptureTheFlagPlugin : DeadworksPluginBase
{
    public override string Name => "CaptureTheFlag";

    private const int Amber = 2;
    private const int Sapphire = 3;

    // Only the neutral mid boss is removed — the urn replaces it as the mid objective (and it's
    // safe to delete). Troopers, Guardians and Walkers spawn and fight as normal.
    private static readonly HashSet<string> NpcsToStrip = new() { "npc_mid_boss" };

    // Lane combatants: spawn + fight normally, are killable, and get reset every round. Only
    // these (plus players) are allowed to deal damage to players (see OnTakeDamage).
    private static readonly HashSet<string> LaneCombatants = new()
    {
        "npc_trooper",
        "npc_trooper_boss",
        "npc_boss_tier1",   // Guardian
        "npc_boss_tier2",   // Walker
    };
    private static readonly HashSet<string> Troopers = new() { "npc_trooper", "npc_trooper_boss" };
    private static readonly HashSet<string> GuardiansAndWalkers = new() { "npc_boss_tier1", "npc_boss_tier2" };

    // Endgame "base bosses" (Patron, Watcher): they can't be Remove()'d (deleting them crashes the
    // objective system), so they're HIDDEN (shrunk to invisible + collision zeroed), kept immortal
    // so the match can't end on a base kill, and can't damage players. Guardians/Walkers are left
    // visible and normal — only these are hidden.
    private static readonly HashSet<string> ProtectedObjectives = new()
    {
        "npc_boss_tier3",   // Patron
        "npc_barrack_boss", // Watcher
    };
    private const string PatronDesigner = "npc_boss_tier3";
    private const string WalkerDesigner = "npc_boss_tier2";

    private void NeutralizeBoss(CBaseEntity e)
    {
        try
        {
            e.SetScale(0.0001f);           // shrink to effectively invisible
            var col = e.Collision;
            if (col != null) { col.Mins = Vector3.Zero; col.Maxs = Vector3.Zero; } // pass-through
        }
        catch { /* best-effort hide; immortality (OnTakeDamage) is the real guarantee */ }
    }

    // Match clock pinning — keep m_eGameState at GameInProgress and the HUD clock sane
    // so the engine never shows its own win screen (CTF runs forever, match-by-match).
    private static readonly SchemaAccessor<float> _gameStartTime = new("CCitadelGameRules"u8, "m_flGameStartTime"u8);
    private static readonly SchemaAccessor<float> _levelStartTime = new("CCitadelGameRules"u8, "m_fLevelStartTime"u8);
    private static readonly SchemaAccessor<float> _roundStartTime = new("CCitadelGameRules"u8, "m_flRoundStartTime"u8);
    private static readonly SchemaAccessor<float> _matchClockAtLastUpdate = new("CCitadelGameRules"u8, "m_flMatchClockAtLastUpdate"u8);
    private static readonly SchemaAccessor<int> _matchClockUpdateTick = new("CCitadelGameRules"u8, "m_nMatchClockUpdateTick"u8);
    private static readonly SchemaAccessor<uint> _eGameState = new("CCitadelGameRules"u8, "m_eGameState"u8);

    private const string NeutralCampDesigner = "info_neutral_trooper_camp";
    private const string TeamSpawnDesigner = "info_team_spawn";

    // team -> that team's real player spawn points (info_player_teamspawn). Used to respawn
    // players in their base at round start instead of dumping them on the Patron.
    private readonly Dictionary<int, List<Vector3>> _teamSpawns = new();

    private Vector3 _center;
    // Candidate idol spawn locations for variety: every neutral creep camp plus the actual
    // mid-boss spawn (captured if it ever spawns). Re-rolled each time the idol returns/scores,
    // with a small 3D jitter (x/y/z) around the chosen point.
    private readonly List<Vector3> _spawnPoints = new();
    private Vector3? _midBossSpawn;
    private const float SpawnJitterRadius = 80f;
    private readonly Dictionary<int, Aabb> _baseZones = new(); // team -> enemy-defended AABB

    private Vector3 PickSpawnPoint()
    {
        if (_spawnPoints.Count == 0) return _center;
        var p = _spawnPoints[Random.Shared.Next(_spawnPoints.Count)];
        // Small horizontal jitter only — keep z at the spawn ground (the idle ground-clearance
        // offset lifts the model just enough to not bury it; no vertical jitter, which floated it).
        float ang = Random.Shared.NextSingle() * MathF.Tau;
        float r = Random.Shared.NextSingle() * SpawnJitterRadius;
        return new Vector3(p.X + r * MathF.Cos(ang), p.Y + r * MathF.Sin(ang), p.Z);
    }
    private float _clockStart = -1f;
    private IHandle? _pollTimer;
    private IHandle? _clockTimer;

    [PluginConfig]
    public CaptureTheFlagConfig Config { get; set; } = new();

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine(isReload ? "CaptureTheFlag reloaded!" : "CaptureTheFlag loaded!");
    }

    public override void OnStartupServer()
    {
        ApplyConVars();

        // The map's NPCs (Patron/Walkers/troopers) are NOT yet spawned at OnStartupServer on
        // an -insecure server, and creating an entity this early crashes the engine (the entity
        // system isn't ready). So the world scan + zone build + idol spawn are deferred to
        // TryInitWorld(), driven from the poll loop once GameRules is live and the base anchors
        // have appeared. (Same reason TrooperInvasion spawns its guardians late, not at startup.)
        _worldInit = false;
        _initAttempts = 0;
        _baseZones.Clear();
        _center = Vector3.Zero;
        RemoveIdol();

        ResetMatch();
        _clockStart = -1f;
        // The plugin instance survives map changes, so cancel any prior timers before
        // re-arming or each map load stacks another permanent per-tick timer.
        _pollTimer?.Cancel();
        _pollTimer = Timer.Every(Math.Max(1, Config.PollIntervalTicks).Ticks(), Poll);
        _clockTimer?.Cancel();
        _clockTimer = Timer.Every(1.Ticks(), TickMatchClock);
    }

    // Deferred one-time world setup: wait for the base anchors (Patron/Walker) to spawn, read
    // their positions, build the scoring zones, strip every NPC, and spawn the idol flag. Runs
    // from Poll() until it succeeds. Entity creation is safe here (post map-load), unlike in
    // OnStartupServer.
    private bool _worldInit;
    private int _initAttempts;
    private const int MaxInitAttempts = 192; // give up waiting for anchors after ~a few seconds

    private void TryInitWorld()
    {
        var patrons = new Dictionary<int, Vector3>();
        var walkers = new Dictionary<int, List<Vector3>>();
        foreach (var ent in Entities.All)
        {
            var name = ent.DesignerName;
            if (name == PatronDesigner && ent.Position != Vector3.Zero)
                patrons[ent.TeamNum] = ent.Position;
            else if (name == WalkerDesigner && ent.Position != Vector3.Zero)
            {
                if (!walkers.TryGetValue(ent.TeamNum, out var l)) walkers[ent.TeamNum] = l = new();
                l.Add(ent.Position);
            }
        }

        // Anchors appear a few ticks after the map loads — keep retrying until they do, but
        // don't block forever: after the cap, init anyway (idol at best-known center).
        if (patrons.Count == 0 && walkers.Count == 0 && ++_initAttempts < MaxInitAttempts)
            return;

        BuildZones(patrons, walkers);

        int stripped = 0, hidden = 0;
        _spawnPoints.Clear();
        _teamSpawns.Clear();
        foreach (var ent in Entities.All)
        {
            var name = ent.DesignerName;
            if (name == NeutralCampDesigner && ent.Position != Vector3.Zero) _spawnPoints.Add(ent.Position);
            if (name == MidBossDesigner && ent.Position != Vector3.Zero) _midBossSpawn = ent.Position;
            if (name == TeamSpawnDesigner && ent.Position != Vector3.Zero)
            {
                if (!_teamSpawns.TryGetValue(ent.TeamNum, out var l)) _teamSpawns[ent.TeamNum] = l = new();
                l.Add(ent.Position);
            }
            if (NpcsToStrip.Contains(name)) { ent.Remove(); stripped++; }
            else if (ProtectedObjectives.Contains(name)) { NeutralizeBoss(ent); hidden++; }
        }
        if (_midBossSpawn is Vector3 mb) _spawnPoints.Add(mb); // actual mid-boss spawn (not map mid)

        _flagPos = PickSpawnPoint();
        SpawnIdol(_flagPos);
        _worldInit = true;
        Console.WriteLine($"[CTF] World init (attempt {_initAttempts}): stripped {stripped} mid boss(es), hid {hidden} base bosses; {_spawnPoints.Count} idol spawns; team spawns [{string.Join(",", _teamSpawns.Select(kv => $"t{kv.Key}x{kv.Value.Count}"))}]; zones [{string.Join(",", _baseZones.Keys)}]; center {_center:F0}");
    }

    public override void OnConfigReloaded()
    {
        ApplyConVars();
        _pollTimer?.Cancel();
        _pollTimer = Timer.Every(Math.Max(1, Config.PollIntervalTicks).Ticks(), Poll);
        Console.WriteLine("[CTF] Config reloaded.");
    }

    private void ApplyConVars()
    {
        // Enable creep spawning (troopers fight in lanes as normal) + full-build purchasing anywhere.
        ConVar.Find("citadel_npc_spawn_enabled")?.SetInt(1);
        ConVar.Find("citadel_allow_purchasing_anywhere")?.SetInt(1);
        ConVar.Find("citadel_player_spawn_time_max_respawn_time")?.SetInt((int)MathF.Round(Config.RespawnDelaySeconds));
    }

    private void BuildZones(Dictionary<int, Vector3> patrons, Dictionary<int, List<Vector3>> walkers)
    {
        _baseZones.Clear();
        foreach (var (team, pos) in patrons)
            _baseZones[team] = Aabb.Around(pos, Config.BaseZoneRadius, Config.BaseZoneRadius * 2f);

        // Fallback: if a playable team's Patron is missing on this map, anchor its base
        // zone on its Walker centroid instead, so the enemy can still score there. Without
        // this a missing Patron would make captures into that base impossible.
        foreach (int team in new[] { Amber, Sapphire })
        {
            if (_baseZones.ContainsKey(team)) continue;
            if (walkers.TryGetValue(team, out var w) && w.Count > 0)
            {
                var c = new Vector3(w.Average(p => p.X), w.Average(p => p.Y), w.Average(p => p.Z));
                _baseZones[team] = Aabb.Around(c, Config.BaseZoneRadius, Config.BaseZoneRadius * 2f);
                Console.WriteLine($"[CTF] {TeamName(team)} Patron missing — base zone anchored on Walker centroid {c:F0}");
            }
            else
            {
                Console.WriteLine($"[CTF] WARNING: no Patron or Walker anchor for {TeamName(team)} — captures into its base are disabled this map.");
            }
        }

        // Center: midpoint of the two Patrons when both known, else centroid of every
        // captured base anchor (Patrons + Walkers). Avoids hardcoded map coordinates.
        if (patrons.TryGetValue(Amber, out var a) && patrons.TryGetValue(Sapphire, out var s))
            _center = (a + s) * 0.5f;
        else
        {
            var all = patrons.Values.Concat(walkers.Values.SelectMany(v => v)).ToList();
            _center = all.Count > 0
                ? new Vector3(all.Average(p => p.X), all.Average(p => p.Y), all.Average(p => p.Z))
                : Vector3.Zero;
        }
    }

    private void TickMatchClock()
    {
        if (!GameRules.IsValid) return;
        var ptr = GameRules.Pointer;

        if (_clockStart < 0f) _clockStart = GlobalVars.CurTime;
        float elapsed = GlobalVars.CurTime - _clockStart;

        // Both the anchor float and its tick must be written together each tick, or the
        // client extrapolates the HUD clock from a stale anchor and it keeps climbing.
        float pausedOffset = GameRules.TotalPausedTicks * GlobalVars.IntervalPerTick;
        float anchor = GlobalVars.CurTime - elapsed - pausedOffset;
        _gameStartTime.Set(ptr, anchor);
        _levelStartTime.Set(ptr, anchor);
        _roundStartTime.Set(ptr, anchor);
        _matchClockAtLastUpdate.Set(ptr, elapsed);
        _matchClockUpdateTick.Set(ptr, GlobalVars.TickCount);

        if ((EGameState)_eGameState.Get(ptr) != EGameState.GameInProgress)
            _eGameState.Set(ptr, (uint)EGameState.GameInProgress);

        TickIdol(); // drive the idol every tick for a smooth carry trace + idle rendering
    }

    [GameEventHandler("gameover_msg")]
    public HookResult OnGameoverMsg(GameoverMsgEvent args) => HookResult.Stop;

    [GameEventHandler("round_end")]
    public HookResult OnRoundEnd(RoundEndEvent args) => HookResult.Stop;

    public override void OnEntitySpawned(EntitySpawnedEvent e)
    {
        var name = e.Entity.DesignerName;
        // Capture the mid-boss spawn position (it spawns on a timer, after world init) for the
        // idol spawn pool — before we delete the mid boss itself.
        if (name == MidBossDesigner && _midBossSpawn == null && e.Entity.Position != Vector3.Zero)
        {
            _midBossSpawn = e.Entity.Position;
            _spawnPoints.Add(e.Entity.Position);
            Console.WriteLine($"[CTF] captured mid-boss spawn {e.Entity.Position:F0}");
        }
        // Mid boss deleted on sight; Patron/Watcher hidden; troopers scaled. Guardians/Walkers normal.
        if (NpcsToStrip.Contains(name))
            e.Entity.Remove();
        else if (_worldInit && ProtectedObjectives.Contains(name))
            NeutralizeBoss(e.Entity);
        else if (Troopers.Contains(name))
            ScaleTrooperForRound(e.Entity);
    }

    // Light per-round health bump for lane troopers (set both MaxHealth + Health — health doesn't
    // auto-clamp to the new max). Far gentler than TrooperInvasion's PvE scaling.
    private void ScaleTrooperForRound(CBaseEntity ent)
    {
        int baseMax = ent.MaxHealth;
        if (baseMax <= 0) return;
        float scale = Math.Min(Config.MaxTrooperHealthScale,
                               1f + (_currentRound - 1) * Config.TrooperHealthScalePerRound);
        int scaled = (int)(baseMax * scale);
        ent.MaxHealth = scaled;
        ent.Health = scaled;
    }

    // Reset the lane NPCs at the start of each round: clear all troopers (the spawn system makes
    // fresh waves) and heal any surviving Guardians/Walkers back to full.
    private void ResetRoundNpcs()
    {
        int cleared = 0, healed = 0;
        foreach (var ent in Entities.All)
        {
            var name = ent.DesignerName;
            if (Troopers.Contains(name)) { ent.Remove(); cleared++; }
            else if (GuardiansAndWalkers.Contains(name))
            {
                int max = ent.MaxHealth;
                if (max > 0 && ent.Health < max) { ent.Health = max; healed++; }
            }
        }
        if (cleared > 0 || healed > 0)
            Console.WriteLine($"[CTF] Round reset: cleared {cleared} troopers, healed {healed} guardians/walkers.");
    }

    public override HookResult OnTakeDamage(TakeDamageEvent args)
    {
        var e = args.Entity;
        var attacker = args.Info.Attacker;

        // The urn is invulnerable; a melee hit on it is the pickup trigger.
        if (_idolEnt >= 0 && e.EntityIndex == _idolEnt)
        {
            args.Info.Damage = 0f;
            args.Info.TotalledDamage = 0f;
            bool melee = (args.Info.DamageFlags & (TakeDamageFlags.LightMelee | TakeDamageFlags.HeavyMelee)) != 0;
            if (IsGrabbable && melee)
            {
                var apawn = attacker?.As<CCitadelPlayerPawn>();
                if (apawn != null && apawn.IsAlive) Pickup(apawn);
            }
            return HookResult.Continue;
        }

        // Endgame objectives (Patron, Watcher) are immortal so the match can't end on a base kill.
        if (ProtectedObjectives.Contains(e.DesignerName))
        {
            args.Info.Damage = 0f;
            args.Info.TotalledDamage = 0f;
            return HookResult.Continue;
        }

        // Damage to a PLAYER is allowed only from another player or a lane combatant
        // (trooper / guardian / walker). Anything else hurting a player — Patron, Watcher,
        // sentries, world/fall damage — is nullified.
        if (e.Is<CCitadelPlayerPawn>())
        {
            bool fromPlayer = attacker != null && attacker.Is<CCitadelPlayerPawn>();
            bool fromCombatant = attacker != null && LaneCombatants.Contains(attacker.DesignerName);
            if (!fromPlayer && !fromCombatant)
            {
                args.Info.Damage = 0f;
                args.Info.TotalledDamage = 0f;
            }
        }
        // Damage to troopers/guardians/walkers (and between NPCs) is left untouched — normal combat.
        return HookResult.Continue;
    }

    private static string TeamName(int team) => team switch
    {
        Amber => "Amber",
        Sapphire => "Sapphire",
        _ => $"Team{team}",
    };

    private static int EnemyOf(int team) => team == Amber ? Sapphire : Amber;

    public override void OnUnload()
    {
        _pollTimer?.Cancel();
        _clockTimer?.Cancel();
        RemoveIdol();
        Console.WriteLine("CaptureTheFlag unloaded!");
    }

    private readonly record struct Aabb(Vector3 Min, Vector3 Max)
    {
        public bool Contains(Vector3 p, float margin = 20f) =>
            p.X >= Min.X - margin && p.X <= Max.X + margin &&
            p.Y >= Min.Y - margin && p.Y <= Max.Y + margin &&
            p.Z >= Min.Z - margin && p.Z <= Max.Z + margin;

        public static Aabb Around(Vector3 c, float xy, float z) =>
            new(new Vector3(c.X - xy, c.Y - xy, c.Z - z), new Vector3(c.X + xy, c.Y + xy, c.Z + z));
    }
}
