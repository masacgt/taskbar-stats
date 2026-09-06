using TaskbarStats.Samplers;
using Xunit;

namespace TaskbarStats.Tests;

public class CpuMathTests
{
    [Fact]
    public void CalculatePercent_WhenTotalDidNotAdvance_ReturnsNull()
        => Assert.Null(CpuMath.CalculatePercent(100, 100, 100, 100));

    [Fact]
    public void CalculatePercent_WhenTotalDecreased_ReturnsNull()
        => Assert.Null(CpuMath.CalculatePercent(100, 200, 150, 150));

    [Fact]
    public void CalculatePercent_FullBusy_Returns100()
        => Assert.Equal(100.0, CpuMath.CalculatePercent(0, 1000, 0, 2000)!.Value, precision: 3);

    [Fact]
    public void CalculatePercent_FullIdle_Returns0()
        => Assert.Equal(0.0, CpuMath.CalculatePercent(0, 1000, 1000, 2000)!.Value, precision: 3);

    [Fact]
    public void CalculatePercent_HalfBusy_Returns50()
        => Assert.Equal(50.0, CpuMath.CalculatePercent(0, 1000, 500, 2000)!.Value, precision: 3);

    [Theory]
    [InlineData(100, 1000, 0, 2000)]
    [InlineData(0, 1000, 2500, 2000)]
    public void CalculatePercent_ClampsToValidRange(long prevIdle, long prevTotal, long curIdle, long curTotal)
    {
        double? result = CpuMath.CalculatePercent(prevIdle, prevTotal, curIdle, curTotal);
        Assert.NotNull(result);
        Assert.InRange(result!.Value, 0.0, 100.0);
    }
}
