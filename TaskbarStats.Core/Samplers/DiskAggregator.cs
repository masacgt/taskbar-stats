namespace TaskbarStats.Samplers;

/// <summary>
/// PDH の \PhysicalDisk インスタンス(ディスク番号 + ボリューム)を
/// ディスク番号ごとに集約し、番号順のコンパクトな配列に変換する。
/// </summary>
public static class DiskAggregator
{
    public static double[] Aggregate(IEnumerable<(string Name, double Value)> items)
    {
        var byDisk = new Dictionary<int, double>();

        foreach ((string name, double value) in items)
        {
            if (!TryParseDiskNumber(name, out int disk))
            {
                continue;
            }

            double clamped = Math.Clamp(value, 0.0, 100.0);
            byDisk[disk] = Math.Max(byDisk.GetValueOrDefault(disk), clamped);
        }

        return byDisk
            .OrderBy(kv => kv.Key)
            .Select(kv => kv.Value)
            .ToArray();
    }

    /// <summary>
    /// PDH インスタンス名は "0 C:", "10 D:" のような「ディスク番号 + ボリューム」形式。
    /// 先頭の数字列をディスク番号として取り出す。
    /// </summary>
    public static bool TryParseDiskNumber(string instance, out int disk)
    {
        disk = 0;
        int end = 0;
        while (end < instance.Length && char.IsDigit(instance[end]))
        {
            end++;
        }

        if (end == 0)
        {
            return false;
        }

        return int.TryParse(instance.AsSpan(0, end), out disk);
    }
}
