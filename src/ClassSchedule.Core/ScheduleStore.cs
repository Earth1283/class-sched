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

    private const int Attempts = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(120);

    private bool unreadable;

    /// <summary>Set when the last Load had to set a damaged file aside or couldn't read the file.</summary>
    public string? LoadWarning { get; private set; }

    public static string DefaultPath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClassSchedule", "schedule.json");

    public ScheduleData Load(DateTime today)
    {
        LoadWarning = null;
        unreadable = false;
        if (!File.Exists(Path)) return new ScheduleData();

        string json;
        try { json = ReadWithRetry(Path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file is there but locked or unreachable. Starting empty is fine, saving over it is not.
            unreadable = true;
            LoadWarning = $"The schedule file could not be read ({ex.Message.TrimEnd('.')}), so changes won't be saved until it can be.";
            return new ScheduleData();
        }

        try
        {
            return Parse(json);
        }
        catch (JsonException)
        {
            return RecoverFromDamage();
        }
    }

    /// <summary>Keeps the damaged file, then tries the newest backup that still reads.</summary>
    private ScheduleData RecoverFromDamage()
    {
        string? aside = System.IO.Path.ChangeExtension(Path, $".damaged-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        try { File.Move(Path, aside); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Copy(Path, aside, overwrite: true); }
            catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException)
            {
                // Can't keep a copy, so leave the file alone rather than overwrite it.
                unreadable = true;
                LoadWarning = "The schedule file is damaged and could not be set aside, so changes won't be saved.";
                return new ScheduleData();
            }
        }

        var kept = System.IO.Path.GetFileName(aside);
        foreach (var backup in BackupFiles())
        {
            var restored = TryRead(backup, out _);
            if (restored is null) continue;
            LoadWarning = $"The schedule file could not be read. It was kept as {kept}, and your schedule was restored from {System.IO.Path.GetFileName(backup)}.";
            return restored;
        }

        LoadWarning = $"The schedule file could not be read. It was kept as {kept}.";
        return new ScheduleData();
    }

    /// <summary>Backups beside the schedule file, newest first.</summary>
    private IEnumerable<string> BackupFiles()
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return Array.Empty<string>();
        var name = System.IO.Path.GetFileNameWithoutExtension(Path);
        try { return Directory.GetFiles(dir, $"{name}.backup-*.json").OrderByDescending(f => f, StringComparer.Ordinal).ToList(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    /// <summary>Reads text even while a sync client or virus scanner has the file open, trying a few times before giving up.</summary>
    private static string ReadWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < Attempts)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }

    /// <summary>Turns file text into data, fills in anything the file left null, and throws JsonException when it isn't a schedule.</summary>
    private static ScheduleData Parse(string json)
    {
        var data = JsonSerializer.Deserialize<ScheduleData>(json, Options);
        return data is null ? throw new JsonException("The file is empty.") : Repair(data);
    }

    /// <summary>A hand-edited or half-written file can say null for a list; the rest of the app assumes none are.</summary>
    private static ScheduleData Repair(ScheduleData data)
    {
        data.Settings ??= new Settings();
        data.Settings.Breaks ??= new List<TermBreak>();
        data.Courses ??= new List<Course>();
        data.Sessions ??= new List<ClassSession>();
        data.Tasks ??= new List<TaskItem>();
        data.Courses.RemoveAll(c => c is null);
        data.Sessions.RemoveAll(s => s is null);
        data.Tasks.RemoveAll(t => t is null);
        foreach (var session in data.Sessions) session.Days ??= new List<DayOfWeek>();
        return data;
    }

    /// <summary>Reads a file the user picked without touching it; null with a reason when it isn't a schedule.</summary>
    public static ScheduleData? TryRead(string path, out string? problem)
    {
        problem = null;
        try
        {
            using var document = JsonDocument.Parse(ReadWithRetry(path));
            var looksRight = document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.EnumerateObject().Any(p =>
                    p.Name.Equals(nameof(ScheduleData.Sessions), StringComparison.OrdinalIgnoreCase) ||
                    p.Name.Equals(nameof(ScheduleData.Courses), StringComparison.OrdinalIgnoreCase));
            if (!looksRight)
            {
                problem = "That doesn't look like a Class Schedule file.";
                return null;
            }
            return Repair(document.RootElement.Deserialize<ScheduleData>(Options) ?? new ScheduleData());
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
        if (unreadable)
            throw new IOException("The schedule file on disk could not be read, so it was left as it is. Close whatever is using it and restart the app.");

        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, Options));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(temp, Path, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < Attempts)
            {
                Thread.Sleep(RetryDelay);
            }
        }
    }
}
