using System.Drawing;
using System.Numerics;
using DeadworksManaged.Api;

namespace LockTimer.Zones;

/// <summary>
/// Interactive zone creation where players aim and shoot to place corners.
/// Phase 1: A cursor X follows the crosshair. Shooting locks corner 1.
/// Phase 2: Corner 1 is locked, a wireframe box preview (env_beam edges)
///          updates dynamically as the player looks around. Shooting locks
///          corner 2 and completes the zone.
/// </summary>
public sealed class InteractiveEditor
{
    private readonly ZoneEditor _zoneEditor;
    private readonly Dictionary<int, EditSession> _sessions = new();
    private readonly Dictionary<int, Vector3> _cachedAim = new();
    private static int _nameCounter;

    /// <summary>Distance in units from the eye to place the aim marker (~7.5 meters).</summary>
    private const float AimProjectionDist = 300f;

    public InteractiveEditor(ZoneEditor zoneEditor)
    {
        _zoneEditor = zoneEditor;
    }

    public bool IsEditing(int slot) => _sessions.ContainsKey(slot);

    public void Begin(int slot, ZoneKind kind)
    {
        Cancel(slot);
        _sessions[slot] = new EditSession { Kind = kind };
    }

    public void Cancel(int slot)
    {
        if (_sessions.Remove(slot, out var session))
            session.Cleanup();
        _cachedAim.Remove(slot);
    }

    /// <summary>
    /// Projects forward from the player's eye along their view direction and
    /// caches the position. Must be called from the game thread (OnAbilityAttempt).
    /// </summary>
    public void UpdateAim(int slot, CCitadelPlayerPawn pawn)
    {
        var eye = pawn.EyePosition;
        var angles = pawn.ViewAngles;

        float pitch = angles.X * MathF.PI / 180f;
        float yaw   = angles.Y * MathF.PI / 180f;
        var forward = new Vector3(
            MathF.Cos(pitch) * MathF.Cos(yaw),
            MathF.Cos(pitch) * MathF.Sin(yaw),
            -MathF.Sin(pitch));

        _cachedAim[slot] = eye + forward * AimProjectionDist;
    }

    /// <summary>Updates visual indicators using the cached aim position. Called from the timer tick.</summary>
    public void Tick(int slot)
    {
        if (!_sessions.TryGetValue(slot, out var session)) return;
        if (!_cachedAim.TryGetValue(slot, out var aimPos)) return;

        // Cursor marker: recreate each tick (CPointWorldText can't be teleported)
        TryRemoveText(session.CursorDot);
        session.CursorDot = CreateMarker(aimPos, Color.FromArgb(255, 255, 220, 50), "."); // yellow dot

        if (session.Phase == EditPhase.PlacingCorner2)
            RecreatePreviewWireframe(session, aimPos);
    }

    /// <summary>
    /// Called when the player presses attack. Uses the cached aim position
    /// to place the corner. Advances the editing phase.
    /// </summary>
    public ConfirmOutcome Confirm(int slot)
    {
        if (!_sessions.TryGetValue(slot, out var session))
            return ConfirmOutcome.Ignored;

        if (!_cachedAim.TryGetValue(slot, out var hit))
            return ConfirmOutcome.Ignored;

        if (session.Phase == EditPhase.PlacingCorner1)
        {
            session.Corner1 = hit;
            session.Phase = EditPhase.PlacingCorner2;

            // Lock corner 1 marker in place
            var c1Color = session.Kind == ZoneKind.Start ? Color.LimeGreen : Color.Red;
            session.Corner1Dot = CreateMarker(hit, c1Color, ".");

            return ConfirmOutcome.Corner1Set;
        }

        // PlacingCorner2 — finalize the zone
        var corner1 = session.Corner1!.Value;
        var corner2 = hit;
        var kind = session.Kind;

        if (kind == ZoneKind.Start)
            _zoneEditor.SetPendingStart(corner1, corner2);
        else
            _zoneEditor.SetPendingEnd(corner1, corner2);

        Cancel(slot);
        return kind == ZoneKind.Start
            ? ConfirmOutcome.StartZoneReady
            : ConfirmOutcome.EndZoneReady;
    }

    public void Remove(int slot) => Cancel(slot);

    // --- Preview wireframe (info_target + env_beam) ---
    // env_beam caches endpoint positions at spawn, so we must destroy and
    // recreate the entire wireframe each tick to keep it in sync.

    private static void RecreatePreviewWireframe(EditSession session, Vector3 cursorPos)
    {
        if (session.Corner1 is null) return;

        // Tear down previous frame's entities
        foreach (var ent in session.PreviewBeams)
            TryRemoveEntity(ent);
        foreach (var ent in session.PreviewTargets)
            TryRemoveEntity(ent);
        session.PreviewBeams.Clear();
        session.PreviewTargets.Clear();

        // Compute AABB from the two corners
        var c1 = session.Corner1.Value;
        var min = new Vector3(
            MathF.Min(c1.X, cursorPos.X),
            MathF.Min(c1.Y, cursorPos.Y),
            MathF.Min(c1.Z, cursorPos.Z));
        var max = new Vector3(
            MathF.Max(c1.X, cursorPos.X),
            MathF.Max(c1.Y, cursorPos.Y),
            MathF.Max(c1.Z, cursorPos.Z));

        var corners = GetCorners(min, max);
        var color = session.Kind == ZoneKind.Start ? Color.LimeGreen : Color.Red;

        // Spawn 8 info_targets at the AABB corners
        for (int i = 0; i < 8; i++)
        {
            var target = CBaseEntity.CreateByName("info_target");
            if (target is null) return;

            var name = $"zp_{_nameCounter++}";
            var ekv = new CEntityKeyValues();
            ekv.SetString("targetname", name);
            target.Spawn(ekv);
            target.Teleport(position: corners[i]);
            session.PreviewTargets.Add(target);
        }

        // Spawn 12 env_beams for the box edges
        foreach (var (a, b) in GetEdgeIndices())
        {
            var beam = CBaseEntity.CreateByName("env_beam");
            if (beam is null) return;

            var ekv = new CEntityKeyValues();
            ekv.SetString("LightningStart", session.PreviewTargets[a].Name);
            ekv.SetString("LightningEnd", session.PreviewTargets[b].Name);
            ekv.SetFloat("BoltWidth", 2.0f);
            ekv.SetFloat("NoiseAmplitude", 0f);
            ekv.SetFloat("life", 0f);
            ekv.SetColor("rendercolor", color.R, color.G, color.B, color.A);
            ekv.SetInt("renderamt", color.A);
            ekv.SetBool("TouchType", false);

            beam.Spawn(ekv);
            beam.AcceptInput("TurnOn");
            session.PreviewBeams.Add(beam);
        }
    }

    // --- Text markers ---

    private static CPointWorldText? CreateMarker(Vector3 pos, Color color, string text = "X")
    {
        var wt = CPointWorldText.Create(
            message: text,
            position: pos,
            fontSize: 160f,
            worldUnitsPerPx: 0.30f,
            r: color.R, g: color.G, b: color.B, a: color.A,
            fontName: "Reaver",
            reorientMode: 1);
        if (wt is null) return null;
        wt.Fullbright = true;
        wt.DepthOffset = 0.1f;
        wt.JustifyHorizontal = HorizontalJustify.Center;
        wt.JustifyVertical = VerticalJustify.Center;
        return wt;
    }

    internal static void TryRemoveText(CPointWorldText? wt)
    {
        if (wt is null) return;
        try { wt.Remove(); } catch { }
    }

    private static void TryRemoveEntity(CBaseEntity? ent)
    {
        if (ent is null) return;
        try { ent.Remove(); } catch { }
    }

    // --- Shared geometry ---

    private static Vector3[] GetCorners(Vector3 min, Vector3 max) => new[]
    {
        new Vector3(min.X, min.Y, min.Z),
        new Vector3(max.X, min.Y, min.Z),
        new Vector3(min.X, max.Y, min.Z),
        new Vector3(max.X, max.Y, min.Z),
        new Vector3(min.X, min.Y, max.Z),
        new Vector3(max.X, min.Y, max.Z),
        new Vector3(min.X, max.Y, max.Z),
        new Vector3(max.X, max.Y, max.Z),
    };

    private static (int A, int B)[] GetEdgeIndices() => new[]
    {
        (0, 1), (1, 3), (3, 2), (2, 0), // bottom face
        (4, 5), (5, 7), (7, 6), (6, 4), // top face
        (0, 4), (1, 5), (2, 6), (3, 7), // vertical edges
    };

    // --- Inner types ---

    private enum EditPhase { PlacingCorner1, PlacingCorner2 }

    private sealed class EditSession
    {
        public ZoneKind Kind;
        public EditPhase Phase = EditPhase.PlacingCorner1;
        public Vector3? Corner1;
        public CPointWorldText? CursorDot;
        public CPointWorldText? Corner1Dot;
        public readonly List<CBaseEntity> PreviewTargets = new(); // 8 info_targets
        public readonly List<CBaseEntity> PreviewBeams = new();   // 12 env_beams

        public void Cleanup()
        {
            TryRemoveText(CursorDot);
            TryRemoveText(Corner1Dot);
            foreach (var ent in PreviewBeams)
                TryRemoveEntity(ent);
            foreach (var ent in PreviewTargets)
                TryRemoveEntity(ent);
            PreviewBeams.Clear();
            PreviewTargets.Clear();
            CursorDot = null;
            Corner1Dot = null;
        }
    }
}

public enum ConfirmOutcome { Ignored, Corner1Set, StartZoneReady, EndZoneReady }
