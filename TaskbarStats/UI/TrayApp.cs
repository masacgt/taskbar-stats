using System.Diagnostics;
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
    private readonly DiskSampler? _diskSampler;
    private readonly NetSampler _netSampler = new();
    private readonly StatsHistory _history = new();
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly ToolStripMenuItem _alwaysOnTopItem;
    private readonly ToolStripMenuItem _labelsMenu;
    private readonly StatsLabel _label;
    private readonly AppSettings _settings = SettingsStore.Load();

    private HistoryForm? _historyForm;
    private Icon? _currentIcon;
    private int _tickCount;
    private string[] _labelOrder = StatsFormatter.BuildLabelOrder(0);
    private int _diskCount;
    private readonly HashSet<string> _hiddenLabels;
    private SystemStatsSample? _lastSample;

    public TrayApp()
    {
        _hiddenLabels = new HashSet<string>(_settings.HiddenLabels, StringComparer.Ordinal);
        _gpuSampler = GpuDetector.Create();
        _diskSampler = DiskSampler.TryCreate();
        _currentIcon = IconFactory.Create(LoadLevelClassifier.GetLevel(0), 0);

        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = "TaskbarStats",
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowHistoryForm();

        var menu = new ContextMenuStrip();
        menu.Items.Add(UiText.History, null, (_, _) => ShowHistoryForm());
        _labelsMenu = new ToolStripMenuItem(UiText.Labels);
        menu.Items.Add(_labelsMenu);
        _alwaysOnTopItem = new ToolStripMenuItem(UiText.AlwaysOnTop(_settings.AlwaysOnTop))
        {
            CheckOnClick = true,
            Checked = _settings.AlwaysOnTop,
        };
        _alwaysOnTopItem.CheckedChanged += (_, _) => ToggleAlwaysOnTop();
        menu.Items.Add(_alwaysOnTopItem);
        _autoStartItem = new ToolStripMenuItem(UiText.AutoStart(false), null, (_, _) => ToggleAutoStart());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(UiText.ResetSettings, null, (_, _) => ResetSettings());
        menu.Items.Add(UiText.OpenLogFolder, null, (_, _) => OpenLogFolder());
        menu.Items.Add(UiText.CheckForUpdates, null, (_, _) => OpenReleases());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(UiText.Exit, null, (_, _) => Exit());
        _notifyIcon.ContextMenuStrip = menu;
        RebuildLabelMenu();
        RefreshAutoStartLabel();

        _label = new StatsLabel();
        _label.SetAlwaysOnTop(_settings.AlwaysOnTop);
        _label.Show();

        if (!_settings.StartupNoticeShown)
        {
            _settings.StartupNoticeShown = true;
            SaveSettings();
            _notifyIcon.ShowBalloonTip(3000, UiText.StartedTitle, UiText.StartedBody, ToolTipIcon.Info);
        }

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
                $"first sample: CPU={sample.CpuPercent:0}% RAM={sample.RamPercent:0}% GPU={sample.GpuPercent?.ToString("0") ?? "--"}% VRAM={sample.VramPercent?.ToString("0") ?? "--"}% disks={sample.DiskBusyPercents?.Length ?? 0} lan={sample.LanMbps?.ToString("0") ?? "--"}Mbps wifi={sample.WifiMbps?.ToString("0") ?? "--"}Mbps gpu={sample.GpuName ?? "none"}",
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
        double[]? disks = _diskSampler?.Sample();
        (double? lanMbps, double? wifiMbps) = _netSampler.Sample();

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
            DiskBusyPercents = disks,
            LanMbps = lanMbps,
            WifiMbps = wifiMbps,
        };
    }

    private void UpdateUi(SystemStatsSample sample)
    {
        _lastSample = sample;
        UpdateLabelOrder(sample.DiskBusyPercents?.Length ?? 0);

        int maxPercent = (int)Math.Round(sample.MaxPercent);
        _label.UpdateText(StatsFormatter.BuildSummary(sample, VisibleLabels()));
        SetNotifyTooltip(sample);

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

        var historyForm = new HistoryForm(_history, _settings);
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

    private void ResetSettings()
    {
        if (MessageBox.Show(UiText.ResetConfirm, UiText.ResetConfirmTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        _historyForm?.Close();
        _hiddenLabels.Clear();
        _settings.AlwaysOnTop = true;
        _settings.HistoryX = null;
        _settings.HistoryY = null;
        _settings.HistoryWidth = null;
        _settings.HistoryHeight = null;
        AutoStart.SetEnabled(false);
        _alwaysOnTopItem.Checked = true;
        _alwaysOnTopItem.Text = UiText.AlwaysOnTop(true);
        _label.SetAlwaysOnTop(true);
        RebuildLabelMenu();
        RefreshAutoStartLabel();
        SaveSettings();
        MessageBox.Show(UiText.ResetDone, UiText.ResetConfirmTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static void OpenLogFolder()
    {
        try
        {
            string? directory = Path.GetDirectoryName(CrashLog.FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{CrashLog.FilePath}\"",
                    UseShellExecute = true,
                });
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("open log folder failed", ex);
        }
    }

    private static void OpenReleases()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/masacgt/taskbar-stats/releases/latest",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            CrashLog.Write("open releases failed", ex);
        }
    }

    private void ToggleAlwaysOnTop()
    {
        bool enabled = _alwaysOnTopItem.Checked;
        _label.SetAlwaysOnTop(enabled);
        _alwaysOnTopItem.Text = UiText.AlwaysOnTop(enabled);
        _settings.AlwaysOnTop = enabled;
        SaveSettings();
    }

    private void RefreshAutoStartLabel()
    {
        _autoStartItem.Text = UiText.AutoStart(AutoStart.IsEnabled());
    }

    private void Exit()
    {
        SaveSettings();
        _timer.Stop();
        _historyForm?.Close();
        _label.Close();
        _notifyIcon.Visible = false;
        Application.Exit();
    }

    private void UpdateLabelOrder(int diskCount)
    {
        // ディスク数変化(USB接続等)時はラベル順序とメニューを再構築。0 は初期値のまま。
        if (diskCount > 0 && diskCount != _diskCount)
        {
            _diskCount = diskCount;
            _labelOrder = StatsFormatter.BuildLabelOrder(diskCount);
            RebuildLabelMenu();
        }
    }

    private string[] VisibleLabels()
        => _labelOrder.Where(l => !_hiddenLabels.Contains(l)).ToArray();

    private void SaveSettings()
    {
        _settings.HiddenLabels = new HashSet<string>(_hiddenLabels, StringComparer.Ordinal);
        SettingsStore.Save(_settings);
    }

    private void RefreshNow()
    {
        if (_lastSample is { } sample)
        {
            _label.UpdateText(StatsFormatter.BuildSummary(sample, VisibleLabels()));
            SetNotifyTooltip(sample);
        }
    }

    private void SetNotifyTooltip(SystemStatsSample sample)
    {
        // NotifyIcon.Text is limited to 63 characters on Windows. A full
        // multi-line tooltip can exceed that limit and throw before the label
        // gets updated, leaving the taskbar display at its initial "--" text.
        string tooltip = StatsFormatter.BuildTooltip(sample, VisibleLabels());
        _notifyIcon.Text = tooltip.Length <= 63 ? tooltip : tooltip[..60] + "...";
    }

    private void RebuildLabelMenu()
    {
        _labelsMenu.DropDownItems.Clear();
        foreach (string label in _labelOrder)
        {
            var item = new ToolStripMenuItem(label)
            {
                CheckOnClick = true,
                Checked = !_hiddenLabels.Contains(label),
            };
            item.CheckedChanged += (_, _) =>
            {
                if (item.Checked)
                {
                    _hiddenLabels.Remove(label);
                }
                else
                {
                    _hiddenLabels.Add(label);
                }

                SaveSettings();
                RefreshNow();
            };
            _labelsMenu.DropDownItems.Add(item);
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _label.Dispose();
        _notifyIcon.Dispose();
        _historyForm?.Dispose();
        _gpuSampler?.Dispose();
        _diskSampler?.Dispose();
        if (_currentIcon is not null)
        {
            DestroyIcon(_currentIcon.Handle);
            _currentIcon = null;
        }
        _owner.Dispose();
    }
}
