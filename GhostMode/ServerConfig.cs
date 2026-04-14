using DeadworksManaged.Api;

namespace GhostMode;

public static class ServerConfig
{
    public static void Apply()
    {
        SetInt("citadel_trooper_spawn_enabled", 0);
        SetInt("citadel_npc_spawn_enabled", 0);
        SetInt("citadel_allow_duplicate_heroes", 1);
        SetInt("citadel_voice_all_talk", 1);
        SetInt("citadel_player_spawn_time_max_respawn_time", 5);
        SetInt("citadel_bots_enabled", 0);
    }

    private static void SetInt(string name, int value)
    {
        var cv = ConVar.Find(name);
        if (cv is null || !cv.IsValid)
        {
            Console.WriteLine($"[GhostMode] convar not found: {name}");
            return;
        }
        cv.SetInt(value);
    }
}
