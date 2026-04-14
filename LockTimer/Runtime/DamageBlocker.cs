using DeadworksManaged.Api;

namespace LockTimer.Runtime;

public sealed class DamageBlocker
{
    public HookResult Handle(TakeDamageEvent args)
    {
        var target = args.Entity;
        var attacker = args.Info.Attacker;
        if (target is null || attacker is null) return HookResult.Continue;
        if (target.EntityIndex == attacker.EntityIndex) return HookResult.Continue;

        int targetSlot = -1, attackerSlot = -1;
        foreach (var controller in Players.GetAll())
        {
            var pawn = controller.GetHeroPawn();
            if (pawn is null) continue;
            int idx = pawn.EntityIndex;
            if (idx == target.EntityIndex) targetSlot = controller.EntityIndex - 1;
            else if (idx == attacker.EntityIndex) attackerSlot = controller.EntityIndex - 1;
            if (targetSlot >= 0 && attackerSlot >= 0) break;
        }

        if (targetSlot < 0 || attackerSlot < 0) return HookResult.Continue;
        return targetSlot == attackerSlot ? HookResult.Continue : HookResult.Stop;
    }
}
