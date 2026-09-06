using TaskbarStats.Models;
using TaskbarStats.UI;
using Xunit;

namespace TaskbarStats.Tests;

public class StatsFormatterTests
{
    [Fact]
    public void BuildLabelOrder_InsertsDisksBeforeNet()
    {
        Assert.Equal(
            new[] { "CPU", "RAM", "GPU", "VRAM", "Disk1", "Disk2", "NET" },
            StatsFormatter.BuildLabelOrder(2));

        Assert.Equal(
            new[] { "CPU", "RAM", "GPU", "VRAM", "NET" },
            StatsFormatter.BuildLabelOrder(0));
    }

    [Theory]
    [InlineData("Disk1", true, 1)]
    [InlineData("Disk12", true, 12)]
    [InlineData("Disk0", false, 0)]
    [InlineData("Disk", false, 0)]
    [InlineData("CPU", false, 0)]
    public void TryGetDiskNumber_ParsesDiskLabels(string label, bool expected, int expectedNumber)
    {
        bool actual = StatsFormatter.TryGetDiskNumber(label, out int number);
        Assert.Equal(expected, actual);
        if (expected)
        {
            Assert.Equal(expectedNumber, number);
        }
    }

    [Theory]
    [InlineData(12.4, " 12%")]
    [InlineData(99.6, "100%")]
    [InlineData(0.0, "  0%")]
    [InlineData(null, " --%")]
    public void FormatPercent_PadsToThreeDigits(double? value, string expected)
        => Assert.Equal(expected, StatsFormatter.FormatPercent(value));

    [Theory]
    [InlineData(940.2, " 940")]
    [InlineData(9999.4, "9999")]
    [InlineData(0.4, "   0")]
    [InlineData(null, "  --")]
    public void FormatMbits_PadsToFourDigits(double? value, string expected)
        => Assert.Equal(expected, StatsFormatter.FormatMbits(value));

    [Fact]
    public void BuildSummary_UsesFixedWidthSegments()
    {
        var sample = new SystemStatsSample
        {
            Timestamp = DateTime.Now,
            CpuPercent = 12.4,
            RamTotalBytes = 32L * 1073741824,
            RamAvailableBytes = 16L * 1073741824,
            GpuPercent = 99.6,
            GpuName = "RTX 3090",
            DiskBusyPercents = new[] { 5.0, 80.0 },
            NetMbps = 940.2,
        };

        string summary = StatsFormatter.BuildSummary(sample, StatsFormatter.BuildLabelOrder(2));

        Assert.Equal(
            "CPU  12%  RAM  50%  GPU 100%  VRAM  --%  Disk1   5%  Disk2  80%  NET  940Mbps",
            summary);
    }

    [Fact]
    public void BuildSummary_HiddenLabelsAreRemoved()
    {
        var sample = new SystemStatsSample
        {
            Timestamp = DateTime.Now,
            CpuPercent = 1.0,
            RamTotalBytes = 8L * 1073741824,
            RamAvailableBytes = 4L * 1073741824,
            GpuPercent = 10.0,
            NetMbps = 50.0,
        };

        string summary = StatsFormatter.BuildSummary(sample, new[] { "CPU", "RAM", "NET" });

        Assert.Equal("CPU   1%  RAM  50%  NET   50Mbps", summary);
    }

    [Fact]
    public void BuildTooltip_ShowsTempAndClockOnlyWhenGpuVisible()
    {
        var sample = new SystemStatsSample
        {
            Timestamp = DateTime.Now,
            CpuPercent = 10.0,
            RamTotalBytes = 32L * 1073741824,
            RamAvailableBytes = 20L * 1073741824,
            GpuPercent = 50.0,
            GpuName = "RTX 3090",
            GpuTempCelsius = 65,
            GpuClockMHz = 1500,
            NetMbps = 100.0,
        };

        string withGpu = StatsFormatter.BuildTooltip(sample, new[] { "CPU", "RAM", "GPU", "VRAM", "NET" });
        Assert.Contains("GPU   50%  (RTX 3090)", withGpu);
        Assert.Contains("65°C / 1500 MHz", withGpu);
        Assert.Contains("NET   100Mbps", withGpu);

        string withoutGpu = StatsFormatter.BuildTooltip(sample, new[] { "CPU", "RAM", "NET" });
        Assert.DoesNotContain("65°C", withoutGpu);
        Assert.DoesNotContain("1500 MHz", withoutGpu);
        Assert.Contains("NET   100Mbps", withoutGpu);
    }

    [Fact]
    public void BuildTooltip_UndetectedGpuShowsMessage()
    {
        var sample = new SystemStatsSample
        {
            Timestamp = DateTime.Now,
            CpuPercent = 10.0,
            RamTotalBytes = 16L * 1073741824,
            RamAvailableBytes = 8L * 1073741824,
        };

        string tooltip = StatsFormatter.BuildTooltip(sample, new[] { "CPU", "GPU" });

        Assert.Contains("GPU   -   (GPUを検出できません)", tooltip);
    }
}
