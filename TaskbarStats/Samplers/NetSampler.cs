using System.Net.NetworkInformation;
using TaskbarStats.Utils;

namespace TaskbarStats.Samplers;

/// <summary>
/// アクティブなイーサネットアダプタの合計転送速度 (Mbps)。
/// System.Net.NetworkInformation の累積バイト数から
/// 1 秒間の差分で速度を求める。P/Invoke 不使用。
/// </summary>
public sealed class NetSampler
{
    private const int AdapterRefreshTicks = 30;

    private string[] _ethernetNames = Array.Empty<string>();
    private long _lastRx;
    private long _lastTx;
    private bool _hasBaseline;
    private int _ticks;
    private bool _adaptersLogged;
    private bool _errorLogged;

    public double? Sample()
    {
        try
        {
            _ticks++;
            if ((_ticks - 1) % AdapterRefreshTicks == 0)
            {
                RefreshAdapters();
            }

            if (_ethernetNames.Length == 0)
            {
                return null;
            }

            long rx = 0;
            long tx = 0;
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (!IsTarget(ni))
                {
                    continue;
                }

                var stats = ni.GetIPv4Statistics();
                rx += stats.BytesReceived;
                tx += stats.BytesSent;
            }

            if (!_hasBaseline)
            {
                _hasBaseline = true;
                _lastRx = rx;
                _lastTx = tx;
                return null;
            }

            // 累積カウンタの差分 (ulong ラップは自然に安全)
            double bytesPerSec = (rx - _lastRx) + (tx - _lastTx);
            _lastRx = rx;
            _lastTx = tx;
            return NetMath.BytesPerSecToMbits(bytesPerSec);
        }
        catch (Exception ex)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                CrashLog.Write("net: sample failed", ex);
            }

            return null;
        }
    }

    private bool IsTarget(NetworkInterface ni)
        => _ethernetNames.Contains(ni.Name, StringComparer.OrdinalIgnoreCase);

    private void RefreshAdapters()
    {
        var names = new List<string>();
        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet
                && ni.OperationalStatus == OperationalStatus.Up
                && !string.IsNullOrEmpty(ni.Name))
            {
                names.Add(ni.Name);
            }
        }

        string[] previous = _ethernetNames;
        _ethernetNames = names.ToArray();
        if (_hasBaseline && !previous.SequenceEqual(_ethernetNames))
        {
            // アダプタ構成が変わると累積バイトの差分が意味をなさない
            _hasBaseline = false;
        }

        if (!_adaptersLogged)
        {
            _adaptersLogged = true;
            CrashLog.Write($"net: ethernet adapters=[{string.Join("; ", names)}]");
        }
    }
}
