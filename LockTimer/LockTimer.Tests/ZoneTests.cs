using System.Numerics;
using LockTimer.Zones;
using Xunit;

namespace LockTimer.Tests;

public class ZoneTests
{
    private static Zone Box(Vector3 min, Vector3 max)
        => new(ZoneKind.Start, "test_map", min, max);

    [Fact]
    public void Contains_point_at_center_is_true()
    {
        var z = Box(new(0, 0, 0), new(100, 100, 100));
        Assert.True(z.Contains(new(50, 50, 50)));
    }

    [Fact]
    public void Contains_point_exactly_on_corner_is_true()
    {
        var z = Box(new(0, 0, 0), new(100, 100, 100));
        Assert.True(z.Contains(new(0, 0, 0)));
        Assert.True(z.Contains(new(100, 100, 100)));
    }

    [Fact]
    public void Contains_point_just_outside_each_axis_is_false()
    {
        var z = Box(new(0, 0, 0), new(100, 100, 100));
        Assert.False(z.Contains(new(-0.01f, 50, 50)));
        Assert.False(z.Contains(new(50, 100.01f, 50)));
        Assert.False(z.Contains(new(50, 50, -0.01f)));
    }

    [Fact]
    public void Contains_handles_negative_coordinates()
    {
        var z = Box(new(-100, -100, -100), new(-50, -50, -50));
        Assert.True(z.Contains(new(-75, -75, -75)));
        Assert.False(z.Contains(new(0, 0, 0)));
    }

    [Fact]
    public void From_two_corners_normalizes_min_max()
    {
        var z = Zone.FromCorners(ZoneKind.End, "m", new(100, 0, 50), new(0, 100, 0));
        Assert.Equal(new Vector3(0, 0, 0), z.Min);
        Assert.Equal(new Vector3(100, 100, 50), z.Max);
    }
}
