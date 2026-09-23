using System.Net.NetworkInformation;
using TaskbarStats.Utils;

namespace TaskbarStats.Samplers;

/// <summary>
/// アクティブなLAN / Wi-Fiアダプタごとの合計転送速度 (Mbps)。
/// System.Net.NetworkInformation の累積バイト数から
/// 1 秒間の差分で速度を求める。P/Invoke 不使用。
/// </summary>
public sealed class NetSampler
{
    private const int AdapterRefreshTicks = 30;

    private string[] _ethernetNames = Array.Empty<string>();
    private string[] _wifiNames = Array.Empty<string>();
    private long _lastLanRx;
    private long _lastLanTx;
    private long _lastWifiRx;
    private long _lastWifiTx;
    private bool _hasLanBaseline;
    private bool _hasWifiBaseline;
    private int _ticks;
    private bool _adaptersLogged;
    private bool _errorLogged;

    public (double? LanMbps, double? WifiMbps) Sample()
    {
        try
        {
            _ticks++;
            if ((_ticks - 1) % AdapterRefreshTicks == 0)
            {
                RefreshAdapters();
            }

            long lanRx = 0;
            long lanTx = 0;
            long wifiRx = 0;
            long wifiTx = 0;
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (IsTarget(ni, _ethernetNames))
                {
                    var stats = ni.GetIPv4Statistics();
                    lanRx += stats.BytesReceived;
                    lanTx += stats.BytesSent;
                }

                if (IsTarget(ni, _wifiNames))
                {
                    var stats = ni.GetIPv4Statistics();
                    wifiRx += stats.BytesReceived;
                    wifiTx += stats.BytesSent;
                }
            }

            double? lan = _ethernetNames.Length == 0
                ? null
                : CalculateMbps(lanRx, lanTx, ref _lastLanRx, ref _lastLanTx, ref _hasLanBaseline);
            double? wifi = _wifiNames.Length == 0
                ? null
                : CalculateMbps(wifiRx, wifiTx, ref _lastWifiRx, ref _lastWifiTx, ref _hasWifiBaseline);
            if (_ethernetNames.Length == 0)
            {
                _hasLanBaseline = false;
            }

            if (_wifiNames.Length == 0)
            {
                _hasWifiBaseline = false;
            }
            return (lan, wifi);
        }
        catch (Exception ex)
        {
            if (!_errorLogged)
            {
                _errorLogged = true;
                CrashLog.Write("net: sample failed", ex);
            }

            return (null, null);
        }
    }

    private static double? CalculateMbps(long rx, long tx, ref long lastRx, ref long lastTx, ref bool hasBaseline)
    {
        if (!hasBaseline)
        {
            hasBaseline = true;
            lastRx = rx;
            lastTx = tx;
            return null;
        }

        double bytesPerSec = (rx - lastRx) + (tx - lastTx);
        lastRx = rx;
        lastTx = tx;
        return NetMath.BytesPerSecToMbits(bytesPerSec);
    }

    private static bool IsTarget(NetworkInterface ni, string[] names)
        => names.Contains(ni.Name, StringComparer.OrdinalIgnoreCase);

    private void RefreshAdapters()
    {
        var ethernetNames = new List<string>();
        var wifiNames = new List<string>();
        foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || string.IsNullOrEmpty(ni.Name))
            {
                continue;
            }

            if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
            {
                ethernetNames.Add(ni.Name);
            }
            else if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            {
                wifiNames.Add(ni.Name);
            }
        }

        string[] previous = _ethernetNames;
        string[] previousWifi = _wifiNames;
        _ethernetNames = ethernetNames.ToArray();
        _wifiNames = wifiNames.ToArray();
        if (_hasLanBaseline && !previous.SequenceEqual(_ethernetNames))
        {
            // アダプタ構成が変わると累積バイトの差分が意味をなさない
            _hasLanBaseline = false;
        }

        if (_hasWifiBaseline && !previousWifi.SequenceEqual(_wifiNames))
        {
            _hasWifiBaseline = false;
        }

        if (!_adaptersLogged)
        {
            _adaptersLogged = true;
            CrashLog.Write($"net: ethernet adapters=[{string.Join("; ", _ethernetNames)}] wifi adapters=[{string.Join("; ", _wifiNames)}]");
        }
    }
}
