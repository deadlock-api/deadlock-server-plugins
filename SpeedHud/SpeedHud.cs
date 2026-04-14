using System.Numerics;
using DeadworksManaged.Api;

namespace SpeedHud;

/// <summary>
/// Displays player speed using a CPointWorldText billboard entity parented to the player pawn.
/// The text always faces the camera and updates every tick via SetMessage().
/// </summary>
public sealed class SpeedHud
{
    private readonly HashSet<int> _disabledSlots = new();
    private readonly Dictionary<int, CPointWorldText> _textEntities = new();

    public bool Toggle(int slot, CCitadelPlayerPawn pawn)
    {
        if (_disabledSlots.Add(slot))
        {
            DestroyText(slot);
            return false;
        }
        _disabledSlots.Remove(slot);
        SpawnText(slot, pawn);
        return true;
    }

    public void Remove(int slot)
    {
        _disabledSlots.Remove(slot);
        DestroyText(slot);
    }

    public void Tick(int slot, CCitadelPlayerPawn pawn)
    {
        if (_disabledSlots.Contains(slot)) return;

        if (!_textEntities.TryGetValue(slot, out var wt) || wt.Handle == nint.Zero)
        {
            SpawnText(slot, pawn);
            if (!_textEntities.TryGetValue(slot, out wt)) return;
        }

        var vel = pawn.AbsVelocity;
        float speed = MathF.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
        int rounded = (int)speed;

        wt.SetMessage($"{rounded} u/s");

        if (rounded >= 1000)
            wt.SetColor(255, 60, 60, 255);
        else if (rounded >= 500)
            wt.SetColor(255, 220, 50, 255);
        else
            wt.SetColor(100, 255, 100, 255);
    }

    private void SpawnText(int slot, CCitadelPlayerPawn pawn)
    {
        var pos = pawn.Position + new Vector3(0, 0, 120f);
        var wt = CPointWorldText.Create(
            message: "0 u/s",
            position: pos,
            fontSize: 60f,
            worldUnitsPerPx: 0.12f,
            r: 100, g: 255, b: 100, a: 255,
            fontName: "Reaver",
            reorientMode: 1);

        if (wt is null) return;

        wt.Fullbright = true;
        wt.DepthOffset = 0.1f;
        wt.JustifyHorizontal = HorizontalJustify.Center;
        wt.JustifyVertical = VerticalJustify.Center;
        wt.Teleport(angles: new Vector3(0f, 0f, 90f));
        wt.SetParent(pawn);

        _textEntities[slot] = wt;
    }

    private void DestroyText(int slot)
    {
        if (_textEntities.TryGetValue(slot, out var wt))
        {
            try { wt.Remove(); } catch { }
            _textEntities.Remove(slot);
        }
    }
}
