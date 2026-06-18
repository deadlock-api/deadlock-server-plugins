using DeadworksManaged.Api;

namespace HeroSelect;

public class HeroSelectPlugin : DeadworksPluginBase
{
    public override string Name => "HeroSelect";

    public override void OnLoad(bool isReload)
    {
        Console.WriteLine($"[{Name}] {(isReload ? "Reloaded" : "Loaded")}");
    }

    [Command("hero", Description = "Swap hero by fuzzy name")]
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
        caller.SelectHero(hero);
        Chat.PrintToChat(caller.Slot, $"[HS] Swapping to {hero.ToDisplayName()}.");
    }

    // Exact → prefix → contains, across display name, enum name, and internal "hero_*" name.
    // Exposed as public so gamemode plugins can reuse the same matcher (e.g. in their own
    // auto-picker logic) instead of carrying a copy.
    public static List<Heroes> FuzzyMatchHero(string query)
    {
        var needle = query.Trim().ToLowerInvariant();
        var available = Enum.GetValues<Heroes>()
            .Where(h => h.GetHeroData()?.AvailableInGame == true)
            .ToArray();

        static string StripPrefix(string s) => s.StartsWith("hero_") ? s[5..] : s;

        var candidates = available
            .Select(h => (
                hero: h,
                display: h.ToDisplayName().ToLowerInvariant(),
                enumN: h.ToString().ToLowerInvariant(),
                internalN: StripPrefix(h.ToHeroName()).ToLowerInvariant()))
            .ToArray();

        bool Any(Func<(Heroes hero, string display, string enumN, string internalN), bool> pred, out List<Heroes> hits)
        {
            hits = candidates.Where(pred).Select(c => c.hero).Distinct().ToList();
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
