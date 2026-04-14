using System.Collections.Generic;
using System.Numerics;
using LockTimer.Timing;
using LockTimer.Zones;
using Xunit;

namespace LockTimer.Tests;

public class TimerEngineTests
{
    private static Zone StartZone() =>
        new(ZoneKind.Start, "m", new(0, 0, 0),    new(100, 100, 100));
    private static Zone EndZone() =>
        new(ZoneKind.End,   "m", new(1000, 0, 0), new(1100, 100, 100));

    private static Zone Cp(float x) =>
        new(ZoneKind.Checkpoint, "m", new(x, 0, 0), new(x + 50, 100, 100));

    private static TimerEngine MakeEngine()
    {
        var e = new TimerEngine();
        e.SetZones(StartZone(), EndZone());
        return e;
    }

    private static TimerEngine MakeEngineWithCheckpoints(params (string name, Zone zone)[] cps)
    {
        var e = new TimerEngine();
        var zones = new List<Zone>();
        var names = new List<string>();
        foreach (var (n, z) in cps) { zones.Add(z); names.Add(n); }
        e.SetZones(StartZone(), EndZone(), zones, names);
        return e;
    }

    [Fact]
    public void Idle_player_far_from_both_zones_stays_idle()
    {
        var e = MakeEngine();
        var f = e.Tick(slot: 0, position: new Vector3(500, 50, 50), nowTickMs: 0);

        Assert.Null(f);
        Assert.Equal(RunState.Idle, e.GetRun(0).State);
    }

    [Fact]
    public void Entering_start_transitions_to_InStart()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50), nowTickMs: 0);

        Assert.Equal(RunState.InStart, e.GetRun(0).State);
    }

    [Fact]
    public void Leaving_start_transitions_to_Running_with_start_tick()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50),   nowTickMs: 1000);
        e.Tick(0, new Vector3(500, 50, 50),  nowTickMs: 1500);

        var run = e.GetRun(0);
        Assert.Equal(RunState.Running, run.State);
        Assert.Equal(1500, run.StartTickMs);
    }

    [Fact]
    public void Entering_end_while_running_emits_finished_with_elapsed()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50),    nowTickMs: 1000);
        e.Tick(0, new Vector3(500, 50, 50),   nowTickMs: 2000); // Running
        var finished = e.Tick(0, new Vector3(1050, 50, 50), nowTickMs: 8000);

        Assert.NotNull(finished);
        Assert.Equal(0, finished!.Value.Slot);
        Assert.Equal(6000, finished.Value.ElapsedMs);
        Assert.Empty(finished.Value.Splits);
        Assert.Equal(RunState.Idle, e.GetRun(0).State);
    }

    [Fact]
    public void Re_entering_start_while_running_resets_to_InStart()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50),   0);
        e.Tick(0, new Vector3(500, 50, 50),  500);  // Running
        e.Tick(0, new Vector3(50, 50, 50),   1000); // back in start

        Assert.Equal(RunState.InStart, e.GetRun(0).State);
    }

    [Fact]
    public void Missing_zones_skip_all_transitions()
    {
        var e = new TimerEngine();
        var f = e.Tick(0, new Vector3(50, 50, 50), 0);

        Assert.Null(f);
        Assert.Equal(RunState.Idle, e.GetRun(0).State);
    }

    [Fact]
    public void ResetAll_returns_every_player_to_idle()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50), 0);
        e.Tick(1, new Vector3(500, 50, 50), 0);

        e.ResetAll();

        Assert.Equal(RunState.Idle, e.GetRun(0).State);
        Assert.Equal(RunState.Idle, e.GetRun(1).State);
    }

    [Fact]
    public void Remove_evicts_player_state()
    {
        var e = MakeEngine();
        e.Tick(0, new Vector3(50, 50, 50), 0);

        e.Remove(0);

        Assert.Equal(RunState.Idle, e.GetRun(0).State);
    }

    [Fact]
    public void Per_player_state_is_isolated()
    {
        var e = MakeEngine();

        e.Tick(0, new Vector3(50, 50, 50),  0);
        e.Tick(1, new Vector3(500, 50, 50), 0);

        Assert.Equal(RunState.InStart, e.GetRun(0).State);
        Assert.Equal(RunState.Idle,    e.GetRun(1).State);
    }

    // --- checkpoint tests ---

    [Fact]
    public void Ordered_checkpoints_record_splits_and_allow_finish()
    {
        var e = MakeEngineWithCheckpoints(
            ("cp1", Cp(300)),
            ("cp2", Cp(600)));

        var hits = new List<CheckpointSplit>();
        void OnCp(int slot, CheckpointSplit s) => hits.Add(s);

        e.Tick(0, new Vector3(50, 50, 50),    1000, OnCp);
        e.Tick(0, new Vector3(200, 50, 50),   2000, OnCp); // Running, startTickMs=2000
        e.Tick(0, new Vector3(320, 50, 50),   3500, OnCp); // cp1
        e.Tick(0, new Vector3(620, 50, 50),   5000, OnCp); // cp2
        var finished = e.Tick(0, new Vector3(1050, 50, 50), 9000, OnCp);

        Assert.Equal(2, hits.Count);
        Assert.Equal("cp1", hits[0].Name);
        Assert.Equal(1500, hits[0].ElapsedMs);
        Assert.Equal("cp2", hits[1].Name);
        Assert.Equal(3000, hits[1].ElapsedMs);

        Assert.NotNull(finished);
        Assert.Equal(7000, finished!.Value.ElapsedMs);
        Assert.Equal(2, finished.Value.Splits.Count);
        Assert.Equal("cp1", finished.Value.Splits[0].Name);
        Assert.Equal("cp2", finished.Value.Splits[1].Name);
    }

    [Fact]
    public void End_zone_without_all_checkpoints_does_not_finish()
    {
        var e = MakeEngineWithCheckpoints(
            ("cp1", Cp(300)),
            ("cp2", Cp(600)));

        e.Tick(0, new Vector3(50, 50, 50),   0);
        e.Tick(0, new Vector3(200, 50, 50),  100); // Running
        var finished = e.Tick(0, new Vector3(1050, 50, 50), 500);

        Assert.Null(finished);
        Assert.Equal(RunState.Running, e.GetRun(0).State);
        Assert.Equal(0, e.GetRun(0).NextCheckpointIndex);
    }

    [Fact]
    public void Out_of_order_checkpoint_is_ignored()
    {
        var e = MakeEngineWithCheckpoints(
            ("cp1", Cp(300)),
            ("cp2", Cp(600)));

        var hits = new List<CheckpointSplit>();
        void OnCp(int slot, CheckpointSplit s) => hits.Add(s);

        e.Tick(0, new Vector3(50, 50, 50),   0, OnCp);
        e.Tick(0, new Vector3(200, 50, 50),  100, OnCp); // Running
        e.Tick(0, new Vector3(620, 50, 50),  500, OnCp); // skip cp1, touch cp2

        Assert.Empty(hits);
        Assert.Equal(0, e.GetRun(0).NextCheckpointIndex);
    }

    [Fact]
    public void Re_entering_start_clears_checkpoint_progress()
    {
        var e = MakeEngineWithCheckpoints(
            ("cp1", Cp(300)),
            ("cp2", Cp(600)));

        e.Tick(0, new Vector3(50, 50, 50),   0);
        e.Tick(0, new Vector3(200, 50, 50),  100); // Running
        e.Tick(0, new Vector3(320, 50, 50),  500); // cp1
        Assert.Equal(1, e.GetRun(0).NextCheckpointIndex);

        e.Tick(0, new Vector3(50, 50, 50), 1000); // back to start
        var run = e.GetRun(0);
        Assert.Equal(RunState.InStart, run.State);
        Assert.Equal(0, run.NextCheckpointIndex);
        Assert.Empty(run.Splits);
    }

    [Fact]
    public void Re_touching_same_checkpoint_does_not_duplicate()
    {
        var e = MakeEngineWithCheckpoints(("cp1", Cp(300)));

        var hits = new List<CheckpointSplit>();
        void OnCp(int slot, CheckpointSplit s) => hits.Add(s);

        e.Tick(0, new Vector3(50, 50, 50),   0, OnCp);
        e.Tick(0, new Vector3(200, 50, 50),  100, OnCp); // Running
        e.Tick(0, new Vector3(320, 50, 50),  500, OnCp); // cp1
        e.Tick(0, new Vector3(320, 50, 50),  600, OnCp); // still in cp1
        e.Tick(0, new Vector3(200, 50, 50),  700, OnCp); // out
        e.Tick(0, new Vector3(320, 50, 50),  800, OnCp); // back in cp1

        Assert.Single(hits);
    }
}
