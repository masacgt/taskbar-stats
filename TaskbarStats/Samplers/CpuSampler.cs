using System.Runtime.InteropServices;

namespace TaskbarStats.Samplers;

public sealed class CpuSampler
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    private long? _prevIdle;
    private long? _prevTotal;

    public double Sample()
    {
        if (!GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user))
        {
            return 0.0;
        }

        long idleTicks = ToTicks(idle);
        long totalTicks = ToTicks(kernel) + ToTicks(user);

        double? percent = null;
        if (_prevIdle is long prevIdle && _prevTotal is long prevTotal)
        {
            percent = CpuMath.CalculatePercent(prevIdle, prevTotal, idleTicks, totalTicks);
        }

        _prevIdle = idleTicks;
        _prevTotal = totalTicks;

        return percent ?? 0.0;
    }

    private static long ToTicks(FileTime value) => ((long)value.High << 32) | value.Low;
}
