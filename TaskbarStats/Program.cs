using TaskbarStats.UI;
using TaskbarStats.Utils;

namespace TaskbarStats;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\TaskbarStats.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        Application.ThreadException += (_, e) => CrashLog.Write("UIスレッド例外", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            CrashLog.Write("未処理例外(プロセス終了)", e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        CrashLog.Write("起動", null);
        using var trayApp = new TrayApp();
        Application.Run(trayApp.Owner);
    }
}
