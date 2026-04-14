using DeadworksManaged.Api;

namespace GhostMode;

public sealed class PlayerIsolation
{
    public void Handle(CheckTransmitEvent args)
    {
        int viewerSlot = args.PlayerSlot;
        for (int i = 0; i < Players.MaxSlot; i++)
        {
            if (i == viewerSlot) continue;
            var controller = Players.FromSlot(i);
            if (controller is null) continue;

            args.Hide(controller);

            var pawn = controller.GetHeroPawn();
            if (pawn is not null) args.Hide(pawn);
        }
    }
}
