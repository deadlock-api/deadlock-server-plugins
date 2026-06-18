using System.Numerics;
using DeadworksManaged.Api;
using LockTimer.Records;
using LockTimer.Timing;

namespace LockTimer.Hud;

/// <summary>
/// Displays the live run timer above the player's head while they are between
/// the start and end zones. Fades out quickly when the run finishes or resets.
/// </summary>
public sealed class TimerHud
{
    private readonly Dictionary<int, CPointWorldText> _textEntities = new();
    private readonly Dictionary<int, long> _fadeStartAt = new();

    private const long FadeDurationMs = 800;

    public void Remove(int slot)
    {
        DestroyText(slot);
        _fadeStartAt.Remove(slot);
    }

    public void Tick(int slot, CCitadelPlayerPawn pawn, PlayerRun run, long nowMs)
    {
        switch (run.State)
        {
            case RunState.Running:
                _fadeStartAt.Remove(slot);
                int elapsedMs = (int)(nowMs - run.StartTickMs);
                if (elapsedMs < 0) elapsedMs = 0;
                var text = TimeFormatter.FormatTime(elapsedMs);
                EnsureText(slot, pawn);
                UpdateText(slot, pawn, text, 255);
                break;

            case RunState.Idle:
            case RunState.InStart:
                // If we were showing a timer, start fading it out
                if (_textEntities.ContainsKey(slot))
                {
                    if (!_fadeStartAt.ContainsKey(slot))
                        _fadeStartAt[slot] = nowMs;

                    long fadeElapsed = nowMs - _fadeStartAt[slot];
                    if (fadeElapsed >= FadeDurationMs)
                    {
                        DestroyText(slot);
                        _fadeStartAt.Remove(slot);
                    }
                    else
                    {
                        byte alpha = (byte)(255 * (1f - (float)fadeElapsed / FadeDurationMs));
                        UpdateText(slot, pawn, null, alpha);
                    }
                }
                break;
        }
    }

    private void EnsureText(int slot, CCitadelPlayerPawn pawn)
    {
        if (_textEntities.TryGetValue(slot, out var existing) && existing.Handle != nint.Zero)
            return;

        var pos = pawn.Position + new Vector3(0, 0, 130f);
        var wt = CPointWorldText.Create(
            message: "0:00:00.000",
            position: pos,
            fontSize: 50f,
            worldUnitsPerPx: 0.10f,
            r: 255, g: 255, b: 255, a: 255,
            fontName: "Reaver",
            reorientMode: 1); // billboard

        if (wt is null) return;

        wt.Fullbright = true;
        wt.DepthOffset = 0.1f;
        wt.JustifyHorizontal = HorizontalJustify.Center;
        wt.JustifyVertical = VerticalJustify.Center;
        wt.Teleport(angles: new Vector3(0f, 0f, 90f)); // roll to read horizontally
        wt.SetParent(pawn);

        _textEntities[slot] = wt;
    }

    private void UpdateText(int slot, CCitadelPlayerPawn pawn, string? text, byte alpha)
    {
        if (!_textEntities.TryGetValue(slot, out var wt) || wt.Handle == nint.Zero) return;

        if (text is not null)
            wt.SetMessage(text);

        wt.SetColor(255, 255, 255, alpha);
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
