namespace TaskbarStats.Samplers;

public sealed class GpuSnapshot
{
    public string? Name { get; set; }

    public string? Vendor { get; set; }

    public double? UtilizationPercent { get; set; }

    public long? VramTotalBytes { get; set; }

    public long? VramUsedBytes { get; set; }

    public int? TemperatureC { get; set; }

    public int? ClockMHz { get; set; }
}
