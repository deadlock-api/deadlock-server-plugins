using System.Numerics;
using DeadworksManaged.Api;
using LockTimer.Timing;

namespace LockTimer.Zones;

public sealed class ZoneEditor
{
    private readonly ZoneRepository _zones;
    private readonly TimerEngine _engine;

    private Vector3? _pendingStart1;
    private Vector3? _pendingStart2;
    private Vector3? _pendingEnd1;
    private Vector3? _pendingEnd2;

    public ZoneEditor(ZoneRepository zones, TimerEngine engine)
    {
        _zones  = zones;
        _engine = engine;
    }

    public EditResult CaptureStart1(CCitadelPlayerPawn pawn) => Capture(pawn, ref _pendingStart1, "start p1");
    public EditResult CaptureStart2(CCitadelPlayerPawn pawn) => Capture(pawn, ref _pendingStart2, "start p2");
    public EditResult CaptureEnd1(CCitadelPlayerPawn pawn)   => Capture(pawn, ref _pendingEnd1,   "end p1");
    public EditResult CaptureEnd2(CCitadelPlayerPawn pawn)   => Capture(pawn, ref _pendingEnd2,   "end p2");

    private EditResult Capture(CCitadelPlayerPawn pawn, ref Vector3? slot, string label)
    {
        var eye    = pawn.EyePosition;
        var angles = pawn.ViewAngles;

        var trace = CGameTrace.Create();
        Trace.SimpleTraceAngles(
            eye, angles,
            RayType_t.Line, RnQueryObjectSet.All,
            MaskTrace.Solid, MaskTrace.Empty, MaskTrace.Empty,
            CollisionGroup.Always, ref trace,
            filterEntity: pawn,
            maxDistance: 8192f);

        if (!trace.DidHit)
            return EditResult.Miss($"no surface hit within 8192u for {label}");

        var hit = trace.HitPoint;
        slot = hit;
        return EditResult.Hit($"{label} set at ({hit.X:F1}, {hit.Y:F1}, {hit.Z:F1})");
    }

    public SaveResult SaveZones(string map, long nowUnix)
    {
        var missing = new List<string>();
        if (_pendingStart1 is null) missing.Add("start1");
        if (_pendingStart2 is null) missing.Add("start2");
        if (_pendingEnd1   is null) missing.Add("end1");
        if (_pendingEnd2   is null) missing.Add("end2");
        if (missing.Count > 0)
            return SaveResult.Failure($"need 4 points — missing: {string.Join(", ", missing)}");

        var start = Zone.FromCorners(ZoneKind.Start, map, _pendingStart1!.Value, _pendingStart2!.Value, nowUnix);
        var end   = Zone.FromCorners(ZoneKind.End,   map, _pendingEnd1!.Value,   _pendingEnd2!.Value,   nowUnix);

        if (start.IsZeroVolume) return SaveResult.Failure("start zone has zero volume");
        if (end.IsZeroVolume)   return SaveResult.Failure("end zone has zero volume");

        _zones.Upsert(start);
        _zones.Upsert(end);
        _engine.SetZones(start, end);

        _pendingStart1 = _pendingStart2 = _pendingEnd1 = _pendingEnd2 = null;

        return SaveResult.Success(start, end);
    }

    public void DeleteZones(string map)
    {
        _zones.DeleteForMap(map);
        _engine.SetZones(null, null);
        _engine.ResetAll();
    }

    public void SetPendingStart(Vector3 corner1, Vector3 corner2)
    {
        _pendingStart1 = corner1;
        _pendingStart2 = corner2;
    }

    public void SetPendingEnd(Vector3 corner1, Vector3 corner2)
    {
        _pendingEnd1 = corner1;
        _pendingEnd2 = corner2;
    }

    /// <summary>
    /// Saves a single zone (start or end) from its pending corners.
    /// Returns the zone on success, null if corners are missing or zero-volume.
    /// </summary>
    public Zone? SaveSingleZone(ZoneKind kind, string map, long nowUnix)
    {
        var (c1, c2) = kind == ZoneKind.Start
            ? (_pendingStart1, _pendingStart2)
            : (_pendingEnd1, _pendingEnd2);

        if (c1 is null || c2 is null) return null;

        var zone = Zone.FromCorners(kind, map, c1.Value, c2.Value, nowUnix);
        if (zone.IsZeroVolume) return null;

        _zones.Upsert(zone);
        return zone;
    }

    public PendingStatus GetPendingStatus()
        => new(_pendingStart1.HasValue, _pendingStart2.HasValue, _pendingEnd1.HasValue, _pendingEnd2.HasValue);
}

// Ok is a record-generated property; use Hit/Miss as factory names to avoid CS0102.
public readonly record struct EditResult(bool Ok, string Message)
{
    public static EditResult Hit(string m)  => new(true,  m);
    public static EditResult Miss(string m) => new(false, m);
}

public readonly record struct SaveResult(bool Ok, string Message, Zone? Start, Zone? End)
{
    public static SaveResult Success(Zone s, Zone e) => new(true,  $"zones saved for {s.Map}", s, e);
    public static SaveResult Failure(string m)       => new(false, m, null, null);
}

public readonly record struct PendingStatus(bool Start1, bool Start2, bool End1, bool End2);
