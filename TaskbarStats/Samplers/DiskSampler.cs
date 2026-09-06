using System.Runtime.InteropServices;
using TaskbarStats.Utils;

namespace TaskbarStats.Samplers;

/// <summary>
/// 物理ディスクごとのアクティブな時間 % (タスクマネージャーの「アクティブな時間」)。
/// PDH の \PhysicalDisk(*)\% Disk Time を読み、ディスク番号ごとに
/// DiskAggregator で集約する。
/// </summary>
public sealed class DiskSampler : IDisposable
{
    private const string CounterPath = "\\PhysicalDisk(*)\\% Disk Time";

    private const int PdhSuccess = 0;
    private const int PdhCstatusNewData = 1;
    private const int PdhMoreData = unchecked((int)0x800007D2);
    private const int PdhFmtDouble = 0x200;
    private const int DiagLimit = 8;

    private readonly object _gate = new();
    private IntPtr _hQuery;
    private IntPtr _hCounter;
    private bool _readOkLogged;
    private int _diagLogged;

    private DiskSampler() { }

    public static DiskSampler? TryCreate()
    {
        try
        {
            var sampler = new DiskSampler();
            int stOpen = PdhOpenQueryA(IntPtr.Zero, 0, out sampler._hQuery);
            if (stOpen != PdhSuccess || sampler._hQuery == IntPtr.Zero)
            {
                CrashLog.Write($"disk: pdh open failed=0x{stOpen:X8}");
                return null;
            }

            int stAdd = PdhAddEnglishCounterA(sampler._hQuery, CounterPath, IntPtr.Zero, out sampler._hCounter);
            if (stAdd != PdhSuccess)
            {
                CrashLog.Write($"disk: pdh add failed=0x{stAdd:X8}");
                PdhCloseQuery(sampler._hQuery);
                sampler._hQuery = IntPtr.Zero;
                sampler._hCounter = IntPtr.Zero;
                return null;
            }

            // 初回 collect でインスタンス列挙を促す。
            PdhCollectQueryData(sampler._hQuery);
            CrashLog.Write("disk: pdh initialized");
            return sampler;
        }
        catch (Exception ex)
        {
            CrashLog.Write("disk: sampler init failed", ex);
            return null;
        }
    }

    public double[]? Sample()
    {
        lock (_gate)
        {
            if (_hQuery == IntPtr.Zero || _hCounter == IntPtr.Zero)
            {
                return null;
            }

            if (PdhCollectQueryData(_hQuery) != PdhSuccess)
            {
                return null;
            }

            return ReadPairs();
        }
    }

    private double[]? ReadPairs()
    {
        int bufferSize = 0;
        int itemCount = 0;
        int status = PdhGetFormattedCounterArrayA(_hCounter, PdhFmtDouble, ref bufferSize, ref itemCount, IntPtr.Zero);
        if (status is not (PdhMoreData or PdhSuccess) || bufferSize <= 0)
        {
            LogDiag($"disk: pdh read#1 failed status=0x{status:X8}");
            return null;
        }

        byte[] buffer = new byte[bufferSize];
        GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        IntPtr pinnedAddr = pinned.AddrOfPinnedObject();
        try
        {
            status = PdhGetFormattedCounterArrayA(_hCounter, PdhFmtDouble, ref bufferSize, ref itemCount, pinnedAddr);
            if (status != PdhSuccess || itemCount <= 0)
            {
                LogDiag($"disk: pdh read#2 failed status=0x{status:X8}");
                return null;
            }

            int stride = Marshal.SizeOf<PdhFmtCounterValueItem>();
            int count = Math.Min(itemCount, buffer.Length / stride);
            var pairs = new List<(string, double)>(count);
            string first = string.Empty;

            for (int i = 0; i < count; i++)
            {
                PdhFmtCounterValueItem item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(pinnedAddr + i * stride);
                if (item.CStatus is not (PdhSuccess or PdhCstatusNewData))
                {
                    continue;
                }

                string name = Marshal.PtrToStringAnsi(item.szName) ?? string.Empty;
                if (first.Length == 0)
                {
                    first = name;
                }

                pairs.Add((name, item.DoubleValue));
            }

            double[]? result = DiskAggregator.Aggregate(pairs);
            if (!_readOkLogged)
            {
                _readOkLogged = true;
                CrashLog.Write($"disk: pdh read ok instances={count} disks={result?.Length ?? 0} first=\"{first}\"");
            }

            return result;
        }
        catch (Exception ex)
        {
            LogDiag($"disk: pdh read exception: {ex.Message}");
            return null;
        }
        finally
        {
            pinned.Free();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_hQuery != IntPtr.Zero)
            {
                PdhCloseQuery(_hQuery);
                _hQuery = IntPtr.Zero;
                _hCounter = IntPtr.Zero;
            }
        }
    }

    private void LogDiag(string message)
    {
        if (_diagLogged < DiagLimit)
        {
            _diagLogged++;
            CrashLog.Write(message);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItem
    {
        public IntPtr szName;
        public uint CStatus;
        public double DoubleValue;
    }

    [DllImport("pdh.dll", CharSet = CharSet.Ansi, EntryPoint = "PdhOpenQueryA")]
    private static extern int PdhOpenQueryA(IntPtr lpMachineName, int dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", CharSet = CharSet.Ansi, EntryPoint = "PdhAddEnglishCounterA")]
    private static extern int PdhAddEnglishCounterA(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll")]
    private static extern int PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll")]
    private static extern int PdhGetFormattedCounterArrayA(IntPtr hCounter, int dwFormat, ref int lpdwBufferSize, ref int lpdwItemCount, IntPtr itemBuffer);

    [DllImport("pdh.dll")]
    private static extern int PdhCloseQuery(IntPtr hQuery);
}
