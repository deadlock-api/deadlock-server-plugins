using LockTimer.Records;
using Xunit;

namespace LockTimer.Tests;

public class TimeFormatterTests
{
    [Theory]
    [InlineData(0,        "0:00:00.000")]
    [InlineData(1,        "0:00:00.001")]
    [InlineData(999,      "0:00:00.999")]
    [InlineData(1_000,    "0:00:01.000")]
    [InlineData(60_000,   "0:01:00.000")]
    [InlineData(83_456,   "0:01:23.456")]
    [InlineData(3_600_000,"1:00:00.000")]
    [InlineData(3_723_456,"1:02:03.456")]
    public void FormatTime_matches_expected(int ms, string expected)
    {
        Assert.Equal(expected, TimeFormatter.FormatTime(ms));
    }
}
