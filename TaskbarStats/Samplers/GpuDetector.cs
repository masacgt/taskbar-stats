using System.Runtime.InteropServices;
using TaskbarStats.Utils;

namespace TaskbarStats.Samplers;

public static class GpuDetector
{
    public static IGpuSampler? Create()
    {
        try
        {
            if (NvidiaSampler.TryCreate() is { } nvidia)
            {
                CrashLog.Write($"gpu detect: {nvidia.Vendor}", null);
                return nvidia;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            CrashLog.Write("gpu detect: nvapi.dll load failed", ex);
        }

        try
        {
            if (AmdSampler.TryCreate() is { } amd)
            {
                CrashLog.Write($"gpu detect: {amd.Vendor}", null);
                return amd;
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            CrashLog.Write("gpu detect: atiadlxx.dll load failed", ex);
        }

        try
        {
            if (PerformanceSampler.TryCreate() is { } perf)
            {
                CrashLog.Write($"gpu detect: {perf.Vendor}", null);
                return perf;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("gpu detect: performance sampler failed", ex);
        }

        CrashLog.Write("gpu detect: no supported GPU", null);
        return null;
    }
}
