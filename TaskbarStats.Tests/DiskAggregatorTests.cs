using TaskbarStats.Samplers;
using Xunit;

namespace TaskbarStats.Tests;

public class DiskAggregatorTests
{
    [Fact]
    public void TryParseDiskNumber_ParsesLeadingDigits()
    {
        Assert.True(DiskAggregator.TryParseDiskNumber("0 C:", out int d0));
        Assert.Equal(0, d0);

        Assert.True(DiskAggregator.TryParseDiskNumber("10 D:", out int d10));
        Assert.Equal(10, d10);
    }

    [Theory]
    [InlineData("C:")]
    [InlineData("")]
    [InlineData("_")]
    public void TryParseDiskNumber_RejectsNonNumeric(string instance)
        => Assert.False(DiskAggregator.TryParseDiskNumber(instance, out _));

    [Fact]
    public void Aggregate_TakesMaxPerDiskAndSortsByDiskNumber()
    {
        double[] result = DiskAggregator.Aggregate(new[]
        {
            ("1 D:", 10.0),
            ("0 C:", 50.0),
            ("1 E:", 80.0),
            ("0 F:", 20.0),
        });

        Assert.Equal(new double[] { 50.0, 80.0 }, result);
    }

    [Fact]
    public void Aggregate_ClampsValuesAndSkipsUnparsable()
    {
        double[] result = DiskAggregator.Aggregate(new[]
        {
            ("0 C:", 150.0),
            ("1 D:", -5.0),
            ("bad", 42.0),
        });

        Assert.Equal(new double[] { 100.0, 0.0 }, result);
    }

    [Fact]
    public void Aggregate_EmptyInputReturnsEmpty()
        => Assert.Empty(DiskAggregator.Aggregate(Array.Empty<(string, double)>()));
}
