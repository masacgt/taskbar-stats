using System.Globalization;

namespace TaskbarStats.UI;

internal static class UiText
{
    private static readonly bool IsJapanese =
        string.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase);

    public static string History => IsJapanese ? "履歴を表示" : "Show history";
    public static string Labels => IsJapanese ? "表示ラベル" : "Display labels";
    public static string AlwaysOnTop(bool enabled)
        => IsJapanese
            ? $"最前面に固定: {(enabled ? "ON" : "OFF")}" 
            : $"Always on top: {(enabled ? "ON" : "OFF")}";

    public static string AutoStart(bool enabled)
        => IsJapanese
            ? $"自動実行: {(enabled ? "ON" : "OFF")}" 
            : $"Startup: {(enabled ? "ON" : "OFF")}";

    public static string Exit => IsJapanese ? "終了" : "Exit";
    public static string ResetSettings => IsJapanese ? "設定を初期化" : "Reset settings";
    public static string OpenLogFolder => IsJapanese ? "ログフォルダーを開く" : "Open log folder";
    public static string CheckForUpdates => IsJapanese ? "最新版を確認" : "Check for updates";
    public static string StartedTitle => "TaskbarStats";
    public static string StartedBody => IsJapanese ? "TaskbarStatsを起動しました。" : "TaskbarStats is running.";
    public static string AlreadyRunningTitle => "TaskbarStats";
    public static string AlreadyRunning => IsJapanese ? "TaskbarStatsはすでに起動しています。" : "TaskbarStats is already running.";
    public static string ResetConfirmTitle => IsJapanese ? "設定を初期化" : "Reset settings";
    public static string ResetConfirm => IsJapanese ? "表示ラベル、最前面表示、自動実行、履歴画面の位置とサイズを初期化します。よろしいですか？" : "Reset display labels, always-on-top, startup, and history window settings?";
    public static string ResetDone => IsJapanese ? "設定を初期化しました。" : "Settings have been reset.";
    public static string HistoryTitle => IsJapanese ? "TaskbarStats - 履歴 (直近5分)" : "TaskbarStats - History (last 5 minutes)";
    public static string WaitingForData => IsJapanese ? "データ待ち..." : "Waiting for data...";
}
