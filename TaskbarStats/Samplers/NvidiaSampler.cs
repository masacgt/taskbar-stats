using System.Runtime.InteropServices;
using System.Text;
using TaskbarStats.Utils;

namespace TaskbarStats.Samplers;

public sealed class NvidiaSampler : IGpuSampler
{
    private const string DllName = "nvapi.dll";

    private const int NvApiOk = 0;
    private const int NvApiApiNotInitialized = -1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Utilization
    {
        public int Gpu;
        public int Mem;
        public int Enc;
        public int Dec;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SmbMemoryInfo
    {
        public ulong Total;
        public ulong Used;
        public ulong Available;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Clocks
    {
        public int Graphics;
        public int Sm;
        public int Memory;
        public int Audio;
    }

    [DllImport(DllName)]
    private static extern int NvAPI_Initialize();

    [DllImport(DllName)]
    private static extern int NvAPI_EnumPhysicalGpu(out IntPtr handle, int index);

    [DllImport(DllName, CharSet = CharSet.Ansi)]
    private static extern int NvAPI_GPU_GetName(IntPtr handle, byte[] name, ref int length);

    [DllImport(DllName)]
    private static extern int NvAPI_GPU_GetUtilization(IntPtr handle, out Utilization utilization);

    [DllImport(DllName)]
    private static extern int NvAPI_GPU_GetDedicatedVideoMemoryInfo(IntPtr handle, out SmbMemoryInfo info);

    [DllImport(DllName)]
    private static extern int NvAPI_GPU_GetTachReading(IntPtr handle, out int temperature);

    [DllImport(DllName)]
    private static extern int NvAPI_GPU_GetCurrentClocks(IntPtr handle, out Clocks clocks);

    private readonly IntPtr _handle;
    private readonly string _name;

    public string Vendor => "NVIDIA";

    private NvidiaSampler(IntPtr handle, string name)
    {
        _handle = handle;
        _name = name;
    }

    public static NvidiaSampler? TryCreate()
    {
        int initResult = NvAPI_Initialize();
        if (initResult != NvApiOk && initResult != NvApiApiNotInitialized)
        {
            CrashLog.Write($"gpu: NvAPI_Initialize failed (code={initResult})", null);
            return null;
        }

        IntPtr bestHandle = IntPtr.Zero;
        ulong bestVram = 0;

        for (int index = 0; ; index++)
        {
            if (NvAPI_EnumPhysicalGpu(out IntPtr handle, index) != NvApiOk)
            {
                break;
            }

            if (NvAPI_GPU_GetDedicatedVideoMemoryInfo(handle, out SmbMemoryInfo info) == NvApiOk
                && info.Total > bestVram)
            {
                bestHandle = handle;
                bestVram = info.Total;
            }
        }

        if (bestHandle == IntPtr.Zero)
        {
            CrashLog.Write("gpu: no NVIDIA physical GPU with VRAM found", null);
            return null;
        }

        string name = "NVIDIA GPU";
        var buffer = new byte[128];
        int length = buffer.Length;
        if (NvAPI_GPU_GetName(bestHandle, buffer, ref length) == NvApiOk && length > 0)
        {
            name = Encoding.ASCII.GetString(buffer, 0, Math.Min(length, buffer.Length - 1)).TrimEnd();
        }

        return new NvidiaSampler(bestHandle, name);
    }

    public GpuSnapshot Sample()
    {
        var snapshot = new GpuSnapshot
        {
            Name = _name,
            Vendor = Vendor,
        };

        try
        {
            if (NvAPI_GPU_GetUtilization(_handle, out Utilization utilization) == NvApiOk)
            {
                snapshot.UtilizationPercent = Math.Clamp(utilization.Gpu, 0, 100);
            }
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
        }

        try
        {
            if (NvAPI_GPU_GetDedicatedVideoMemoryInfo(_handle, out SmbMemoryInfo memory) == NvApiOk)
            {
                snapshot.VramTotalBytes = (long)memory.Total;
                snapshot.VramUsedBytes = (long)memory.Used;
            }
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
        }

        try
        {
            if (NvAPI_GPU_GetTachReading(_handle, out int temperature) == NvApiOk
                && temperature > 0 && temperature < 150)
            {
                snapshot.TemperatureC = temperature;
            }
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
        }

        try
        {
            if (NvAPI_GPU_GetCurrentClocks(_handle, out Clocks clocks) == NvApiOk && clocks.Graphics > 0)
            {
                snapshot.ClockMHz = clocks.Graphics;
            }
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
        }

        return snapshot;
    }

    public void Dispose()
    {
    }

    private static bool IsNativeFailure(Exception exception)
        => exception is DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception;
}
