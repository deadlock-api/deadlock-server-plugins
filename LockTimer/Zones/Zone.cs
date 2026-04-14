using System;
using System.Numerics;

namespace LockTimer.Zones;

public sealed record Zone(
    ZoneKind Kind,
    string Map,
    Vector3 Min,
    Vector3 Max)
{
    /// <summary>
    /// Checks if a point is inside the zone, expanded by a margin on all sides.
    /// The margin accounts for the player's collision hull so the zone triggers
    /// when any part of the player overlaps, not just their origin point.
    /// </summary>
    public bool Contains(Vector3 p, float margin = 20f) =>
        p.X >= Min.X - margin && p.X <= Max.X + margin &&
        p.Y >= Min.Y - margin && p.Y <= Max.Y + margin &&
        p.Z >= Min.Z - margin && p.Z <= Max.Z + margin;

    public bool IsZeroVolume =>
        Min.X == Max.X || Min.Y == Max.Y || Min.Z == Max.Z;

    public static Zone FromCorners(ZoneKind kind, string map, Vector3 a, Vector3 b) =>
        new(kind, map,
            Min: new Vector3(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z)),
            Max: new Vector3(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z)));
}
