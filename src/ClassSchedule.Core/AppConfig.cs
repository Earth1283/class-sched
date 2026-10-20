using System.Text.Json;

namespace ClassSchedule.Core;

/// <summary>Per-machine settings that must be found before any schedule file is opened.</summary>
public sealed class AppConfig
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    /// <summary>Where the schedule is stored; empty means the default location.</summary>
    public string? DataPath { get; set; }

    public static string DefaultFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClassSchedule", "config.json");

    public static AppConfig Load(string? file = null)
    {
        try
        {
            var path = file ?? DefaultFile;
            if (!File.Exists(path)) return new AppConfig();
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path), Options) ?? new AppConfig();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new AppConfig();
        }
    }

    public void Save(string? file = null)
    {
        var path = file ?? DefaultFile;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
    }

    public bool IsDefault(string path) =>
        string.Equals(Path.GetFullPath(path), Path.GetFullPath(ScheduleStore.DefaultPath), StringComparison.OrdinalIgnoreCase);

    public void Use(string path) => DataPath = IsDefault(path) ? null : Path.GetFullPath(path);

    /// <summary>The file to open at startup. Falls back to the default when the chosen folder is missing, for example an unplugged drive.</summary>
    public string ResolveDataPath(out string? warning)
    {
        warning = null;
        if (string.IsNullOrWhiteSpace(DataPath)) return ScheduleStore.DefaultPath;

        var dir = Path.GetDirectoryName(DataPath);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return DataPath;

        warning = $"{DataPath} isn't available, so the default location is in use.";
        return ScheduleStore.DefaultPath;
    }
}
