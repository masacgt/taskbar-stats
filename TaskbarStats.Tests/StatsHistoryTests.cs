using TaskbarStats.Models;
using TaskbarStats.UI;
using Xunit;

namespace TaskbarStats.Tests;

public class StatsHistoryTests
{
    private static SystemStatsSample CreateSample(double cpu) => new()
    {
        Timestamp = DateTime.Now,
        CpuPercent = cpu,
        RamTotalBytes = 32L * 1024 * 1024 * 1024,
        RamAvailableBytes = 16L * 1024 * 1024 * 1024,
    };

    [Fact]
    public void Add_WhenUnderCapacity_KeepsAll()
    {
        var history = new StatsHistory(10);
        for (int i = 0; i < 5; i++)
        {
            history.Add(CreateSample(i));
        }

        Assert.Equal(5, history.Count);
        Assert.Equal(5, history.Snapshot().Count);
    }

    [Fact]
    public void Add_WhenOverCapacity_DropsOldest()
    {
        var history = new StatsHistory(3);
        for (int i = 0; i < 5; i++)
        {
            history.Add(CreateSample(i));
        }

        Assert.Equal(3, history.Count);
        IReadOnlyList<SystemStatsSample> snapshot = history.Snapshot();
        Assert.Equal(2.0, snapshot[0].CpuPercent);
        Assert.Equal(4.0, snapshot[^1].CpuPercent);
    }

    [Fact]
    public void Snapshot_ReturnsChronologicalOrder()
    {
        var history = new StatsHistory(100);
        history.Add(CreateSample(1));
        history.Add(CreateSample(2));
        history.Add(CreateSample(3));

        double[] values = history.Snapshot().Select(s => s.CpuPercent).ToArray();
        Assert.Equal(new[] { 1.0, 2.0, 3.0 }, values);
    }
}
