using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using LockTimer.Data;
using Xunit;

namespace LockTimer.Tests;

public class LockTimerDbTests
{
    [Fact]
    public void Open_in_memory_applies_schema()
    {
        using var db = LockTimerDb.OpenInMemory();

        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
        var tables = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) tables.Add(r.GetString(0));

        Assert.Contains("zones", tables);
        Assert.Contains("records", tables);
    }

    [Fact]
    public void Open_is_idempotent()
    {
        using var db = LockTimerDb.OpenInMemory();
        // Running migration again must not throw
        db.ApplySchema();
    }
}
