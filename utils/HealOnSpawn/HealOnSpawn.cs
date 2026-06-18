using DeadworksManaged.Api;

namespace HealOnSpawn;

public class HealOnSpawnPlugin : DeadworksPluginBase
{
    public override string Name => "HealOnSpawn";

    // GetMaxHealth() returns 0 for several ticks after player_respawned /
    // player_hero_changed because stats/modifiers haven't settled — writing Health
    // immediately leaves the player at 0 HP. Poll max up to MaxAttempts ticks.
    private const int MaxAttempts = 20;

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}");
    }

    [GameEventHandler("player_respawned")]
    public HookResult OnPlayerRespawned(PlayerRespawnedEvent args)
    {
        HealToFull(args.Userid?.As<CCitadelPlayerPawn>());
        return HookResult.Continue;
    }

    [GameEventHandler("player_hero_changed")]
    public HookResult OnPlayerHeroChanged(PlayerHeroChangedEvent args)
    {
        HealToFull(args.Userid?.As<CCitadelPlayerPawn>());
        return HookResult.Continue;
    }

    private void HealToFull(CCitadelPlayerPawn? pawn)
    {
        if (pawn == null) return;
        TryHeal(pawn.EntityIndex, 0);
    }

    // Re-resolve the pawn via EntityIndex inside each tick — handles become stale across ticks.
    private void TryHeal(int idx, int attempt)
    {
        var pawn = CBaseEntity.FromIndex<CCitadelPlayerPawn>(idx);
        if (pawn == null) return;
        int max = pawn.GetMaxHealth();
        if (max > 0)
        {
            pawn.Health = max;
            return;
        }
        if (attempt >= MaxAttempts) return;
        Timer.Once(1.Ticks(), () => TryHeal(idx, attempt + 1));
    }

    public override void OnUnload()
    {
        Console.WriteLine($"[{Name}] Unloaded");
    }
}
