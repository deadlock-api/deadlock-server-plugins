using DeadworksManaged.Api;

namespace HeroSelectOnNextSpawn;

public class HeroSelectOnNextSpawnPlugin : DeadworksPluginBase
{
    public override string Name => "HeroSelectOnNextSpawn";

    // Brief post-death window during which the in-game hero picker
    // (selecthero / citadel_hero_pick) is allowed through to the engine.
    // Outside the window, those concommands are intercepted and turned
    // into queued swaps just like !hero.
    private const float HeroSwapWindowSeconds = 10f;
    private readonly Dictionary<int, float> _heroSwapUntil = new();

    // Queued hero set by `!hero <name>` (or intercepted hero-picker). Applied
    // on `player_death` so the engine respawns the player as the chosen hero.
    // SelectHero-while-alive would swap mid-fight.
    private readonly Dictionary<int, Heroes> _pendingHeroSwap = new();

    private const string CmdSelectHero = "selecthero";
    private const string CmdCitadelHeroPick = "citadel_hero_pick";

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}");
    }

    [Command("hero", Description = "Queue a hero swap for your next respawn (fuzzy name match)")]
    public void CmdHero(CCitadelPlayerController caller, params string[] nameParts)
    {
        if (nameParts.Length == 0)
            throw new CommandException("usage: !hero <name>");

        var query = string.Join(' ', nameParts).Trim();
        var matches = FuzzyMatchHero(query);
        if (matches.Count == 0)
            throw new CommandException($"No hero matches '{query}'.");
        if (matches.Count > 1)
        {
            var names = string.Join(", ", matches.Take(6).Select(h => h.ToDisplayName()));
            throw new CommandException($"'{query}' is ambiguous: {names}");
        }

        var hero = matches[0];
        _pendingHeroSwap[caller.EntityIndex] = hero;
        Chat.PrintToChat(caller.Slot, $"[HS] Queued swap to {hero.ToDisplayName()} — applies on next respawn.");
    }

    public override HookResult OnClientConCommand(ClientConCommandEvent e)
    {
        if (e.Command != CmdSelectHero && e.Command != CmdCitadelHeroPick)
            return HookResult.Continue;

        var ctrl = e.Controller;
        if (ctrl != null
            && _heroSwapUntil.TryGetValue(ctrl.EntityIndex, out var until)
            && GlobalVars.CurTime < until)
            return HookResult.Continue;

        // In-game hero picker while alive: queue the swap (same as !hero) so the
        // player gets confirmation and the swap lands on their next respawn,
        // instead of silently dropping the command.
        if (ctrl != null && TryResolveHeroFromCommandArgs(e.Args) is Heroes hero)
        {
            _pendingHeroSwap[ctrl.EntityIndex] = hero;
            Chat.PrintToChat(ctrl.Slot, $"[HS] Queued swap to {hero.ToDisplayName()} — applies on next respawn.");
        }
        return HookResult.Stop;
    }

    [GameEventHandler("player_hero_changed")]
    public HookResult OnPlayerHeroChanged(PlayerHeroChangedEvent args)
    {
        var pawn = args.Userid?.As<CCitadelPlayerPawn>();
        if (pawn?.Controller is { } ctrl)
        {
            _heroSwapUntil.Remove(ctrl.EntityIndex);
            _pendingHeroSwap.Remove(ctrl.EntityIndex);
        }
        return HookResult.Continue;
    }

    [GameEventHandler("player_death")]
    public HookResult OnPlayerDeath(PlayerDeathEvent args)
    {
        // Apply queued swap now, while the player is dead — SelectHero during
        // the dead-state respawn window tells the engine which hero to spawn
        // as. Calling it post-respawn would either swap mid-fight or apply
        // only to the *next* respawn.
        var pawn = args.UseridPawn?.As<CCitadelPlayerPawn>();
        if (pawn?.Controller is { } victimCtrl
            && _pendingHeroSwap.Remove(victimCtrl.EntityIndex, out var queuedHero))
        {
            victimCtrl.SelectHero(queuedHero);
            Console.WriteLine($"[{Name}] Applied queued hero swap for ent#{victimCtrl.EntityIndex}: {queuedHero.ToDisplayName()}");
        }
        return HookResult.Continue;
    }

    [GameEventHandler("player_respawned")]
    public HookResult OnPlayerRespawned(PlayerRespawnedEvent args)
    {
        var pawn = args.Userid?.As<CCitadelPlayerPawn>();
        if (pawn?.Controller is { } ctrl)
            _heroSwapUntil[ctrl.EntityIndex] = GlobalVars.CurTime + HeroSwapWindowSeconds;
        return HookResult.Continue;
    }

    public override void OnClientDisconnect(ClientDisconnectedEvent args)
    {
        var ctrl = args.Controller;
        if (ctrl == null) return;
        _heroSwapUntil.Remove(ctrl.EntityIndex);
        _pendingHeroSwap.Remove(ctrl.EntityIndex);
    }

    private static Heroes? TryResolveHeroFromCommandArgs(string[] args)
    {
        foreach (var a in args)
        {
            if (string.IsNullOrWhiteSpace(a)) continue;
            // `selecthero hero_atlas` — internal name, with or without the `hero_` prefix.
            if (HeroTypeExtensions.TryParse(a, out var byName)) return byName;
            if (HeroTypeExtensions.TryParse("hero_" + a, out var byShort)) return byShort;
            // `citadel_hero_pick <HeroID>` — numeric id; look up the matching Heroes enum.
            if (int.TryParse(a, out var id) && id > 0)
            {
                foreach (var h in Enum.GetValues<Heroes>())
                    if (h.GetHeroData()?.HeroID == id) return h;
            }
        }
        return null;
    }

    // Exact → prefix → contains, across display name, enum name, and internal "hero_*" name.
    // Duplicated from HeroSelectPlugin.FuzzyMatchHero — keeping a local copy avoids a
    // cross-plugin assembly reference for ~30 lines of matching code, and the two plugins
    // never load together anyway (gamemodes.json selects one per mode).
    private static readonly (Heroes hero, string display, string enumN, string internalN)[] _heroCandidates =
        Enum.GetValues<Heroes>()
            .Where(h => h.GetHeroData()?.AvailableInGame == true)
            .Select(h => (
                hero: h,
                display: h.ToDisplayName().ToLowerInvariant(),
                enumN: h.ToString().ToLowerInvariant(),
                internalN: (h.ToHeroName().StartsWith("hero_") ? h.ToHeroName()[5..] : h.ToHeroName()).ToLowerInvariant()))
            .ToArray();

    private static List<Heroes> FuzzyMatchHero(string query)
    {
        var needle = query.Trim().ToLowerInvariant();

        bool Any(Func<(Heroes hero, string display, string enumN, string internalN), bool> pred, out List<Heroes> hits)
        {
            hits = _heroCandidates.Where(pred).Select(c => c.hero).Distinct().ToList();
            return hits.Count > 0;
        }

        if (Any(c => c.display == needle || c.enumN == needle || c.internalN == needle, out var exact))
            return exact;
        if (Any(c => c.display.StartsWith(needle) || c.enumN.StartsWith(needle) || c.internalN.StartsWith(needle), out var prefix))
            return prefix;
        if (Any(c => c.display.Contains(needle) || c.enumN.Contains(needle) || c.internalN.Contains(needle), out var contains))
            return contains;
        return new List<Heroes>();
    }

    public override void OnUnload()
    {
        Console.WriteLine($"[{Name}] Unloaded");
    }
}
