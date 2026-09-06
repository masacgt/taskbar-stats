using TaskbarStats.Models;
using TaskbarStats.Samplers;

namespace TaskbarStats.UI;

/// <summary>
/// タスクバー右端ラベル(ストリップ)とトレイのツールチップの
/// 文字列生成。固定幅セグメント + 2 スペース区切り。
/// </summary>
public static class StatsFormatter
{
    public const string NetLabel = "NET";

    /// <summary>ラベルの標準順序: CPU, RAM, GPU, VRAM, Disk1..N, NET。</summary>
    public static string[] BuildLabelOrder(int diskCount)
    {
        var list = new List<string> { "CPU", "RAM", "GPU", "VRAM" };
        for (int i = 1; i <= diskCount; i++)
        {
            list.Add($"Disk{i}");
        }

        list.Add(NetLabel);
        return list.ToArray();
    }

    /// <summary>3 桁パッドのパーセント (% 付き)。null は " --%"。</summary>
    public static string FormatPercent(double? value)
        => value is { } v ? v.ToString("0").PadLeft(3) + "%" : " --%";

    /// <summary>4 桁パッドの Mbps (最大 9999)。null は "  --"。</summary>
    public static string FormatMbits(double? value)
        => value is { } v ? NetMath.ClampMbps(v).ToString().PadLeft(4) : "  --";

    public static string FormatValue(SystemStatsSample sample, string label) => label switch
    {
        "CPU" => FormatPercent(sample.CpuPercent),
        "RAM" => FormatPercent(sample.RamPercent),
        "GPU" => FormatPercent(sample.GpuPercent),
        "VRAM" => FormatPercent(sample.VramPercent),
        NetLabel => FormatMbits(sample.NetMbps) + "Mbps",
        _ when TryGetDiskNumber(label, out int number)
            && sample.DiskBusyPercents is { } disks
            && number - 1 < disks.Length
            => FormatPercent(disks[number - 1]),
        _ => FormatPercent(null),
    };

    public static string BuildSummary(SystemStatsSample sample, string[] visibleLabels)
    {
        var parts = new List<string>(visibleLabels.Length);
        foreach (string label in visibleLabels)
        {
            parts.Add($"{label} {FormatValue(sample, label)}");
        }

        return string.Join("  ", parts);
    }

    public static string BuildTooltip(SystemStatsSample sample, string[] visibleLabels)
    {
        var lines = new List<string>(visibleLabels.Length + 2);
        bool gpuVisible = false;

        foreach (string label in visibleLabels)
        {
            switch (label)
            {
                case "CPU":
                    lines.Add($"CPU   {sample.CpuPercent:F0}%");
                    break;
                case "RAM":
                    lines.Add($"RAM   {sample.RamPercent:F0}%  ({FormatGib(sample.RamUsedBytes)}/{FormatGib(sample.RamTotalBytes)} GB)");
                    break;
                case "GPU":
                    gpuVisible = true;
                    if (sample.GpuPercent is double gpu)
                    {
                        string name = string.IsNullOrWhiteSpace(sample.GpuName) ? string.Empty : $"  ({sample.GpuName})";
                        lines.Add($"GPU   {gpu:F0}%{name}");
                    }
                    else
                    {
                        lines.Add("GPU   -   (GPUを検出できません)");
                    }

                    break;
                case "VRAM":
                    if (sample.VramPercent is double vram)
                    {
                        lines.Add($"VRAM  {vram:F0}%  ({FormatGib(sample.VramUsedBytes ?? 0)}/{FormatGib(sample.VramTotalBytes ?? 0)} GB)");
                    }
                    else
                    {
                        lines.Add("VRAM  -");
                    }

                    break;
                case NetLabel:
                    string netValue = sample.NetMbps is { } n ? NetMath.ClampMbps(n).ToString() : "--";
                    lines.Add($"NET   {netValue}Mbps");
                    break;
                default:
                    if (TryGetDiskNumber(label, out int number)
                        && sample.DiskBusyPercents is { } disks
                        && number - 1 < disks.Length)
                    {
                        lines.Add($"{label}  {disks[number - 1]:F0}%");
                    }
                    else
                    {
                        lines.Add($"{label}  --%");
                    }

                    break;
            }
        }

        if (gpuVisible)
        {
            if (sample.GpuTempCelsius is int temp && sample.GpuClockMHz is int clock)
            {
                lines.Add($"{temp}°C / {clock} MHz");
            }
            else if (sample.GpuTempCelsius is { } temperature)
            {
                lines.Add($"{temperature}°C");
            }
            else if (sample.GpuClockMHz is { } clockOnly)
            {
                lines.Add($"{clockOnly} MHz");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatGib(long bytes)
        => bytes > 0 ? (bytes / 1073741824.0).ToString("F1") : "0.0";

    public static bool TryGetDiskNumber(string label, out int number)
    {
        number = 0;
        if (!label.StartsWith("Disk", StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(label["Disk".Length..], out number) && number >= 1;
    }
}
