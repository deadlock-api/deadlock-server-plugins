using DeadworksManaged.Api;

namespace TrooperInvasion;

public partial class TrooperInvasionPlugin
{
    private void CullAllTroopers()
    {
        // Snapshot the enumeration first — Remove mutates the entity list.
        var victims = Entities.All.Where(e => IsTrooperDesigner(e.DesignerName)).Select(e => e.EntityIndex).ToArray();
        foreach (var idx in victims)
            Timer.Once(1.Ticks(), () => CBaseEntity.FromIndex(idx)?.Remove());
        _aliveEnemyTroopers.Clear();
    }

    private void ScaleTrooperHealth(CBaseEntity ent)
    {
        // m_iHealth doesn't auto-clamp to the new max, so we set both.
        int baseMax = ent.MaxHealth;
        if (baseMax <= 0) return;
        int scaled = (int)(baseMax * WaveTuning.ComputeHealthScale(_roundNum, _waveNum));
        ent.MaxHealth = scaled;
        ent.Health = scaled;
    }

    public override void OnEntitySpawned(EntitySpawnedEvent args)
    {
        // Direct Remove inside OnEntitySpawned AV'd under horde load, so cull via
        // deferred Remove (see 2026-04-22-onentityspawned-remove-deferral.md).
        var ent = args.Entity;
        // Guardians we spawn report DesignerName "npc_trooper_boss" — never cull/scale them.
        if (_guardianIndices.Contains(ent.EntityIndex)) return;
        if (!IsTrooperDesigner(ent.DesignerName)) return;
        int idx = ent.EntityIndex;

        if (ent.TeamNum == HumanTeam)
        {
            Timer.Once(1.Ticks(), () => CBaseEntity.FromIndex(idx)?.Remove());
            return;
        }

        // Strict enemy team — neutral/unassigned troopers are left alone.
        if (ent.TeamNum != EnemyTeam) return;

        int humans = HumanPlayerCount();
        if (humans == 0)
        {
            Timer.Once(1.Ticks(), () => CBaseEntity.FromIndex(idx)?.Remove());
            return;
        }

        _aliveEnemyTroopers.Add(idx);
        ScaleTrooperHealth(ent);
        if (_aliveEnemyTroopers.Count >= WaveTuning.ComputeTrooperCap(humans))
            SetSpawnEnabled(false);
    }

    public override void OnEntityDeleted(EntityDeletedEvent args)
    {
        _aliveEnemyTroopers.Remove(args.Entity.EntityIndex);
    }

    // OnEntityDeleted misses some removal paths (super-trooper promotion, engine
    // end-of-lane despawn), and dying troopers linger as live entities for a tick
    // before deletion, so the set leaks across waves. Sweep gone/dead/non-enemy
    // entries before the cap check in RunWave.
    private void ReconcileAliveTroopers()
    {
        if (_aliveEnemyTroopers.Count == 0) return;
        _aliveEnemyTroopers.RemoveWhere(idx =>
        {
            var ent = CBaseEntity.FromIndex(idx);
            return ent == null
                || !IsTrooperDesigner(ent.DesignerName)
                || ent.TeamNum != EnemyTeam
                || !ent.IsAlive;
        });
    }
}
