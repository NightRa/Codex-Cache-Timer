using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CodexCacheTimer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--dump", StringComparer.OrdinalIgnoreCase))
        {
            var sessions = new CodexSessionReader().ReadRecent(TimeSpan.FromHours(3));
            var lines = sessions.Select(session =>
                $"{session.Id} | {session.Title} | {session.Model} | {session.Anchor:O} | running={session.Running}");
            var output = args.SkipWhile(arg => arg != "--dump").Skip(1).FirstOrDefault();
            if (output is not null) File.WriteAllLines(output, lines);
            else foreach (var line in lines) Console.WriteLine(line);
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            DiagnosticLog.Error("unhandled", e.ExceptionObject as Exception
                ?? new Exception(e.ExceptionObject.ToString()));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DiagnosticLog.Error("unobserved-task", e.Exception);
            e.SetObserved();
        };
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => DiagnosticLog.Error("ui-exception", e.Exception);
        DiagnosticLog.Info("startup", $"version={Assembly.GetExecutingAssembly().GetName().Version} "
            + $"runtime={Environment.Version} exe={Environment.ProcessPath} "
            + $"codexHome={Environment.GetEnvironmentVariable("CODEX_HOME") ?? "default"}");
        var dpiResult = TaskbarOverlayForm.SetProcessDpiAwarenessContext(new nint(-4));
        DiagnosticLog.Info("dpi-context", $"perMonitorV2={dpiResult}");
        ApplicationConfiguration.Initialize();
        Application.Run(new TimerApplicationContext());
        DiagnosticLog.Info("shutdown");
    }
}
