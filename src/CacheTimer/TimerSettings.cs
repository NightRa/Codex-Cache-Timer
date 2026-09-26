using System.IO;
using System.Text.Json;

namespace CodexCacheTimer;

internal sealed class TimerSettings
{
    public int RecentHours { get; set; } = 3;
    public string? PreferredSessionId { get; set; }

    public static string PathOnDisk => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexCacheTimer", "settings.json");

    public static TimerSettings Load()
    {
        var path = PathOnDisk;
        if (!File.Exists(path))
        {
            var defaults = new TimerSettings();
            defaults.Save();
            return defaults;
        }
        try
        {
            var settings = JsonSerializer.Deserialize<TimerSettings>(File.ReadAllText(path)) ?? new();
            settings.RecentHours = Math.Clamp(settings.RecentHours, 1, 24);
            return settings;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return new TimerSettings();
        }
    }

    public void Save()
    {
        var path = PathOnDisk;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
        File.Move(temp, path, true);
    }
}
