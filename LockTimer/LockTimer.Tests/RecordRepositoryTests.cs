using LockTimer.Data;
using LockTimer.Records;
using Xunit;

namespace LockTimer.Tests;

public class RecordRepositoryTests
{
    private static (LockTimerDb db, RecordRepository repo) Make()
    {
        var db = LockTimerDb.OpenInMemory();
        var repo = new RecordRepository(db.Connection);
        return (db, repo);
    }

    [Fact]
    public void First_submission_is_new_pb_with_null_previous()
    {
        var (db, repo) = Make();
        using var _ = db;

        var result = repo.UpsertIfFaster(steamId: 1, map: "m1", timeMs: 10_000,
            playerName: "alice", nowUnix: 100);

        Assert.True(result.Changed);
        Assert.Null(result.PreviousMs);
    }

    [Fact]
    public void Faster_submission_updates_pb_and_reports_previous()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.UpsertIfFaster(1, "m1", 10_000, "alice", 100);
        var result = repo.UpsertIfFaster(1, "m1",  9_000, "alice", 200);

        Assert.True(result.Changed);
        Assert.Equal(10_000, result.PreviousMs);

        var pb = repo.GetPb(1, "m1");
        Assert.NotNull(pb);
        Assert.Equal(9_000, pb!.TimeMs);
    }

    [Fact]
    public void Slower_submission_reports_unchanged_with_previous()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.UpsertIfFaster(1, "m1",  9_000, "alice", 100);
        var result = repo.UpsertIfFaster(1, "m1", 12_000, "alice", 200);

        Assert.False(result.Changed);
        Assert.Equal(9_000, result.PreviousMs);
    }

    [Fact]
    public void Top_returns_fastest_first_across_players()
    {
        var (db, repo) = Make();
        using var _ = db;

        repo.UpsertIfFaster(1, "m1", 10_000, "alice", 100);
        repo.UpsertIfFaster(2, "m1",  8_000, "bob",   100);
        repo.UpsertIfFaster(3, "m1", 15_000, "carol", 100);

        var top = repo.GetTop("m1", limit: 10);
        Assert.Equal(3, top.Count);
        Assert.Equal("bob",   top[0].PlayerName);
        Assert.Equal("alice", top[1].PlayerName);
        Assert.Equal("carol", top[2].PlayerName);
    }

    [Fact]
    public void GetPb_returns_null_when_missing()
    {
        var (db, repo) = Make();
        using var _ = db;

        Assert.Null(repo.GetPb(42, "nowhere"));
    }
}
