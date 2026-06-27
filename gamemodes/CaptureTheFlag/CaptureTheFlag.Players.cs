using System.Numerics;
using DeadworksManaged.Api;

namespace CaptureTheFlag;

public partial class CaptureTheFlagPlugin
{
    // AFK kick: per-slot last position + the time they were last seen moving.
    private readonly Dictionary<int, (Vector3 Pos, float Since)> _afk = new();
    private const float AfkMoveEpsilonSq = 25f * 25f; // >25 units of movement counts as active

    private void CheckAfk(float now)
    {
        if (Config.AfkKickSeconds <= 0f) return;
        foreach (var ctrl in Players.GetAll())
        {
            int slot = ctrl.Slot;
            if (ctrl.IsBot) { _afk.Remove(slot); continue; }
            var pawn = ctrl.GetHeroPawn()?.As<CCitadelPlayerPawn>();
            if (pawn == null) { _afk.Remove(slot); continue; }

            var pos = pawn.Position;
            if (!_afk.TryGetValue(slot, out var rec) || Vector3.DistanceSquared(pos, rec.Pos) > AfkMoveEpsilonSq)
            {
                _afk[slot] = (pos, now); // moved (or first sample) — reset the AFK timer
                continue;
            }
            if (now - rec.Since >= Config.AfkKickSeconds)
            {
                Chat.PrintToChatAll($"[CTF] Kicked {ctrl.PlayerName} (AFK).");
                Server.ClientCommand(slot, "disconnect");
                _afk.Remove(slot);
            }
        }
    }

    public override void OnClientFullConnect(ClientFullConnectEvent args)
    {
        var controller = args.Controller;
        if (controller == null) return;

        // Server-side balance: send the joiner to the playable team with fewer players.
        // Bypasses the engine team-picker; TeamChangeBlock stops client re-teaming.
        int amber = 0, sapphire = 0;
        foreach (var p in Players.GetAll())
        {
            if (p.EntityIndex == controller.EntityIndex) continue;
            if (p.TeamNum == Amber) amber++;
            else if (p.TeamNum == Sapphire) sapphire++;
        }
        int team = amber <= sapphire ? Amber : Sapphire;
        controller.ChangeTeam(team);

        // Assign a random in-game hero so the player spawns properly. Without this the engine
        // leaves them hero-less and drops them under the map until they pick one manually.
        var available = Enum.GetValues<Heroes>()
            .Where(h => h.GetHeroData()?.AvailableInGame == true)
            .ToArray();
        string heroName = "(none)";
        if (available.Length > 0)
        {
            var hero = available[Random.Shared.Next(available.Length)];
            controller.SelectHero(hero);
            heroName = hero.ToHeroName();
        }

        _humanCount++;
        ArmWarmupIfPopulated();
        Console.WriteLine($"[CTF] Slot {args.Slot} -> {TeamName(team)} (amber={amber} sapphire={sapphire}), hero {heroName}");
    }

    public override void OnClientDisconnect(ClientDisconnectedEvent args)
    {
        var controller = args.Controller;
        if (controller == null) return;

        bool wasPlayable = controller.TeamNum == Amber || controller.TeamNum == Sapphire;
        if (wasPlayable && _humanCount > 0) _humanCount--;

        // Carrier left: reset straight to center (no body to drop at).
        if (IsCarried && controller.Slot == _carrierSlot)
        {
            ResetFlagToCenter(announce: false);
            Chat.PrintToChatAll("[CTF] Carrier left — urn reset.");
        }

        // Server emptied: rewind to a fresh warmup so the next arrivals start clean.
        if (_humanCount == 0)
            ResetMatch();
    }

    [GameEventHandler("player_death")]
    public HookResult OnPlayerDeath(PlayerDeathEvent args)
    {
        var victim = args.UseridPawn;
        if (victim != null && IsCarried && victim.EntityIndex == _carrierEnt)
            // Drop at the carrier's actual position (tracked every tick); the event's victim
            // coords can come through as ~origin, which dumped the urn at map center.
            DropFlag(victim.Position != Vector3.Zero ? victim.Position : _flagPos);
        return HookResult.Continue;
    }

    // Per-tick mask hook (return-less). The carrier cannot fire their weapon, forcing
    // teammates to escort. Abilities/items stay live. Masks reset every tick, so this
    // must re-apply each tick the carrier holds the flag — no cleanup needed on drop.
    public override void OnAbilityAttempt(AbilityAttemptEvent args)
    {
        if (Config.CarrierDisableFire && IsCarried && args.PlayerSlot == _carrierSlot)
            args.Block(InputButton.Attack | InputButton.Attack2);
    }
}
