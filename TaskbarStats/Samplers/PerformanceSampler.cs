using System.Runtime.InteropServices;
using System.Text;
using TaskbarStats.Utils;

namespace TaskbarStats.Samplers;

/// <summary>
/// ベンダー非依存 GPU サンプラ。ベンダー DLL（nvapi.dll / atiadlxx.dll）が
/// 存在しない環境でも動作する。
/// 使用率・VRAM 使用量は Windows パフォーマンスカウンター（PDH）、
/// VRAM 総量とアダプタ名は DXGI を使用する。
/// PDH の VRAM カウンターが利用できない環境では
/// IDXGIAdapter3::QueryVideoMemoryInfo にフォールバックする。
/// </summary>
public sealed class PerformanceSampler : IGpuSampler, IDisposable
{
    private const string UtilizationPath = "\\GPU Engine(*)\\Utilization Percentage";
    private const string VramPath = "\\GPU Process Memory(*)\\Committed Bytes";

    private const int PdhSuccess = 0;
    private const int PdhCstatusNewData = 1;
    private const int PdhMoreData = unchecked((int)0x800007D2);
    private const int PdhFmtDouble = 0x200;
    private const int VramMaxRetries = 30;

    // IID_IDXGIFactory (Windows SDK dxgi.h)
    private static readonly Guid IidDxgiFactory = new("7B7166EC-21C7-44AE-B21A-C9AE321AE369");

    // IID_IDXGIAdapter3 — QueryVideoMemoryInfo はこのインターフェースのメソッド。
    // 呼び出す前に QueryInterface でサポートを確認する (vtable slot 14)。
    private static readonly Guid IidDxgiAdapter3 = new("608C03ED-8B72-4232-8BD2-7A6DEA9AECEA");

    // DXGI_MEMORY_SEGMENT_GROUP_LOCAL
    private const uint DxgiMemorySegmentLocal = 0;

    private readonly object _gate = new();
    private IntPtr _hQuery;
    private IntPtr _hEngine;
    private IntPtr _hVram;
    private int _vramRetryCount;
    private string _adapterName = string.Empty;
    private long _vramTotalBytes;
    private static int _pdhDiagLogged;
    private const int PdhDiagLimit = 8;

    // QueryVideoMemoryInfo フォールバック用のアダプタ参照 (AddRef 済み)
    private IntPtr _dxgiAdapter;
    private bool _dxgiVramAvailable;
    private bool _dxgiVramLogged;

    // 対象アダプタの LUID プレフィックス。PDH インスタンス名は
    // luid_0xXXXXXXXX_0xYYYYYYYY_phys_0 の形をしており、複数 GPU 環境では
    // 対象アダプタのインスタンスだけ合計することで過大計上を防ぐ。
    private string[] _vramLuidPrefixes = Array.Empty<string>();
    private bool _vramNoMatchLogged;
    private bool _vramReadLogged;

    public string Vendor { get; } = "Performance";

    private PerformanceSampler() { }

    public static PerformanceSampler? TryCreate()
    {
        try
        {
            var sampler = new PerformanceSampler();
            if (!sampler.Initialize())
            {
                sampler.Dispose();
                return null;
            }

            return sampler;
        }
        catch (Exception ex)
        {
            CrashLog.Write("gpu: performance sampler init failed", ex);
            return null;
        }
    }

    private bool Initialize()
    {
        int stOpen = PdhOpenQueryA(IntPtr.Zero, 0, out _hQuery);
        CrashLog.Write($"gpu: pdh open query=0x{stOpen:X8} h={(_hQuery == IntPtr.Zero ? "null" : "ok")}");
        if (stOpen != PdhSuccess || _hQuery == IntPtr.Zero)
        {
            return false;
        }

        int stEngine = PdhAddEnglishCounterA(_hQuery, UtilizationPath, IntPtr.Zero, out _hEngine);
        CrashLog.Write($"gpu: pdh add engine=0x{stEngine:X8} h={(_hEngine == IntPtr.Zero ? "null" : "ok")}");
        if (stEngine != PdhSuccess)
        {
            PdhCloseQuery(_hQuery);
            _hQuery = IntPtr.Zero;
            return false;
        }

        // VRAM カウンターは任意。利用不可でも使用率は取得できる。
        // GPU Process Memory オブジェクトは起動直後にインスタンスが 0 個
        // のことがあり、add が失敗する (例: 0xC0000BB9)。一度 collect して
        // インスタンス列挙を促してからリトライする。
        int stVram = PdhAddEnglishCounterA(_hQuery, VramPath, IntPtr.Zero, out _hVram);
        bool vramRetried = false;
        if (stVram != PdhSuccess)
        {
            _hVram = IntPtr.Zero;
            vramRetried = true;
            PdhCollectQueryData(_hQuery);
            stVram = PdhAddEnglishCounterA(_hQuery, VramPath, IntPtr.Zero, out _hVram);
            if (stVram != PdhSuccess)
            {
                _hVram = IntPtr.Zero;
            }
        }
        CrashLog.Write($"gpu: pdh add vram=0x{stVram:X8}{(vramRetried ? " (retried)" : "")} h={(_hVram == IntPtr.Zero ? "null" : "ok")}");
        if (_hVram == IntPtr.Zero)
        {
            // 標準オブジェクトがマシンに存在しない可能性がある。
            // PDH オブジェクトを列挙して使える GPU メモリカウンターを探す。
            TryDiscoverVramCounter(stVram);
        }
        _vramRetryCount = _hVram == IntPtr.Zero ? 0 : VramMaxRetries;

        // レシオ系カウンターは 2 サンプル目から意味のある値になるため、
        // 最初に 1 回収集しておく。
        int stCollect = PdhCollectQueryData(_hQuery);
        CrashLog.Write($"gpu: pdh initial collect=0x{stCollect:X8}");

        // 一発の診断: サンプラ稼働前に読み取り経路を検証する。
        if (_hEngine != IntPtr.Zero && TryReadValues(_hEngine, out double[] test))
        {
            CrashLog.Write($"gpu: pdh test read ok count={test.Length} max={(test.Length > 0 ? test.Max().ToString("0.#") : "n/a")}");
        }
        else
        {
            CrashLog.Write("gpu: pdh test read failed (no values)");
        }

        QueryAdapterInfo();
        return true;
    }

    public GpuSnapshot Sample()
    {
        var snapshot = new GpuSnapshot
        {
            Vendor = Vendor,
            Name = string.IsNullOrEmpty(_adapterName) ? null : _adapterName,
            VramTotalBytes = _vramTotalBytes > 0 ? _vramTotalBytes : null,
        };

        if (_hQuery == IntPtr.Zero)
        {
            return snapshot;
        }

        lock (_gate)
        {
            if (_hQuery == IntPtr.Zero)
            {
                return snapshot;
            }

            if (PdhCollectQueryData(_hQuery) != PdhSuccess)
            {
                return snapshot;
            }

            // VRAM カウンターが起動時に追加できなかった場合はここで再試行する。
            // GPU Process Memory オブジェクトは GPU が使われてから
            // インスタンスを持つことがあるため。上限を設けて無駄打ちを防ぐ。
            if (_hVram == IntPtr.Zero && _vramRetryCount < VramMaxRetries)
            {
                _vramRetryCount++;
                int stRetry = PdhAddEnglishCounterA(_hQuery, VramPath, IntPtr.Zero, out _hVram);
                if (stRetry != PdhSuccess)
                {
                    _hVram = IntPtr.Zero;
                }
                else
                {
                    // 新規追加したカウンターに値を持たせるため 1 回収集する。
                    PdhCollectQueryData(_hQuery);
                    CrashLog.Write("gpu: pdh add vram (lazy) ok");
                }
            }

            if (_hEngine != IntPtr.Zero && TryReadValues(_hEngine, out double[] engineValues))
            {
                snapshot.UtilizationPercent = engineValues.Length > 0 ? engineValues.Max() : 0.0;
            }

            if (_hVram != IntPtr.Zero && TryReadVramValue(out long vramUsed))
            {
                // 全アダプタ合計などの過大計上に対する安全策
                if (_vramTotalBytes > 0 && vramUsed > _vramTotalBytes)
                {
                    vramUsed = _vramTotalBytes;
                }

                snapshot.VramUsedBytes = vramUsed;
            }

            // PDH の VRAM カウンターが利用できない場合のフォールバック:
            // QueryVideoMemoryInfo (IDXGIAdapter3, vtable slot 14)。
            if (snapshot.VramUsedBytes is null
                && _dxgiVramAvailable
                && _dxgiAdapter != IntPtr.Zero
                && TryQueryVideoMemory(_dxgiAdapter, out long dxgiUsage, out long dxgiReservation)
                && dxgiReservation > 0)
            {
                snapshot.VramUsedBytes = Math.Min(dxgiUsage, dxgiReservation);
                if (_vramTotalBytes <= 0)
                {
                    _vramTotalBytes = dxgiReservation;
                }

                if (!_dxgiVramLogged)
                {
                    _dxgiVramLogged = true;
                    CrashLog.Write($"gpu: dxgi videomem sample ok usage={dxgiUsage / (1024 * 1024)}MiB reservation={dxgiReservation / (1024 * 1024)}MiB");
                }
            }
        }

        return snapshot;
    }

    private static bool TryReadValues(IntPtr hCounter, out double[] values)
    {
        values = Array.Empty<double>();

        int bufferSize = 0;
        int itemCount = 0;
        int status = PdhGetFormattedCounterArrayA(hCounter, PdhFmtDouble, ref bufferSize, ref itemCount, IntPtr.Zero);
        if (status is not (PdhMoreData or PdhSuccess) || bufferSize <= 0)
        {
            LogPdhDiag($"gpu: pdh read#1 failed status=0x{status:X8} bufferSize={bufferSize}");

            return false;
        }

        byte[] buffer = new byte[bufferSize];
        GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        IntPtr pinnedAddr = pinned.AddrOfPinnedObject();
        try
        {
            status = PdhGetFormattedCounterArrayA(hCounter, PdhFmtDouble, ref bufferSize, ref itemCount, pinnedAddr);
            if (status != PdhSuccess || itemCount <= 0)
            {
                LogPdhDiag($"gpu: pdh read#2 failed status=0x{status:X8} bufferSize={bufferSize} itemCount={itemCount}");

                return false;
            }

            int stride = Marshal.SizeOf<PdhFmtCounterValueItem>();
            int count = Math.Min(itemCount, buffer.Length / stride);
            var list = new List<double>(count);
            for (int i = 0; i < count; i++)
            {
                PdhFmtCounterValueItem item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(pinnedAddr + i * stride);
                if (item.CStatus is PdhSuccess or PdhCstatusNewData)
                {
                    list.Add(item.DoubleValue);
                }
            }
            values = list.ToArray();

            LogPdhDiag($"gpu: pdh read ok count={count} max={(count > 0 ? values.Max().ToString("0.#") : "n/a")}");
            return true;
        }
        catch (Exception ex)
        {
            LogPdhDiag($"gpu: pdh read exception: {ex.Message}");
            return false;
        }
        finally
        {
            pinned.Free();
        }
    }

    // VRAM カウンターを読み取り、対象アダプタ (LUID) のインスタンスだけ合計する。
    // \GPU Adapter Memory(*)\Dedicated Usage のようなカウンターはアダプタごとに
    // 1 インスタンス持っており、全インスタンスを足すと複数 GPU 環境で過大計上になる。
    private bool TryReadVramValue(out long used)
    {
        used = 0;

        int bufferSize = 0;
        int itemCount = 0;
        int status = PdhGetFormattedCounterArrayA(_hVram, PdhFmtDouble, ref bufferSize, ref itemCount, IntPtr.Zero);
        if (status is not (PdhMoreData or PdhSuccess) || bufferSize <= 0)
        {
            LogPdhDiag($"gpu: pdh vram read#1 failed status=0x{status:X8} bufferSize={bufferSize}");
            return false;
        }

        byte[] buffer = new byte[bufferSize];
        GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        IntPtr pinnedAddr = pinned.AddrOfPinnedObject();
        try
        {
            status = PdhGetFormattedCounterArrayA(_hVram, PdhFmtDouble, ref bufferSize, ref itemCount, pinnedAddr);
            if (status != PdhSuccess || itemCount <= 0)
            {
                LogPdhDiag($"gpu: pdh vram read#2 failed status=0x{status:X8}");
                return false;
            }

            int stride = Marshal.SizeOf<PdhFmtCounterValueItem>();
            int count = Math.Min(itemCount, buffer.Length / stride);
            long sum = 0;
            int matched = 0;
            string firstInstance = string.Empty;

            for (int i = 0; i < count; i++)
            {
                PdhFmtCounterValueItem item = Marshal.PtrToStructure<PdhFmtCounterValueItem>(pinnedAddr + i * stride);
                if (item.CStatus is not (PdhSuccess or PdhCstatusNewData))
                {
                    continue;
                }

                string name = Marshal.PtrToStringAnsi(item.szName) ?? string.Empty;
                if (firstInstance.Length == 0)
                {
                    firstInstance = name;
                }

                if (_vramLuidPrefixes.Length == 0
                    || _vramLuidPrefixes.Any(p => name.Contains(p, StringComparison.Ordinal)))
                {
                    sum += (long)item.DoubleValue;
                    matched++;
                }
            }

            if (matched == 0 && _vramLuidPrefixes.Length > 0)
            {
                // LUID が一致しない場合、全インスタンス合計は過大計上になるため
                // 値を出さず (UI 上は "--")、診断ログだけ残す。
                if (!_vramNoMatchLogged)
                {
                    _vramNoMatchLogged = true;
                    CrashLog.Write($"gpu: pdh vram: no instance matches luid [{string.Join("; ", _vramLuidPrefixes)}] first=\"{firstInstance}\"");
                }

                return false;
            }

            used = sum;
            if (!_vramReadLogged)
            {
                _vramReadLogged = true;
                CrashLog.Write($"gpu: pdh vram read ok matched={matched}/{count} sum={sum / (1024 * 1024)}MiB first=\"{firstInstance}\"");
            }

            return true;
        }
        catch (Exception ex)
        {
            LogPdhDiag($"gpu: pdh vram read exception: {ex.Message}");
            return false;
        }
        finally
        {
            pinned.Free();
        }
    }

    private static void LogPdhDiag(string message)
    {
        if (_pdhDiagLogged < PdhDiagLimit)
        {
            _pdhDiagLogged++;
            CrashLog.Write(message);
        }
    }

    // --- VRAM カウンターの自動検出 (標準オブジェクトがない場合) ---

    private void TryDiscoverVramCounter(int initialStatus)
    {
        CrashLog.Write($"gpu: pdh discover: enumerating objects (initial vram status=0x{initialStatus:X8})");
        string[] objects = EnumPdhObjects();
        if (objects.Length == 0)
        {
            CrashLog.Write("gpu: pdh discover: object enumeration failed");
            return;
        }

        string? bestObject = null;
        string? bestCounter = null;
        int bestScore = 0;

        foreach (string obj in objects)
        {
            if (!obj.Contains("GPU", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryEnumObjectItems(obj, out string[] counters, out string[] instances))
            {
                CrashLog.Write($"gpu: pdh discover: item enumeration failed for \"{obj}\"");
                continue;
            }

            CrashLog.Write($"gpu: pdh discover: object \"{obj}\": {counters.Length} counters [{TruncateItems(counters)}] {instances.Length} instances [{TruncateItems(instances)}]");

            // アダプタ単位のオブジェクト (1 インスタンス = 1 GPU) を優先する。
            // プロセス単位のオブジェクトは全プロセスを合計すると
            // 複数 GPU 環境で dedicated VRAM を超えることがある。
            int objScore = obj.Equals("GPU Adapter Memory", StringComparison.OrdinalIgnoreCase) ? 200
                : obj.Equals("GPU Process Memory", StringComparison.OrdinalIgnoreCase) ? 100
                : obj.Contains("MEMORY", StringComparison.OrdinalIgnoreCase) ? 50
                : 10;

            foreach (string counter in counters)
            {
                int counterScore = counter.Equals("Dedicated Usage", StringComparison.OrdinalIgnoreCase) ? 100
                    : counter.Equals("Committed Bytes", StringComparison.OrdinalIgnoreCase) ? 90
                    : counter.Equals("Local Usage", StringComparison.OrdinalIgnoreCase) ? 80
                    : counter.Contains("Committed", StringComparison.OrdinalIgnoreCase) ? 70
                    : counter.Equals("In Use", StringComparison.OrdinalIgnoreCase) ? 60
                    : counter.Contains("Used", StringComparison.OrdinalIgnoreCase) ? 50
                    : 0;
                if (counterScore == 0)
                {
                    continue;
                }

                int score = objScore * 1000 + counterScore;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestObject = obj;
                    bestCounter = counter;
                }
            }
        }

        if (bestObject is null || bestCounter is null)
        {
            CrashLog.Write("gpu: pdh discover: no GPU memory counter candidate found");
            return;
        }

        string path = $@"\{bestObject}(*)\{bestCounter}";
        int st = PdhAddEnglishCounterA(_hQuery, path, IntPtr.Zero, out _hVram);
        if (st != PdhSuccess)
        {
            string plain = $@"\{bestObject}\{bestCounter}";
            st = PdhAddEnglishCounterA(_hQuery, plain, IntPtr.Zero, out _hVram);
            if (st != PdhSuccess)
            {
                _hVram = IntPtr.Zero;
                CrashLog.Write($"gpu: pdh discover: add \"{path}\" failed=0x{st:X8}");
                return;
            }

            path = plain;
        }

        PdhCollectQueryData(_hQuery);
        CrashLog.Write($"gpu: pdh discover: vram counter added \"{path}\" ok");
    }

    private static string[] EnumPdhObjects()
    {
        int size = 0;
        int st = PdhEnumObjectsA(null, null, IntPtr.Zero, ref size, 0, 1);
        if (st is not (PdhSuccess or PdhMoreData) || size <= 0 || size > 1024 * 1024)
        {
            return Array.Empty<string>();
        }

        byte[] buffer = new byte[size];
        GCHandle pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            st = PdhEnumObjectsA(null, null, pinned.AddrOfPinnedObject(), ref size, 0, 1);
            if (st is not (PdhSuccess or PdhMoreData))
            {
                return Array.Empty<string>();
            }

            return ParseNullTerminatedList(buffer);
        }
        finally
        {
            pinned.Free();
        }
    }

    private static bool TryEnumObjectItems(string objectName, out string[] counters, out string[] instances)
    {
        counters = Array.Empty<string>();
        instances = Array.Empty<string>();

        foreach (int detail in new[] { 0, 1 })
        {
            int counterSize = 0;
            int instanceSize = 0;
            int st = PdhEnumObjectItemsA(null, null, objectName, IntPtr.Zero, ref counterSize, IntPtr.Zero, ref instanceSize, detail, 0);
            if (st is not (PdhSuccess or PdhMoreData)
                || counterSize <= 0 || instanceSize <= 0
                || counterSize > 1024 * 1024 || instanceSize > 1024 * 1024)
            {
                continue;
            }

            byte[] counterBuf = new byte[counterSize];
            byte[] instanceBuf = new byte[instanceSize];
            GCHandle counterPin = GCHandle.Alloc(counterBuf, GCHandleType.Pinned);
            GCHandle instancePin = GCHandle.Alloc(instanceBuf, GCHandleType.Pinned);
            try
            {
                st = PdhEnumObjectItemsA(null, null, objectName, counterPin.AddrOfPinnedObject(), ref counterSize, instancePin.AddrOfPinnedObject(), ref instanceSize, detail, 0);
                if (st is not (PdhSuccess or PdhMoreData))
                {
                    continue;
                }

                counters = ParseNullTerminatedList(counterBuf);
                instances = ParseNullTerminatedList(instanceBuf);
                return true;
            }
            finally
            {
                counterPin.Free();
                instancePin.Free();
            }
        }

        return false;
    }

    // PZZSTR (null 終了文字列の null 終了配列) を string[] に変換する。
    private static string[] ParseNullTerminatedList(byte[] buffer)
    {
        var list = new List<string>();
        int start = 0;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != 0)
            {
                continue;
            }

            if (i > start)
            {
                list.Add(Encoding.ASCII.GetString(buffer, start, i - start));
            }

            start = i + 1;
        }

        return list.ToArray();
    }

    private static string TruncateItems(string[] items)
    {
        const int max = 16;
        if (items.Length == 0)
        {
            return "(none)";
        }

        string shown = string.Join("; ", items.Take(max));
        return items.Length > max ? shown + "; ..." : shown;
    }

    private void QueryAdapterInfo()
    {
        IntPtr factory = IntPtr.Zero;
        try
        {
            Guid iid = IidDxgiFactory;
            int hrFactory = CreateDXGIFactory(ref iid, out factory);
            CrashLog.Write($"gpu: dxgi create factory=0x{hrFactory:X8} h={(factory == IntPtr.Zero ? "null" : "ok")}");
            if (hrFactory != 0 || factory == IntPtr.Zero)
            {
                return;
            }

            long bestTotal = 0;
            string bestName = string.Empty;
            IntPtr bestAdapter = IntPtr.Zero;
            string[] bestLuidPrefixes = Array.Empty<string>();

            for (uint ordinal = 0; ; ordinal++)
            {
                int hr = EnumAdapters(factory, ordinal, out IntPtr adapter);
                if (hr != 0 || adapter == IntPtr.Zero)
                {
                    CrashLog.Write($"gpu: dxgi enum adapter[{ordinal}]=0x{hr:X8} h={(adapter == IntPtr.Zero ? "null" : "ok")}");
                    break;
                }

                try
                {
                    var descMaybe = GetAdapterDesc(adapter);
                    if (ordinal == 0)
                    {
                        CrashLog.Write($"gpu: dxgi desc[0]={(descMaybe is null ? "null" : $"ok name={descMaybe.Value.AdapterName} dedicated={(long)descMaybe.Value.DedicatedVideoMemory / (1024 * 1024)}MiB")}");
                    }

                    if (descMaybe is { } desc)
                    {
                        long dedicated = (long)desc.DedicatedVideoMemory;
                        if (dedicated > bestTotal)
                        {
                            // QueryVideoMemoryInfo 用にベストアダプタの参照を保持する。
                            if (bestAdapter != IntPtr.Zero)
                            {
                                ReleaseCom(bestAdapter);
                            }

                            bestAdapter = adapter;
                            AddRefCom(adapter);
                            bestTotal = dedicated;
                            bestName = desc.AdapterName;
                            string p1 = $"luid_0x{desc.LuidHigh:X8}_0x{desc.LuidLow:X8}";
                            string p2 = $"luid_0x{desc.LuidLow:X8}_0x{desc.LuidHigh:X8}";
                            bestLuidPrefixes = p1 == p2 ? new[] { p1 } : new[] { p1, p2 };
                        }
                    }
                }
                finally
                {
                    ReleaseCom(adapter);
                }
            }

            _vramTotalBytes = bestTotal;
            _adapterName = bestName;
            _dxgiAdapter = bestAdapter;
            _vramLuidPrefixes = bestLuidPrefixes;
            if (bestLuidPrefixes.Length > 0)
            {
                CrashLog.Write($"gpu: dxgi best luid=[{string.Join("; ", bestLuidPrefixes)}]");
            }

            if (_dxgiAdapter != IntPtr.Zero)
            {
                if (IsAdapter3(_dxgiAdapter)
                    && TryQueryVideoMemory(_dxgiAdapter, out long vmemUsage, out long vmemReservation))
                {
                    _dxgiVramAvailable = true;
                    if (_vramTotalBytes <= 0 && vmemReservation > 0)
                    {
                        _vramTotalBytes = vmemReservation;
                    }

                    CrashLog.Write($"gpu: dxgi videomem ok usage={vmemUsage / (1024 * 1024)}MiB reservation={vmemReservation / (1024 * 1024)}MiB");
                }
                else
                {
                    CrashLog.Write("gpu: dxgi videomem unavailable (IDXGIAdapter3 not supported)");
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("gpu: dxgi adapter query failed", ex);
        }
        finally
        {
            ReleaseCom(factory);
        }
    }

    private static int EnumAdapters(IntPtr factory, uint ordinal, out IntPtr adapter)
    {
        adapter = IntPtr.Zero;
        IntPtr fn = GetVtableSlot(factory, 7);
        return ((EnumAdaptersFn)Marshal.GetDelegateForFunctionPointer(fn, typeof(EnumAdaptersFn)))(factory, ordinal, out adapter);
    }

    private static DxgiAdapterDesc1? GetAdapterDesc(IntPtr adapter)
    {
        // IDXGIAdapter1::GetDesc1 (vtable slot 10) fills DXGI_ADAPTER_DESC1, whose memory
        // fields are SIZE_T (64-bit). Slot 8 (IDXGIAdapter::GetDesc) fills the v1 struct
        // with 32-bit fields, which truncates e.g. 24 GiB of VRAM down to 0.
        IntPtr fn = GetVtableSlot(adapter, 10);
        var getDesc1 = (GetDesc1Fn)Marshal.GetDelegateForFunctionPointer(fn, typeof(GetDesc1Fn));
        return getDesc1(adapter, out DxgiAdapterDesc1 desc) == 0 ? desc : null;
    }

    private static void ReleaseCom(IntPtr comObject)
    {
        if (comObject == IntPtr.Zero)
        {
            return;
        }

        IntPtr fn = GetVtableSlot(comObject, 2);
        ((ReleaseFn)Marshal.GetDelegateForFunctionPointer(fn, typeof(ReleaseFn)))(comObject);
    }

    private static IntPtr GetVtableSlot(IntPtr comObject, int slot)
    {
        IntPtr vtable = Marshal.ReadIntPtr(comObject);
        return Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
    }

    private static void AddRefCom(IntPtr comObject)
    {
        IntPtr fn = GetVtableSlot(comObject, 1);
        ((AddRefFn)Marshal.GetDelegateForFunctionPointer(fn, typeof(AddRefFn)))(comObject);
    }

    private static bool IsAdapter3(IntPtr adapter)
    {
        IntPtr fn = GetVtableSlot(adapter, 0);
        var qi = (QueryInterfaceFn)Marshal.GetDelegateForFunctionPointer(fn, typeof(QueryInterfaceFn));
        Guid iid = IidDxgiAdapter3;
        int hr = qi(adapter, ref iid, out IntPtr result);
        if (hr != 0 || result == IntPtr.Zero)
        {
            return false;
        }

        ReleaseCom(result);
        return true;
    }

    // IDXGIAdapter3::QueryVideoMemoryInfo (vtable slot 14)。
    // DXGI_ADAPTER_DESC1 の GetDesc1 が slot 10 で動作していることと
    // vtable の並び (adapter2: 11-13, adapter3: 14-16) から導かれる。
    private static bool TryQueryVideoMemory(IntPtr adapter, out long usage, out long reservation)
    {
        usage = 0;
        reservation = 0;

        IntPtr fn = GetVtableSlot(adapter, 14);
        var query = (QueryVideoMemoryInfoFn)Marshal.GetDelegateForFunctionPointer(fn, typeof(QueryVideoMemoryInfoFn));
        if (query(adapter, DxgiMemorySegmentLocal, out DxgiQueryVideoMemoryInfo info) != 0)
        {
            return false;
        }

        usage = (long)info.CurrentUsage;
        reservation = (long)info.CurrentReservation;
        return usage > 0 || reservation > 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_hQuery != IntPtr.Zero)
            {
                PdhCloseQuery(_hQuery);
                _hQuery = IntPtr.Zero;
                _hEngine = IntPtr.Zero;
                _hVram = IntPtr.Zero;
            }

            if (_dxgiAdapter != IntPtr.Zero)
            {
                ReleaseCom(_dxgiAdapter);
                _dxgiAdapter = IntPtr.Zero;
            }
        }
    }

    // PDH_FMT_COUNTERVALUE_ITEM_A (Windows SDK Pdh.h)
    [StructLayout(LayoutKind.Sequential)]
    private struct PdhFmtCounterValueItem
    {
        public IntPtr szName;
        public uint CStatus;
        public double DoubleValue;
    }

    // DXGI_ADAPTER_DESC1 (Windows SDK dxgi.h), 312 bytes. Returned by IDXGIAdapter1::GetDesc1.
    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiAdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128, ArraySubType = UnmanagedType.U2)]
        public ushort[] NameChars;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public UIntPtr DedicatedVideoMemory;
        public UIntPtr DedicatedSystemMemory;
        public UIntPtr SharedSystemMemory;
        public uint LuidLow;
        public uint LuidHigh;
        public uint Flags;

        public string AdapterName
        {
            get
            {
                if (NameChars is null)
                {
                    return string.Empty;
                }

                int length = 0;
                while (length < NameChars.Length && NameChars[length] != 0)
                {
                    length++;
                }

                char[] chars = new char[length];
                for (int i = 0; i < length; i++)
                {
                    chars[i] = (char)NameChars[i];
                }

                return new string(chars);
            }
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumAdaptersFn(IntPtr self, uint ordinal, out IntPtr ppAdapter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDesc1Fn(IntPtr self, out DxgiAdapterDesc1 pDesc);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint ReleaseFn(IntPtr self);

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

    [DllImport("dxgi.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int CreateDXGIFactory(ref Guid riid, out IntPtr ppFactory);

    [DllImport("pdh.dll", CharSet = CharSet.Ansi, EntryPoint = "PdhEnumObjectsA")]
    private static extern int PdhEnumObjectsA(string? szDataSource, string? szMachineName, IntPtr mszObjectList, ref int pcchBufferSize, int dwDetailLevel, int bRefresh);

    [DllImport("pdh.dll", CharSet = CharSet.Ansi, EntryPoint = "PdhEnumObjectItemsA")]
    private static extern int PdhEnumObjectItemsA(string? szDataSource, string? szMachineName, string szObjectName, IntPtr mszCounterList, ref int pcchCounterListLength, IntPtr mszInstanceList, ref int pcchInstanceListLength, int dwDetailLevel, int dwFlags);

    // DXGI_QUERY_VIDEO_MEMORY_INFO (Windows SDK dxgi.h), 32 bytes
    [StructLayout(LayoutKind.Sequential)]
    private struct DxgiQueryVideoMemoryInfo
    {
        public UIntPtr Budget;
        public UIntPtr CurrentUsage;
        public UIntPtr AvailableForReservation;
        public UIntPtr CurrentReservation;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint AddRefFn(IntPtr self);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryInterfaceFn(IntPtr self, ref Guid riid, out IntPtr ppvObject);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int QueryVideoMemoryInfoFn(IntPtr self, uint segmentType, out DxgiQueryVideoMemoryInfo pVideoMemoryInfo);
}
