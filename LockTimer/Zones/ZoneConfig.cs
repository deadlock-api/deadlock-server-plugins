using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace LockTimer.Zones;

/// <summary>
/// Loads map zone definitions from a YAML file. The YAML shape is:
///
///   maps:
///     street_map:
///       start: { min: [0, 0, 0], max: [100, 100, 50] }
///       end:   { min: [500, 500, 0], max: [600, 600, 50] }
///       checkpoints:
///         - { name: cp1, min: [200, 200, 0], max: [250, 250, 50] }
///         - { name: cp2, min: [350, 350, 0], max: [400, 400, 50] }
///
/// Coordinates are in world units (min/max AABB corners). Checkpoints are
/// ordered: runners must touch them in list order before the end zone counts.
/// </summary>
public sealed class ZoneConfig
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public Dictionary<string, MapZones> Maps { get; set; } = new();

    public sealed class MapZones
    {
        public BoxDef? Start { get; set; }
        public BoxDef? End   { get; set; }
        public List<CheckpointDef>? Checkpoints { get; set; }
    }

    public class BoxDef
    {
        public List<float> Min { get; set; } = new();
        public List<float> Max { get; set; } = new();
    }

    public sealed class CheckpointDef : BoxDef
    {
        public string? Name { get; set; }
    }

    public static ZoneConfig LoadFromFile(string path)
    {
        try
        {
            return LoadFromString(File.ReadAllText(path));
        }
        catch (FileNotFoundException)
        {
            return new ZoneConfig();
        }
        catch (DirectoryNotFoundException)
        {
            return new ZoneConfig();
        }
    }

    public static ZoneConfig LoadFromString(string yaml) =>
        Deserializer.Deserialize<ZoneConfig>(yaml) ?? new ZoneConfig();

    public sealed record MapZoneSet(
        Zone? Start,
        Zone? End,
        IReadOnlyList<Zone> Checkpoints,
        IReadOnlyList<string> CheckpointNames);

    /// <summary>Returns start/end/checkpoints for the given map.</summary>
    public MapZoneSet GetForMap(string map)
    {
        if (!Maps.TryGetValue(map, out var def))
            return new MapZoneSet(null, null, Array.Empty<Zone>(), Array.Empty<string>());

        var start = ToZone(def.Start, ZoneKind.Start, map, label: "start");
        var end   = ToZone(def.End,   ZoneKind.End,   map, label: "end");

        var cps      = new List<Zone>();
        var cpNames  = new List<string>();
        if (def.Checkpoints is { Count: > 0 })
        {
            for (int i = 0; i < def.Checkpoints.Count; i++)
            {
                var cp = def.Checkpoints[i];
                var name = string.IsNullOrWhiteSpace(cp.Name) ? $"cp{i + 1}" : cp.Name!;
                cps.Add(ToZone(cp, ZoneKind.Checkpoint, map, label: $"checkpoint[{i}] ({name})")!);
                cpNames.Add(name);
            }
        }

        return new MapZoneSet(start, end, cps, cpNames);
    }

    private static Zone? ToZone(BoxDef? def, ZoneKind kind, string map, string label)
    {
        if (def is null) return null;
        if (def.Min.Count != 3 || def.Max.Count != 3)
            throw new InvalidDataException(
                $"Zone for map '{map}' ({label}) must have 3-element min and max arrays.");
        var min = new Vector3(def.Min[0], def.Min[1], def.Min[2]);
        var max = new Vector3(def.Max[0], def.Max[1], def.Max[2]);
        return Zone.FromCorners(kind, map, min, max);
    }
}
