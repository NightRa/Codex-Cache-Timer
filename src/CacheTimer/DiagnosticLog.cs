using System.Diagnostics;
using System.IO;
using System.Text;

namespace CodexCacheTimer;

internal static class DiagnosticLog
{
    private static readonly object Gate = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexCacheTimer", "logs");

    public static string CurrentPath => Path.Combine(DirectoryPath,
        $"cache-timer-{DateTime.Now:yyyy-MM-dd}.log");

    public static void Info(string eventName, string details = "") => Write("INFO", eventName, details);
    public static void Warn(string eventName, string details = "") => Write("WARN", eventName, details);
    public static void Error(string eventName, Exception exception) =>
        Write("ERROR", eventName, exception.ToString());

    private static void Write(string level, string eventName, string details)
    {
        var line = $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} tid={Environment.CurrentManagedThreadId} "
            + $"{level} {eventName} {details.Replace('\r', ' ').Replace('\n', ' ')}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                using var stream = new FileStream(CurrentPath, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite | FileShare.Delete);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(line);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Log write failed: {error}");
            }
        }
    }
}
