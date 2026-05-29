using DeadworksManaged.Api;
using TrooperInvasion.Stats;

namespace TrooperInvasion;

public class TrooperInvasionConfig
{
}

// God-mode-free PvE horde mode. Implementation is split across partials:
//   TrooperInvasion.Waves.cs    — wave scheduler + round cycle
//   TrooperInvasion.Troopers.cs — enemy/friendly trooper spawn handling
//   TrooperInvasion.EndMode.cs  — patron death, victory/defeat, map reset
//   TrooperInvasion.Players.cs  — join/leave, hero pick, spawn ritual
//   TrooperInvasion.Commands.cs — chat commands
// Pure tuning lives in WaveTuning; telemetry in SessionStats.
public partial class TrooperInvasionPlugin : DeadworksPluginBase
{
    public override string Name => "TrooperInvasion";

    private const int HumanTeam = 2;
    private const int EnemyTeam = 3;

    private const string PatronDesigner = "npc_barrack_boss";

    // npc_boss_tier3 = Base Guardian / Shrine, npc_boss_tier2 = Walker.
    private static bool IsGuardianDesigner(string designer) =>
        designer == "npc_boss_tier3" || designer == "npc_boss_tier2";
    private const double GuardianWeakenWindowSeconds = 2.0;
    private DateTime? _humanPatronWeakenAt;
    private DateTime? _enemyPatronWeakenAt;

    private const int StarterGold = 2500;
    private const int CatchUpGoldPerWave = 500;
    // Once per slot: respawn keeps your earned souls; disconnect clears the slot.
    private readonly HashSet<int> _starterGoldSeeded = new();

    private const float FirstWaveGraceSeconds = 10f;
    private const float IntermissionSeconds = 30f;
    // Without a cooldown, _modeOver latches true until last disconnect — survivors
    // would be stuck in a silent server with no waves.
    private const float PostModeCooldownSeconds = 30f;
    private int _roundNum = 1;

    private readonly HashSet<int> _aliveEnemyTroopers = new();
    private static bool IsTrooperDesigner(string designer) =>
        designer == "npc_trooper" || designer == "npc_trooper_boss";

    private IHandle? _pendingWaveTimer;
    private IHandle? _pendingBurstEnd;
    private const int VoteSkipPercent = 20;
    private readonly HashSet<int> _voteSkipSlots = new();
    private bool _wavesActive;
    // Mirrors citadel_trooper_spawn_enabled so redundant writes don't each burn
    // an ExecuteCommand dispatch (the only runtime-safe path — see SetSpawnEnabled).
    private bool _spawnEnabled;

    private int _waveNum;
    private bool _modeOver;

    // Maintained by OnClientFullConnect (+1) / OnClientDisconnect (-1) to avoid
    // Players.GetAll().Count() in the hot OnEntitySpawned path.
    private int _humanCount;

    private readonly SessionStats _stats = new();

    [PluginConfig]
    public TrooperInvasionConfig Config { get; set; } = new();

    public override void OnLoad(bool isReload)
    {
        StatsClient.Configure();
        Console.WriteLine(isReload ? "TrooperInvasion reloaded!" : "TrooperInvasion loaded!");
    }

    public override void OnStartupServer()
    {
        // Applied BEFORE the trooper subsystem starts streaming — runtime
        // mutation of these crashes natively (see ConVar gotcha in SetSpawnEnabled
        // / RunWave). max_per_lane 2048 correlated with AVs under the Remove() storm.
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
        _spawnEnabled = false;
        _waveNum = 0;
        _roundNum = 1;
        _humanCount = 0;
        _pendingWaveTimer?.Cancel(); _pendingWaveTimer = null;
        _pendingBurstEnd?.Cancel(); _pendingBurstEnd = null;
        _aliveEnemyTroopers.Clear();
        _starterGoldSeeded.Clear();
        _voteSkipSlots.Clear();
        _stats.ClearRoundStats();
        _humanPatronWeakenAt = null;
        _enemyPatronWeakenAt = null;
        _stats.Reset();
    }

    private int HumanPlayerCount() => _humanCount;

    public override void OnUnload()
    {
        Console.WriteLine("TrooperInvasion unloaded!");
    }
}
