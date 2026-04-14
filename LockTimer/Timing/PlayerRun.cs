using System.Collections.Generic;

namespace LockTimer.Timing;

public sealed class PlayerRun
{
    public RunState State { get; set; } = RunState.Idle;
    public long StartTickMs { get; set; }
    public int NextCheckpointIndex { get; set; }
    public List<CheckpointSplit> Splits { get; } = new();

    public void ResetProgress()
    {
        StartTickMs = 0;
        NextCheckpointIndex = 0;
        Splits.Clear();
    }
}
