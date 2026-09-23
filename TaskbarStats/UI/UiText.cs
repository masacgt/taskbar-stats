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
    public static string HistoryTitle => IsJapanese ? "TaskbarStats - 履歴 (直近5分)" : "TaskbarStats - History (last 5 minutes)";
    public static string WaitingForData => IsJapanese ? "データ待ち..." : "Waiting for data...";
}
