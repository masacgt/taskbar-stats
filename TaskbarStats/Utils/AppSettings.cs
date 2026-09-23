using System.Text.Json;

namespace TaskbarStats.Utils;

public sealed class AppSettings
{
    public HashSet<string> HiddenLabels { get; set; } = new(StringComparer.Ordinal);

    public bool AlwaysOnTop { get; set; } = true;

    public bool StartupNoticeShown { get; set; }

    public int? HistoryX { get; set; }

    public int? HistoryY { get; set; }

    public int? HistoryWidth { get; set; }

    public int? HistoryHeight { get; set; }
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TaskbarStats",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
            {
                return new AppSettings();
            }

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions)
                ?? new AppSettings();
        }
        catch (Exception ex)
        {
            CrashLog.Write("settings: load failed", ex);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch (Exception ex)
        {
            CrashLog.Write("settings: save failed", ex);
        }
    }
}
