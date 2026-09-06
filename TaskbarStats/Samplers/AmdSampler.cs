using System.Reflection;
using System.Runtime.InteropServices;
using TaskbarStats.Utils;

namespace TaskbarStats.Samplers;

public sealed class AmdSampler : IGpuSampler
{
    private const string DllName = "atiadlxx.dll";

    private const int AdlOk = 0;
    private const int AdlTempGpu = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct Adapter
    {
        public int AdapterIndex;
        public int AdapterDriverIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string AdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string AdapterDescription;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DriverDescription;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuUtilization
    {
        public int Gpu;
        public int Vram;
        public int Decode;
        public int Encode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VramInfo
    {
        public ulong Total;
        public ulong Used;
        public ulong Free;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GpuMetrics
    {
        public int Clocks;
        public int Power;
        public int Temperature;
        public GpuUtilization Utilization;
        public int UtilizationClocks;
        public int UtilizationTemperature;
        public int Reserved;
    }

    [DllImport(DllName)]
    private static extern int ADL2_Initialize();

    [DllImport(DllName)]
    private static extern int ADL2_Shutdown();

    [DllImport(DllName)]
    private static extern int ADL2_AdapterEnum(ref Adapter adapter, int index);

    [DllImport(DllName)]
    private static extern int ADL2_GPU_UtilizationGet(int adapterIndex, out GpuUtilization utilization);

    [DllImport(DllName)]
    private static extern int ADL2_VRAM_InfoGet(int adapterIndex, out VramInfo vram);

    [DllImport(DllName)]
    private static extern int ADL2_Temperature_Get(int adapterIndex, int type, out int temperature);

    [DllImport(DllName)]
    private static extern int ADL2_GPU_MetricsGet(int adapterIndex, out GpuMetrics metrics);

    private readonly int _adapterIndex;
    private readonly string _name;
    private bool _shutdownCalled;

    public string Vendor => "AMD";

    static AmdSampler()
    {
        NativeLibrary.SetDllImportResolver(typeof(AmdSampler).Assembly, ResolveNativeLibrary);
    }

    private static IntPtr ResolveNativeLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (string.Equals(libraryName, DllName, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var candidate in CandidatePaths(DllName))
            {
                try
                {
                    if (File.Exists(candidate))
                    {
                        return NativeLibrary.Load(candidate);
                    }
                }
                catch
                {
                }
            }
        }

        return IntPtr.Zero;
    }

    private static IEnumerable<string> CandidatePaths(string dllName)
    {
        string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        string fileRepo = Path.Combine(systemDir, "DriverStore", "FileRepository");
        if (Directory.Exists(fileRepo))
        {
            foreach (var file in Directory.EnumerateFiles(fileRepo, dllName, SearchOption.AllDirectories))
            {
                yield return file;
            }
        }

        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(programFiles))
        {
            string amdDir = Path.Combine(programFiles, "AMD");
            if (Directory.Exists(amdDir))
            {
                foreach (var file in Directory.EnumerateFiles(amdDir, dllName, SearchOption.AllDirectories))
                {
                    yield return file;
                }
            }
        }
    }

    private AmdSampler(int adapterIndex, string name)
    {
        _adapterIndex = adapterIndex;
        _name = name;
    }

    public static AmdSampler? TryCreate()
    {
        int initResult = ADL2_Initialize();
        if (initResult != AdlOk)
        {
            CrashLog.Write($"gpu: ADL2_Initialize failed (code={initResult})", null);
            return null;
        }

        int bestIndex = -1;
        long bestVram = -1;
        string bestName = string.Empty;

        for (int index = 0; ; index++)
        {
            var adapter = new Adapter();
            if (ADL2_AdapterEnum(ref adapter, index) != AdlOk)
            {
                break;
            }

            if (!GpuUtilizationSupported(adapter.AdapterIndex))
            {
                continue;
            }

            if (TryGetVramTotal(adapter.AdapterIndex) is long total && total > bestVram)
            {
                bestVram = total;
                bestIndex = adapter.AdapterIndex;
                bestName = string.IsNullOrWhiteSpace(adapter.AdapterDescription)
                    ? adapter.AdapterName ?? string.Empty
                    : adapter.AdapterDescription;
            }
        }

        if (bestIndex < 0)
        {
            CrashLog.Write("gpu: no AMD adapter with utilization support", null);
            ADL2_Shutdown();
            return null;
        }

        return new AmdSampler(bestIndex, string.IsNullOrWhiteSpace(bestName) ? "AMD GPU" : bestName.Trim());
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
            if (ADL2_GPU_UtilizationGet(_adapterIndex, out GpuUtilization utilization) == AdlOk)
            {
                snapshot.UtilizationPercent = Math.Clamp(utilization.Gpu, 0, 100);
            }
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
        }

        try
        {
            if (ADL2_VRAM_InfoGet(_adapterIndex, out VramInfo vram) == AdlOk)
            {
                snapshot.VramTotalBytes = (long)vram.Total;
                snapshot.VramUsedBytes = (long)vram.Used;
            }
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
        }

        try
        {
            if (ADL2_Temperature_Get(_adapterIndex, AdlTempGpu, out int temperature) == AdlOk
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
            if (ADL2_GPU_MetricsGet(_adapterIndex, out GpuMetrics metrics) == AdlOk
                && metrics.Clocks > 0 && metrics.Clocks < 5000)
            {
                snapshot.ClockMHz = metrics.Clocks;
            }
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
        }

        return snapshot;
    }

    public void Dispose()
    {
        if (_shutdownCalled)
        {
            return;
        }

        _shutdownCalled = true;
        try
        {
            ADL2_Shutdown();
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
        }
    }

    private static bool GpuUtilizationSupported(int adapterIndex)
    {
        try
        {
            return ADL2_GPU_UtilizationGet(adapterIndex, out _) == AdlOk;
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
            return false;
        }
    }

    private static long? TryGetVramTotal(int adapterIndex)
    {
        try
        {
            if (ADL2_VRAM_InfoGet(adapterIndex, out VramInfo vram) == AdlOk)
            {
                return (long)vram.Total;
            }
        }
        catch (Exception ex) when (IsNativeFailure(ex))
        {
        }

        return null;
    }

    private static bool IsNativeFailure(Exception exception)
        => exception is DllNotFoundException or EntryPointNotFoundException or System.ComponentModel.Win32Exception;
}
