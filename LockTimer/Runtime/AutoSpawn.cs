using System.Numerics;
using DeadworksManaged.Api;
using LockTimer.Zones;

namespace LockTimer.Runtime;

public sealed class AutoSpawn
{
    private const float DropEpsilon = 32f;

    private readonly Dictionary<int, int> _lastPawnIndex = new();
    private Zone? _startZone;

    public void SetStartZone(Zone? startZone) => _startZone = startZone;

    public void OnDisconnect(int slot) => _lastPawnIndex.Remove(slot);

    public void Tick(CCitadelPlayerController controller, CCitadelPlayerPawn pawn)
    {
        if (_startZone is null) return;

        int slot = controller.EntityIndex - 1;
        int pawnIndex = pawn.EntityIndex;

        bool isNewPawn = !_lastPawnIndex.TryGetValue(slot, out var prev) || prev != pawnIndex;
        _lastPawnIndex[slot] = pawnIndex;

        if (isNewPawn) TeleportToStart(pawn);
    }

    public void ResetRun(CCitadelPlayerController? controller)
    {
        var pawn = controller?.GetHeroPawn();
        if (pawn is not null) TeleportToStart(pawn);
    }

    private void TeleportToStart(CCitadelPlayerPawn pawn)
    {
        if (_startZone is null) return;
        var z = _startZone;
        var center = new Vector3(
            (z.Min.X + z.Max.X) * 0.5f,
            (z.Min.Y + z.Max.Y) * 0.5f,
            z.Max.Z + DropEpsilon);
        pawn.Teleport(position: center, velocity: Vector3.Zero);
    }
}
