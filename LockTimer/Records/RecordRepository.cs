using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace LockTimer.Records;

public readonly record struct UpsertResult(bool Changed, int? PreviousMs);

public sealed class RecordRepository
{
    private readonly SqliteConnection _conn;

    public RecordRepository(SqliteConnection connection)
    {
        _conn = connection;
    }

    public UpsertResult UpsertIfFaster(long steamId, string map, int timeMs, string playerName, long nowUnix)
    {
        using var tx = _conn.BeginTransaction();

        int? previous = null;
        using (var read = _conn.CreateCommand())
        {
            read.Transaction = tx;
            read.CommandText = "SELECT time_ms FROM records WHERE steam_id = @sid AND map = @map;";
            read.Parameters.AddWithValue("@sid", steamId);
            read.Parameters.AddWithValue("@map", map);
            var o = read.ExecuteScalar();
            if (o is long l) previous = (int)l;
        }

        bool changed;
        if (previous is null)
        {
            using var ins = _conn.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = @"
INSERT INTO records (steam_id, map, time_ms, player_name, achieved_at)
VALUES (@sid, @map, @t, @n, @at);";
            ins.Parameters.AddWithValue("@sid", steamId);
            ins.Parameters.AddWithValue("@map", map);
            ins.Parameters.AddWithValue("@t",   timeMs);
            ins.Parameters.AddWithValue("@n",   playerName);
            ins.Parameters.AddWithValue("@at",  nowUnix);
            ins.ExecuteNonQuery();
            changed = true;
        }
        else if (timeMs < previous)
        {
            using var upd = _conn.CreateCommand();
            upd.Transaction = tx;
            upd.CommandText = @"
UPDATE records
SET time_ms = @t, player_name = @n, achieved_at = @at
WHERE steam_id = @sid AND map = @map;";
            upd.Parameters.AddWithValue("@sid", steamId);
            upd.Parameters.AddWithValue("@map", map);
            upd.Parameters.AddWithValue("@t",   timeMs);
            upd.Parameters.AddWithValue("@n",   playerName);
            upd.Parameters.AddWithValue("@at",  nowUnix);
            upd.ExecuteNonQuery();
            changed = true;
        }
        else
        {
            changed = false;
        }

        tx.Commit();
        return new UpsertResult(changed, previous);
    }

    public Record? GetPb(long steamId, string map)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT steam_id, map, time_ms, player_name, achieved_at
FROM records
WHERE steam_id = @sid AND map = @map;";
        cmd.Parameters.AddWithValue("@sid", steamId);
        cmd.Parameters.AddWithValue("@map", map);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Record(
            SteamId: r.GetInt64(0),
            Map:     r.GetString(1),
            TimeMs:  r.GetInt32(2),
            PlayerName:    r.GetString(3),
            AchievedAtUnix: r.GetInt64(4));
    }

    public List<Record> GetTop(string map, int limit)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT steam_id, map, time_ms, player_name, achieved_at
FROM records
WHERE map = @map
ORDER BY time_ms ASC
LIMIT @lim;";
        cmd.Parameters.AddWithValue("@map", map);
        cmd.Parameters.AddWithValue("@lim", limit);

        var list = new List<Record>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new Record(
                SteamId: r.GetInt64(0),
                Map:     r.GetString(1),
                TimeMs:  r.GetInt32(2),
                PlayerName:    r.GetString(3),
                AchievedAtUnix: r.GetInt64(4)));
        }
        return list;
    }
}
