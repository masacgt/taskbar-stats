using System.Runtime.InteropServices;
using System.Windows.Forms;
using TaskbarStats.Models;
using TaskbarStats.Samplers;
using TaskbarStats.Utils;

namespace TaskbarStats.UI;

public sealed class TrayApp : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    private sealed class HiddenOwnerForm : Form
    {
        public HiddenOwnerForm()
        {
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Minimized;
            Size = new Size(0, 0);
        }

        protected override void SetVisibleCore(bool value)
        {
        }
    }

    private readonly HiddenOwnerForm _owner = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Timers.Timer _timer;
    private readonly CpuSampler _cpuSampler = new();
    private readonly RamSampler _ramSampler = new();
    private readonly IGpuSampler? _gpuSampler;
    private readonly StatsHistory _history = new();
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly StatsLabel _label;

    private HistoryForm? _historyForm;
    private Icon? _currentIcon;
    private int _tickCount;

    public TrayApp()
    {
        _gpuSampler = GpuDetector.Create();
        _currentIcon = IconFactory.Create(LoadLevelClassifier.GetLevel(0), 0);

        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "TaskbarStats",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowHistoryForm();

        var menu = new ContextMenuStrip();
        menu.Items.Add("履歴を表示", null, (_, _) => ShowHistoryForm());
        _autoStartItem = new ToolStripMenuItem("自動実行: OFF", null, (_, _) => ToggleAutoStart());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Exit());
        _notifyIcon.ContextMenuStrip = menu;
        RefreshAutoStartLabel();

        _label = new StatsLabel();
        _label.Show();

        _timer = new System.Timers.Timer(1000) { AutoReset = true };
        _timer.Elapsed += OnTick;
        _timer.Start();
    }

    public Form Owner => _owner;

    private void OnTick(object? sender, System.Timers.ElapsedEventArgs e)
    {
        SystemStatsSample sample;
        try
        {
            sample = BuildSample();
        }
        catch (Exception ex)
        {
            if (_tickCount++ == 0)
            {
                CrashLog.Write("BuildSample failed", ex);
            }

            return;
        }

        if (_tickCount++ == 0)
        {
            CrashLog.Write(
                $"first sample: CPU={sample.CpuPercent:0}% RAM={sample.RamPercent:0}% GPU={sample.GpuPercent?.ToString("0") ?? "--"}% VRAM={sample.VramPercent?.ToString("0") ?? "--"}% gpu={sample.GpuName ?? "none"}",
                null);
        }

        if (!_label.IsHandleCreated)
        {
            if (_tickCount == 1)
            {
                CrashLog.Write("label handle not created at tick", null);
            }

            return;
        }

        _label.BeginInvoke(() =>
        {
            _history.Add(sample);
            UpdateUi(sample);
        });
    }

    private SystemStatsSample BuildSample()
    {
        double cpu = _cpuSampler.Sample();
        (long ramTotal, long ramAvailable) = _ramSampler.Sample();
        GpuSnapshot? gpu = _gpuSampler?.Sample();

        return new SystemStatsSample
        {
            Timestamp = DateTime.Now,
            CpuPercent = cpu,
            RamTotalBytes = ramTotal,
            RamAvailableBytes = ramAvailable,
            GpuPercent = gpu?.UtilizationPercent,
            VramTotalBytes = gpu?.VramTotalBytes,
            VramUsedBytes = gpu?.VramUsedBytes,
            GpuName = gpu?.Name,
            GpuVendor = gpu?.Vendor,
            GpuTempCelsius = gpu?.TemperatureC,
            GpuClockMHz = gpu?.ClockMHz,
        };
    }

    private void UpdateUi(SystemStatsSample sample)
    {
        int maxPercent = (int)Math.Round(sample.MaxPercent);
        _notifyIcon.Text = FormatTooltip(sample);
        _label.UpdateText(FormatSummary(sample));

        Icon newIcon = IconFactory.Create(LoadLevelClassifier.GetLevel(maxPercent), maxPercent);
        Icon? oldIcon = _currentIcon;
        _currentIcon = newIcon;
        _notifyIcon.Icon = newIcon;
        if (oldIcon is not null)
        {
            DestroyIcon(oldIcon.Handle);
        }
    }

    private void ShowHistoryForm()
    {
        if (_historyForm is { IsDisposed: false } form)
        {
            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }

            form.Activate();
            return;
        }

        var historyForm = new HistoryForm(_history);
        historyForm.FormClosed += (_, _) => _historyForm = null;
        _historyForm = historyForm;
        historyForm.Show();
        historyForm.Activate();
    }

    private void ToggleAutoStart()
    {
        AutoStart.SetEnabled(!AutoStart.IsEnabled());
        RefreshAutoStartLabel();
    }

    private void RefreshAutoStartLabel()
    {
        _autoStartItem.Text = AutoStart.IsEnabled() ? "自動実行: ON" : "自動実行: OFF";
    }

    private void Exit()
    {
        _timer.Stop();
        _historyForm?.Close();
        _label.Close();
        _notifyIcon.Visible = false;
        Application.Exit();
    }

    public static string FormatTooltip(SystemStatsSample sample)
    {
        var lines = new List<string>
        {
            $"CPU   {sample.CpuPercent:F0}%",
            $"RAM   {sample.RamPercent:F0}%  ({FormatGib(sample.RamUsedBytes)}/{FormatGib(sample.RamTotalBytes)} GB)",
        };

        if (sample.GpuPercent is double gpu)
        {
            string name = string.IsNullOrWhiteSpace(sample.GpuName) ? string.Empty : $"  ({sample.GpuName})";
            lines.Add($"GPU   {gpu:F0}%{name}");
        }
        else
        {
            lines.Add("GPU   -   (GPUを検出できません)");
        }

        if (sample.VramPercent is double vram)
        {
            lines.Add($"VRAM  {vram:F0}%  ({FormatGib(sample.VramUsedBytes ?? 0)}/{FormatGib(sample.VramTotalBytes ?? 0)} GB)");
        }
        else
        {
            lines.Add("VRAM  -");
        }

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

        return string.Join(Environment.NewLine, lines);
    }

    public static string FormatGib(long bytes)
        => bytes > 0 ? (bytes / 1073741824.0).ToString("F1") : "0.0";

    public static string FormatSummary(SystemStatsSample sample)
        => $"CPU {FormatPercent(sample.CpuPercent)}%  RAM {FormatPercent(sample.RamPercent)}%  GPU {FormatPercent(sample.GpuPercent)}%  VRAM {FormatPercent(sample.VramPercent)}%";

    private static string FormatPercent(double? value) => value?.ToString("0").PadLeft(3) ?? " --";

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _label.Dispose();
        _notifyIcon.Dispose();
        _historyForm?.Dispose();
        _gpuSampler?.Dispose();
        if (_currentIcon is not null)
        {
            DestroyIcon(_currentIcon.Handle);
            _currentIcon = null;
        }
        _owner.Dispose();
    }
}
