namespace LockTimer.Records;

public static class TimeFormatter
{
    public static string FormatTime(int ms)
    {
        if (ms < 0) ms = 0;
        int totalSec = ms / 1000;
        int millis   = ms % 1000;
        int hours    = totalSec / 3600;
        int minutes  = (totalSec % 3600) / 60;
        int seconds  = totalSec % 60;
        return $"{hours}:{minutes:D2}:{seconds:D2}.{millis:D3}";
    }
}
