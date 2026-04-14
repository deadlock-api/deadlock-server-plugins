using System.Drawing;
using System.Numerics;
using DeadworksManaged.Api;

namespace LockTimer.Zones;

public sealed class ZoneRenderer
{
    private static int _nameCounter;

    private readonly List<CBaseEntity> _spawned = new();

    public void Render(Zone zone)
    {
        var color = zone.Kind switch
        {
            ZoneKind.Start      => Color.LimeGreen,
            ZoneKind.End        => Color.Red,
            ZoneKind.Checkpoint => Color.DeepSkyBlue,
            _                   => Color.White,
        };
        var corners = GetCorners(zone.Min, zone.Max);
        var edgeIndices = GetEdgeIndices();

        // Try beam approach first
        if (TryRenderBeams(corners, edgeIndices, color))
        {
            Console.WriteLine($"[ZoneRenderer] Rendered {zone.Kind} zone with env_beam entities.");
            return;
        }

        // Fallback to CPointWorldText blocks
        Console.WriteLine("[ZoneRenderer] Beam creation failed, falling back to text blocks.");
        RenderTextBlocks(zone, color);
    }

    private bool TryRenderBeams(Vector3[] corners, (int A, int B)[] edgeIndices, Color color)
    {
        // Create info_target at each corner
        var targets = new CBaseEntity?[corners.Length];
        try
        {
            for (int i = 0; i < corners.Length; i++)
            {
                var target = CBaseEntity.CreateByName("info_target");
                if (target is null)
                {
                    Console.WriteLine("[ZoneRenderer] CreateByName(\"info_target\") returned null.");
                    CleanupPartial(targets);
                    return false;
                }

                var name = $"zt_{_nameCounter++}";
                var ekv = new CEntityKeyValues();
                ekv.SetString("targetname", name);
                target.Spawn(ekv);
                target.Teleport(position: corners[i]);
                targets[i] = target;
                _spawned.Add(target);
            }

            // Create env_beam for each edge
            foreach (var (a, b) in edgeIndices)
            {
                var beam = CBaseEntity.CreateByName("env_beam");
                if (beam is null)
                {
                    Console.WriteLine("[ZoneRenderer] CreateByName(\"env_beam\") returned null.");
                    // Clean up all spawned entities (targets + any beams)
                    ClearAll();
                    return false;
                }

                var nameA = targets[a]!.Name;
                var nameB = targets[b]!.Name;

                var ekv = new CEntityKeyValues();
                ekv.SetString("LightningStart", nameA);
                ekv.SetString("LightningEnd", nameB);
                ekv.SetFloat("BoltWidth", 2.0f);
                ekv.SetFloat("NoiseAmplitude", 0f);
                ekv.SetFloat("life", 0f); // permanent
                ekv.SetColor("rendercolor", color.R, color.G, color.B, color.A);
                ekv.SetInt("renderamt", color.A);
                ekv.SetBool("TouchType", false);

                beam.Spawn(ekv);
                beam.AcceptInput("TurnOn");
                _spawned.Add(beam);
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ZoneRenderer] Beam render failed: {ex.Message}");
            ClearAll();
            return false;
        }
    }

    private void CleanupPartial(CBaseEntity?[] entities)
    {
        foreach (var e in entities)
        {
            if (e is null) continue;
            try { e.Remove(); } catch { }
        }
    }

    // ---- CPointWorldText fallback ----

    private const float SegmentLength = 80f;
    private const float BlockScale = 0.15f;
    private const float CharPixelWidth = 55f;

    private void RenderTextBlocks(Zone zone, Color color)
    {
        float worldPerChar = BlockScale * CharPixelWidth;
        int charsPerSegment = Math.Max(1, (int)(SegmentLength / worldPerChar));
        string segmentText = new string('\u2588', charsPerSegment);

        foreach (var (a, b) in Edges(zone.Min, zone.Max))
        {
            float length = Vector3.Distance(a, b);
            var dir = b - a;
            bool isVertical = MathF.Abs(dir.Z) > MathF.Abs(dir.X) + MathF.Abs(dir.Y);
            float yaw = MathF.Atan2(dir.Y, dir.X) * (180f / MathF.PI);
            var angles = isVertical
                ? new Vector3(90f, yaw, 0f)
                : new Vector3(0f, yaw, 0f);

            float halfSeg = SegmentLength / 2f;
            int segments = Math.Max(1, (int)MathF.Round((length - SegmentLength) / SegmentLength)) + 1;
            for (int i = 0; i < segments; i++)
            {
                float dist = halfSeg + i * ((length - SegmentLength) / Math.Max(1, segments - 1));
                if (segments == 1) dist = length / 2f;
                float t = dist / length;
                var point = Vector3.Lerp(a, b, t);
                SpawnText(segmentText, point, color, BlockScale, angles);
            }
        }
    }

    private void SpawnText(string text, Vector3 position, Color color, float worldUnitsPerPx, Vector3 angles)
    {
        var wt = CPointWorldText.Create(
            message: text,
            position: position,
            fontSize: 100f,
            worldUnitsPerPx: worldUnitsPerPx,
            r: color.R, g: color.G, b: color.B, a: color.A,
            reorientMode: 0);
        if (wt is not null)
        {
            wt.Fullbright = true;
            wt.DepthOffset = 0.1f;
            wt.JustifyHorizontal = HorizontalJustify.Center;
            wt.JustifyVertical = VerticalJustify.Center;
            wt.Teleport(angles: angles);
            _spawned.Add(wt);
        }
    }

    // ---- Shared geometry ----

    public void ClearAll()
    {
        foreach (var e in _spawned)
        {
            try { e.Remove(); } catch { }
        }
        _spawned.Clear();
    }

    private static Vector3[] GetCorners(Vector3 min, Vector3 max) => new[]
    {
        new Vector3(min.X, min.Y, min.Z), // 0: c000
        new Vector3(max.X, min.Y, min.Z), // 1: c100
        new Vector3(min.X, max.Y, min.Z), // 2: c010
        new Vector3(max.X, max.Y, min.Z), // 3: c110
        new Vector3(min.X, min.Y, max.Z), // 4: c001
        new Vector3(max.X, min.Y, max.Z), // 5: c101
        new Vector3(min.X, max.Y, max.Z), // 6: c011
        new Vector3(max.X, max.Y, max.Z), // 7: c111
    };

    private static (int A, int B)[] GetEdgeIndices() => new[]
    {
        // Bottom face
        (0, 1), (1, 3), (3, 2), (2, 0),
        // Top face
        (4, 5), (5, 7), (7, 6), (6, 4),
        // Vertical edges
        (0, 4), (1, 5), (2, 6), (3, 7),
    };

    private static IEnumerable<(Vector3 A, Vector3 B)> Edges(Vector3 min, Vector3 max)
    {
        var c = GetCorners(min, max);
        foreach (var (a, b) in GetEdgeIndices())
            yield return (c[a], c[b]);
    }
}
