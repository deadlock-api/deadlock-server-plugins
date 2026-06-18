using System.Numerics;
using DeadworksManaged.Api;

namespace TrooperInvasion;

// Lane tier1 "Guardians" are authored into dl_midtown — shops, zipline nodes, and
// guard_boss_name wiring all reference boss_{combine,rebel}_t1_{blue,yellow,purple}
// (3 lanes x 2 teams) — but the npc_boss_tier1 entities themselves are NOT in the map's
// entity lump (verified by decompiling default_ents.vents_c with ValveResourceFormat).
// In a real match a matchmaking/objective-init code path spawns them into those named
// slots; an `-insecure -allow_no_lobby_connect` dedicated server never runs it, so no
// Guardians ever appear. Forcing m_eGameState=GameInProgress and game/match mode to
// Normal/Unranked does NOT trigger the spawn (both verified empirically). So we spawn the
// six ourselves with proper KeyValues at the authored lane positions once the match is live.
//
// Gotcha: a spawned npc_boss_tier1 reports its DesignerName as "npc_trooper_boss". Without
// the _guardianIndices exemption in TrooperInvasion.Troopers.cs::OnEntitySpawned, the
// friendly-trooper cull deletes the team-2 ones and the enemy path HP-scales the team-3 ones.
public partial class TrooperInvasionPlugin
{
    // (targetname, team, lane, x, y, z). Positions taken from the guard_boss_name zipline
    // nodes in dl_midtown's entity lump (symmetric per team across the map origin).
    // Lanes: 1=Yellow, 4=Blue, 6=Purple. Teams: 2=Amber/rebels (humans), 3=Sapphire/combine.
    private static readonly (string Name, int Team, int Lane, float X, float Y, float Z)[] GuardianSpecs =
    {
        ("boss_rebel_t1_yellow",   2, 1, -8181f, -1869f, 781f),
        ("boss_rebel_t1_blue",     2, 4,   256f, -1792f, 1024f),
        ("boss_rebel_t1_purple",   2, 6,  7179f, -1919f, 785f),
        ("boss_combine_t1_yellow", 3, 1, -7168f,  1920f, 785f),
        ("boss_combine_t1_blue",   3, 4,  -256f,  1792f, 1024f),
        ("boss_combine_t1_purple", 3, 6,  8192f,  1869f, 781f),
    };

    // Entity indices of Guardians we spawned — exempt from trooper culling/scaling in
    // OnEntitySpawned (they report DesignerName "npc_trooper_boss"). Populated BEFORE
    // Spawn() so the OnEntitySpawned that fires during Spawn() already sees the index.
    internal readonly HashSet<int> _guardianIndices = new();
    private bool _guardiansSpawned;

    private static readonly SchemaAccessor<uint> _gameModeField = new("CCitadelGameRules"u8, "m_eGameMode"u8);
    private static readonly SchemaAccessor<uint> _matchModeField = new("CCitadelGameRules"u8, "m_eMatchMode"u8);

    // Plugin-hosted servers boot with game/match mode = Invalid. The Guardians were validated
    // as working under Normal/Unranked, so normalise both as the match comes up. Convar set is
    // pre-init; the short schema retry catches the value once CCitadelGameRules has networked.
    private void EnsureNormalMatchMode()
    {
        Server.ExecuteCommand("game_mode 1");   // ECitadelGameMode.Normal
        Server.ExecuteCommand("match_mode 1");  // ECitadelMatchMode.Unranked
        var ticks = new int[1];
        var holder = new IHandle[1];
        holder[0] = Timer.Every(1.Ticks(), () =>
        {
            if (GameRules.IsValid)
            {
                _gameModeField.Set(GameRules.Pointer, 1u);
                _matchModeField.Set(GameRules.Pointer, 1u);
            }
            if (++ticks[0] >= 128) holder[0]?.Cancel();   // ~2s @ 64-tick
        });
        holder[0].CancelOnMapChange();
    }

    // Cleared on every map load (OnStartupServer); the level reload wipes the entities.
    private void ResetGuardians()
    {
        _guardiansSpawned = false;
        _guardianIndices.Clear();
    }

    // Spawn the six Guardians once, when waves first arm and the map is fully loaded. Driven
    // from ArmWaves rather than a startup timer because timers don't tick while the server
    // hibernates with no players.
    internal void MaybeSpawnGuardians()
    {
        if (_guardiansSpawned) return;
        if (!GameRules.IsValid || !Entities.All.Any(e => e.DesignerName == "npc_boss_tier2")) return;
        _guardiansSpawned = true;
        Console.WriteLine("[TI] Spawning lane guardians.");
        foreach (var s in GuardianSpecs)
            SpawnGuardian(s.Name, s.Team, s.Lane, new Vector3(s.X, s.Y, s.Z));
    }

    private void SpawnGuardian(string bossName, int team, int lane, Vector3 pos)
    {
        var ekv = new CEntityKeyValues();
        ekv.SetString("targetname", bossName);
        ekv.SetString("BossName", bossName);
        ekv.SetString("subclass_name", "npc_boss_tier1");
        ekv.SetInt("teamnumber", team);
        ekv.SetInt("LaneNum", lane);
        ekv.SetVector("origin", pos);
        ekv.SetVector("angles", Vector3.Zero);

        var g = CBaseEntity.CreateByDesignerName("npc_boss_tier1");
        if (g == null) { Console.WriteLine($"[TI] Guardian '{bossName}' create failed."); return; }
        _guardianIndices.Add(g.EntityIndex);   // mark before Spawn — OnEntitySpawned must skip the cull
        g.Spawn(ekv);
        g.Teleport(pos);
        g.TeamNum = team;
    }
}
