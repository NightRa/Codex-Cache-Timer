using CodexCacheTimer;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string output = Path.GetFullPath("artifacts/popup");
        bool live = false;
        for (int index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--output" when index + 1 < args.Length:
                    output = Path.GetFullPath(args[++index]);
                    break;
                case "--live":
                    live = true;
                    break;
                default:
                    Console.Error.WriteLine("Usage: dotnet run --project tools/CacheTimer.Render -- [--output DIRECTORY] [--live]");
                    return 2;
            }
        }

        try
        {
            // Match the desktop app's DPI awareness and WinForms configuration.
            TaskbarOverlayForm.SetProcessDpiAwarenessContext(new nint(-4));
            ApplicationConfiguration.Initialize();
            Directory.CreateDirectory(output);
            var settings = new TimerSettings();
            if (live)
            {
                var now = DateTimeOffset.Now;
                var sessions = new CodexSessionReader().ReadRecent(TimeSpan.FromHours(settings.RecentHours));
                Render(output, "live", sessions, settings, null, now);
                return 0;
            }

            // Fixed clock and synthetic titles keep captures repeatable and shareable.
            var clock = new DateTimeOffset(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
            CodexSession Session(string id, string title, int minutesAgo) =>
                new(id, title, "model", clock.AddMinutes(-minutesAgo),
                    clock.AddMinutes(-minutesAgo), false, null);
            CodexSession[] examples =
            [
                Session("cold", "Example uncommitted changes with a title that should be ellipsized consistently", 90),
                Session("urgent", "Example", 27),
                Session("soon", "Review popup layout and alignment", 23),
                Session("warm", "Implement the popup image renderer", 5),
                Session("running", "Running task", 0) with { Running = true, RunningSince = clock.AddSeconds(-83) },
            ];
            Render(output, "mixed", examples, settings, "warm", clock);
            Render(output, "empty", [], settings, null, clock);
            Render(output, "long-text",
            [
                Session("long-cold", new string('W', 100), 1500),
                Session("long-running", "A long running task with a title that also needs to fit beside its elapsed time", 0)
                    with { Running = true, RunningSince = clock.AddMinutes(-1234) },
            ], settings, "long-running", clock);
            var many = Enumerable.Range(0, 20)
                .Select(index => Session($"task-{index:00}", $"Example task {index + 1:00} — inspect scrolling and row spacing", 29 - index))
                .ToArray();
            Render(output, "scroll-top", many, settings, "task-03", clock);
            Render(output, "scroll-bottom", many, settings, "task-19", clock, scrollToBottom: true);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static void Render(string output, string name, IReadOnlyList<CodexSession> sessions,
        TimerSettings settings, string? preferredId, DateTimeOffset now, bool scrollToBottom = false)
    {
        using var popup = new SessionsPopupForm();
        // A visible control tree is needed for WinForms layout at the desktop DPI.
        // Keep it offscreen; DrawToBitmap captures the controls, not desktop pixels.
        popup.Location = new Point(-32000, -32000);
        popup.Show();
        popup.SetSessions(sessions, settings, preferredId, now);
        popup.PerformLayout();
        Application.DoEvents();
        if (scrollToBottom)
        {
            popup.ScrollListTo(popup.MaximumScroll);
            Application.DoEvents();
        }
        using var bitmap = new Bitmap(popup.ClientSize.Width, popup.ClientSize.Height);
        popup.DrawToBitmap(bitmap, popup.ClientRectangle);
        var path = Path.Combine(output, $"{name}.png");
        bitmap.Save(path, ImageFormat.Png);
        Console.WriteLine($"{path} ({bitmap.Width}x{bitmap.Height}, DPI={popup.DeviceDpi}, rows={sessions.Count}, scrollY={-popup.ScrollOffset})");
    }
}
