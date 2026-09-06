using System.IO;

namespace TaskbarStats.Utils;

/// <summary>
/// クラッシュ情報を %LOCALAPPDATA%\TaskbarStats\crash.log に書き出す。
/// </summary>
public static class CrashLog
{
    private static readonly object Lock = new();

    public static void Write(string title, Exception? ex = null)
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskbarStats");
            string path = Path.Combine(dir, "crash.log");
            string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {title}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}";
            lock (Lock)
            {
                Directory.CreateDirectory(dir);
                File.AppendAllText(path, entry);
            }
        }
        catch
        {
        }
    }
}
