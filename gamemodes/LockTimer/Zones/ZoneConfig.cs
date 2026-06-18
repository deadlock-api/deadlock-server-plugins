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
///
/// Coordinates are in world units (min/max AABB corners).
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
    }

    public sealed class BoxDef
    {
        public List<float> Min { get; set; } = new();
        public List<float> Max { get; set; } = new();
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

    /// <summary>Returns (start, end) zones for the given map, or (null, null) if unknown.</summary>
    public (Zone? Start, Zone? End) GetForMap(string map)
    {
        if (!Maps.TryGetValue(map, out var def))
            return (null, null);

        var start = ToZone(def.Start, ZoneKind.Start, map);
        var end   = ToZone(def.End,   ZoneKind.End,   map);
        return (start, end);
    }

    private static Zone? ToZone(BoxDef? def, ZoneKind kind, string map)
    {
        if (def is null) return null;
        if (def.Min.Count != 3 || def.Max.Count != 3)
            throw new InvalidDataException(
                $"Zone for map '{map}' ({kind}) must have 3-element min and max arrays.");
        var min = new Vector3(def.Min[0], def.Min[1], def.Min[2]);
        var max = new Vector3(def.Max[0], def.Max[1], def.Max[2]);
        return Zone.FromCorners(kind, map, min, max);
    }
}
