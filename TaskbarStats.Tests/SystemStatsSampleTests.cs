using TaskbarStats.Models;
using Xunit;

namespace TaskbarStats.Tests;

public class SystemStatsSampleTests
{
    [Fact]
    public void RamPercent_CalculatedFromUsedAndTotal()
    {
        var sample = new SystemStatsSample
        {
            Timestamp = DateTime.Now,
            RamTotalBytes = 32L << 30,
            RamAvailableBytes = 8L << 30,
        };

        Assert.Equal(75.0, sample.RamPercent, precision: 5);
    }

    [Fact]
    public void VramPercent_IsNullWhenNoVramData()
    {
        var sample = new SystemStatsSample
        {
            Timestamp = DateTime.Now,
        };

        Assert.Null(sample.VramPercent);
    }

    [Fact]
    public void MaxPercent_TakesLargestMetric()
    {
        var sample = new SystemStatsSample
        {
            Timestamp = DateTime.Now,
            CpuPercent = 40,
            RamTotalBytes = 100,
            RamAvailableBytes = 80,
            GpuPercent = 95,
            VramTotalBytes = 16L << 30,
            VramUsedBytes = 8L << 30,
        };

        Assert.Equal(95.0, sample.MaxPercent, precision: 5);
    }
}
