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

    private static TimerEngine MakeEngine()
    {
        var e = new TimerEngine();
        e.SetZones(StartZone(), EndZone());
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
        Assert.Equal(RunState.Idle, e.GetRun(0).State); // flushed to Idle same tick
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

        // Fresh GetRun creates a new Idle run
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
}
