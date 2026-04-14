using System.Collections.Generic;

namespace LockTimer.Timing;

public readonly record struct CheckpointSplit(string Name, int ElapsedMs);

public readonly record struct FinishedRun(
    int Slot,
    int ElapsedMs,
    IReadOnlyList<CheckpointSplit> Splits);
