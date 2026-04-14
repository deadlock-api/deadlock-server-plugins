using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Data.Sqlite;

namespace LockTimer.Data;

public sealed class LockTimerDb : IDisposable
{
    public SqliteConnection Connection { get; }

    private LockTimerDb(SqliteConnection conn)
    {
        Connection = conn;
    }

    public static LockTimerDb Open(string path)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
        }.ToString();

        var conn = new SqliteConnection(cs);
        conn.Open();
        var db = new LockTimerDb(conn);
        db.ApplyPragmas();
        db.ApplySchema();
        return db;
    }

    public static LockTimerDb OpenInMemory()
    {
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        var db = new LockTimerDb(conn);
        db.ApplySchema();
        return db;
    }

    public void ApplySchema()
    {
        var sql = LoadEmbeddedSql("001_initial.sql");
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void ApplyPragmas()
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode = WAL;";
        cmd.ExecuteNonQuery();
    }

    private static string LoadEmbeddedSql(string name)
    {
        var asm = typeof(LockTimerDb).Assembly;
        var resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded SQL '{name}' not found.");
        using var s = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded SQL stream '{resourceName}' was null.");
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public void Dispose() => Connection.Dispose();
}
