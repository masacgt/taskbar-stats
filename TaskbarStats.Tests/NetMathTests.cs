using TaskbarStats.Samplers;
using Xunit;

namespace TaskbarStats.Tests;

public class NetMathTests
{
    [Fact]
    public void BytesPerSecToMbits_Converts()
    {
        Assert.Equal(1000.0, NetMath.BytesPerSecToMbits(125_000_000.0), 3);
        Assert.Equal(1.0, NetMath.BytesPerSecToMbits(125_000.0), 3);
    }

    [Fact]
    public void BytesPerSecToMbits_NegativeIsZero()
        => Assert.Equal(0.0, NetMath.BytesPerSecToMbits(-125_000_000.0));

    [Fact]
    public void ClampMbps_RoundsToLong()
    {
        Assert.Equal(940, NetMath.ClampMbps(940.2));
        Assert.Equal(1, NetMath.ClampMbps(0.5));
    }

    [Fact]
    public void ClampMbps_FloorsAtZeroAndCapsAt9999()
    {
        Assert.Equal(0, NetMath.ClampMbps(-5));
        Assert.Equal(9999, NetMath.ClampMbps(10_000_000));
    }
}
