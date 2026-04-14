using System.Collections.Generic;
using System.Numerics;
using LockTimer.Zones;

namespace LockTimer.Timing;

public sealed class TimerEngine
{
    private readonly Dictionary<int, PlayerRun> _runs = new();
    private Zone? _start;
    private Zone? _end;

    public void SetZones(Zone? start, Zone? end)
    {
        _start = start;
        _end   = end;
    }

    public void Remove(int slot) => _runs.Remove(slot);

    public void ResetAll()
    {
        foreach (var run in _runs.Values)
        {
            run.State       = RunState.Idle;
            run.StartTickMs = 0;
        }
    }

    public PlayerRun GetRun(int slot)
    {
        if (!_runs.TryGetValue(slot, out var run))
        {
            run = new PlayerRun();
            _runs[slot] = run;
        }
        return run;
    }

    public FinishedRun? Tick(int slot, Vector3 position, long nowTickMs)
    {
        if (_start is null || _end is null) return null;

        var run    = GetRun(slot);
        bool inStart = _start.Contains(position);
        bool inEnd   = _end.Contains(position);

        switch (run.State)
        {
            case RunState.Idle:
                if (inStart) run.State = RunState.InStart;
                return null;

            case RunState.InStart:
                if (!inStart)
                {
                    run.State       = RunState.Running;
                    run.StartTickMs = nowTickMs;
                }
                return null;

            case RunState.Running:
                if (inStart)
                {
                    run.State       = RunState.InStart;
                    run.StartTickMs = 0;
                    return null;
                }
                if (inEnd)
                {
                    long elapsed = nowTickMs - run.StartTickMs;
                    if (elapsed < 0) elapsed = 0;
                    if (elapsed > int.MaxValue) elapsed = int.MaxValue;
                    run.State       = RunState.Idle;
                    run.StartTickMs = 0;
                    return new FinishedRun(slot, (int)elapsed);
                }
                return null;

            case RunState.Finished:
                run.State = RunState.Idle;
                return null;

            default:
                return null;
        }
    }
}
