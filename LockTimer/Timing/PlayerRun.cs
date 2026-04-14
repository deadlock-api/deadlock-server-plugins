namespace LockTimer.Timing;

public sealed class PlayerRun
{
    public RunState State { get; set; } = RunState.Idle;
    public long StartTickMs { get; set; }
}
