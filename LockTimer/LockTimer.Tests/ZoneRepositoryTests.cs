using System.Numerics;
using LockTimer.Data;
using LockTimer.Zones;
using Xunit;

namespace LockTimer.Tests;

public class ZoneRepositoryTests
{
    private static (LockTimerDb db, ZoneRepository repo) Make()
    {
        var db = LockTimerDb.OpenInMemory();
        var repo = new ZoneRepository(db.Connection);
        return (db, repo);
    }

    [Fact]
    public void Upsert_inserts_new_zone()
    {
        var (db, repo) = Make();
        using var _ = db;

        var z = new Zone(ZoneKind.Start, "m1", new(0, 0, 0), new(1, 1, 1), UpdatedAtUnix: 100);
        repo.Upsert(z);

        var loaded = repo.GetForMap("m1");
        Assert.Single(loaded);
        Assert.Equal(ZoneKind.Start, loaded[0].Kind);
        Assert.Equal(new Vector3(1, 1, 1), loaded[0].Max);
    }

    [Fact]
    public void Upsert_replaces_existing_zone_of_same_kind()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.Upsert(new Zone(ZoneKind.Start, "m1", new(0, 0, 0), new(1, 1, 1), 100));
        repo.Upsert(new Zone(ZoneKind.Start, "m1", new(5, 5, 5), new(6, 6, 6), 200));

        var loaded = repo.GetForMap("m1");
        Assert.Single(loaded);
        Assert.Equal(new Vector3(5, 5, 5), loaded[0].Min);
        Assert.Equal(200, loaded[0].UpdatedAtUnix);
    }

    [Fact]
    public void GetForMap_isolates_by_map()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.Upsert(new Zone(ZoneKind.Start, "m1", new(0, 0, 0), new(1, 1, 1), 100));
        repo.Upsert(new Zone(ZoneKind.End, "m2", new(0, 0, 0), new(1, 1, 1), 100));

        Assert.Single(repo.GetForMap("m1"));
        Assert.Single(repo.GetForMap("m2"));
        Assert.Empty(repo.GetForMap("m3"));
    }

    [Fact]
    public void Delete_removes_all_zones_for_map()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.Upsert(new Zone(ZoneKind.Start, "m1", new(0, 0, 0), new(1, 1, 1), 100));
        repo.Upsert(new Zone(ZoneKind.End,   "m1", new(2, 2, 2), new(3, 3, 3), 100));
        repo.DeleteForMap("m1");

        Assert.Empty(repo.GetForMap("m1"));
    }
}
