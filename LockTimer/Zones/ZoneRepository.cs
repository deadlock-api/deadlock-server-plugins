using System.Collections.Generic;
using System.Numerics;
using Microsoft.Data.Sqlite;

namespace LockTimer.Zones;

public sealed class ZoneRepository
{
    private readonly SqliteConnection _conn;

    public ZoneRepository(SqliteConnection connection)
    {
        _conn = connection;
    }

    public void Upsert(Zone zone)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO zones (map, kind, min_x, min_y, min_z, max_x, max_y, max_z, updated_at)
VALUES (@map, @kind, @minx, @miny, @minz, @maxx, @maxy, @maxz, @ua)
ON CONFLICT(map, kind) DO UPDATE SET
    min_x = excluded.min_x,
    min_y = excluded.min_y,
    min_z = excluded.min_z,
    max_x = excluded.max_x,
    max_y = excluded.max_y,
    max_z = excluded.max_z,
    updated_at = excluded.updated_at;";
        cmd.Parameters.AddWithValue("@map",  zone.Map);
        cmd.Parameters.AddWithValue("@kind", (int)zone.Kind);
        cmd.Parameters.AddWithValue("@minx", zone.Min.X);
        cmd.Parameters.AddWithValue("@miny", zone.Min.Y);
        cmd.Parameters.AddWithValue("@minz", zone.Min.Z);
        cmd.Parameters.AddWithValue("@maxx", zone.Max.X);
        cmd.Parameters.AddWithValue("@maxy", zone.Max.Y);
        cmd.Parameters.AddWithValue("@maxz", zone.Max.Z);
        cmd.Parameters.AddWithValue("@ua",   zone.UpdatedAtUnix);
        cmd.ExecuteNonQuery();
    }

    public List<Zone> GetForMap(string map)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT kind, min_x, min_y, min_z, max_x, max_y, max_z, updated_at
FROM zones
WHERE map = @map
ORDER BY kind;";
        cmd.Parameters.AddWithValue("@map", map);

        var list = new List<Zone>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Zone(
                Kind: (ZoneKind)r.GetInt32(0),
                Map:  map,
                Min:  new Vector3((float)r.GetDouble(1), (float)r.GetDouble(2), (float)r.GetDouble(3)),
                Max:  new Vector3((float)r.GetDouble(4), (float)r.GetDouble(5), (float)r.GetDouble(6)),
                UpdatedAtUnix: r.GetInt64(7)));
        }
        return list;
    }

    public void DeleteForMap(string map)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM zones WHERE map = @map;";
        cmd.Parameters.AddWithValue("@map", map);
        cmd.ExecuteNonQuery();
    }
}
