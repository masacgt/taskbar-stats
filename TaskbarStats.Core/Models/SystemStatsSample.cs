namespace TaskbarStats.Models;

public sealed record SystemStatsSample
{
    public required DateTime Timestamp { get; init; }

    public double CpuPercent { get; init; }

    public long RamTotalBytes { get; init; }

    public long RamAvailableBytes { get; init; }

    public double? GpuPercent { get; init; }

    public long? VramTotalBytes { get; init; }

    public long? VramUsedBytes { get; init; }

    public string? GpuName { get; init; }

    public string? GpuVendor { get; init; }

    public int? GpuTempCelsius { get; init; }

    public int? GpuClockMHz { get; init; }

    public double[]? DiskBusyPercents { get; init; }

    public double? NetMbps { get; init; }

    public long RamUsedBytes => Math.Max(0, RamTotalBytes - RamAvailableBytes);

    public double RamPercent => RamTotalBytes > 0 ? (double)RamUsedBytes / RamTotalBytes * 100.0 : 0.0;

    public double? VramPercent =>
        VramTotalBytes is long total && total > 0 && VramUsedBytes is long used
            ? (double)used / total * 100.0
            : null;

    public double MaxPercent =>
        Math.Max(CpuPercent, Math.Max(RamPercent, Math.Max(GpuPercent ?? 0.0, VramPercent ?? 0.0)));
}
