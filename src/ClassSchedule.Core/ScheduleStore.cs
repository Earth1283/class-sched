using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClassSchedule.Core;

/// <summary>Reads and writes the schedule as one JSON file.</summary>
public sealed class ScheduleStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ScheduleStore(string path) => Path = path;

    public string Path { get; }

    /// <summary>Set when the last Load had to set a damaged file aside.</summary>
    public string? LoadWarning { get; private set; }

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClassSchedule", "schedule.json");

    public ScheduleData Load(DateTime today)
    {
        LoadWarning = null;
        if (!File.Exists(Path)) return new ScheduleData();

        try
        {
            var json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize<ScheduleData>(json, Options) ?? new ScheduleData();
        }
        catch (JsonException)
        {
            var aside = System.IO.Path.ChangeExtension(Path, $".damaged-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Move(Path, aside);
            LoadWarning = $"The schedule file could not be read. It was kept as {System.IO.Path.GetFileName(aside)}.";
            return new ScheduleData();
        }
    }

    /// <summary>Reads a file the user picked without touching it; null with a reason when it isn't a schedule.</summary>
    public static ScheduleData? TryRead(string path, out string? problem)
    {
        problem = null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var looksRight = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.EnumerateObject().Any(p =>
                    p.Name.Equals(nameof(ScheduleData.Sessions), StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Equals(nameof(ScheduleData.Courses), StringComparison.OrdinalIgnoreCase));
            if (!looksRight)
            {
                problem = "That doesn't look like a Class Schedule file.";
                return null;
            }
            return document.RootElement.Deserialize<ScheduleData>(Options) ?? new ScheduleData();
        }
        catch (JsonException)
        {
            problem = "That file isn't valid JSON, so it was left alone.";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = ex.Message;
            return null;
        }
    }

    public void Save(ScheduleData data)
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
        File.Move(temp, Path, overwrite: true);
    }
}
