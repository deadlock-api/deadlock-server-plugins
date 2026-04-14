using System;
using System.Collections.Generic;
using System.Numerics;
using LockTimer.Zones;

namespace LockTimer.Timing;

public sealed class TimerEngine
{
    private readonly Dictionary<int, PlayerRun> _runs = new();
    private Zone? _start;
    private Zone? _end;
    private IReadOnlyList<Zone> _checkpoints = Array.Empty<Zone>();
    private IReadOnlyList<string> _checkpointNames = Array.Empty<string>();

    public void SetZones(Zone? start, Zone? end) =>
        SetZones(start, end, Array.Empty<Zone>(), Array.Empty<string>());

    public void SetZones(
        Zone? start,
        Zone? end,
        IReadOnlyList<Zone> checkpoints,
        IReadOnlyList<string> checkpointNames)
    {
        if (checkpoints.Count != checkpointNames.Count)
            throw new ArgumentException("checkpoints and names must have the same length");
        _start = start;
        _end   = end;
        _checkpoints = checkpoints;
        _checkpointNames = checkpointNames;
    }

    public void Remove(int slot) => _runs.Remove(slot);

    public void ResetAll()
    {
        foreach (var run in _runs.Values)
        {
            run.State = RunState.Idle;
            run.ResetProgress();
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

    /// <summary>
    /// Advance state for a player. Returns a FinishedRun only when the end
    /// zone is hit with all checkpoints cleared. When onCheckpoint is
    /// provided, it fires once per checkpoint as it is hit (in order).
    /// </summary>
    public FinishedRun? Tick(
        int slot,
        Vector3 position,
        long nowTickMs,
        Action<int, CheckpointSplit>? onCheckpoint = null)
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
                    run.NextCheckpointIndex = 0;
                    run.Splits.Clear();
                }
                return null;

            case RunState.Running:
                if (inStart)
                {
                    run.State = RunState.InStart;
                    run.ResetProgress();
                    return null;
                }

                if (run.NextCheckpointIndex < _checkpoints.Count &&
                    _checkpoints[run.NextCheckpointIndex].Contains(position))
                {
                    int cpElapsed = ClampElapsed(nowTickMs - run.StartTickMs);
                    var split = new CheckpointSplit(
                        _checkpointNames[run.NextCheckpointIndex],
                        cpElapsed);
                    run.Splits.Add(split);
                    run.NextCheckpointIndex++;
                    onCheckpoint?.Invoke(slot, split);
                    return null;
                }

                if (inEnd && run.NextCheckpointIndex == _checkpoints.Count)
                {
                    int elapsed = ClampElapsed(nowTickMs - run.StartTickMs);
                    var splits = run.Splits.ToArray();
                    run.State = RunState.Idle;
                    run.ResetProgress();
                    return new FinishedRun(slot, elapsed, splits);
                }
                return null;

            default:
                return null;
        }
    }

    private static int ClampElapsed(long ms)
    {
        if (ms < 0) return 0;
        if (ms > int.MaxValue) return int.MaxValue;
        return (int)ms;
    }
}
