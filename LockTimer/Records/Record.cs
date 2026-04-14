namespace LockTimer.Records;

public sealed record Record(
    long SteamId,
    string Map,
    int TimeMs,
    string PlayerName,
    long AchievedAtUnix);
