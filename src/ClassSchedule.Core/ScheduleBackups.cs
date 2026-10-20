namespace ClassSchedule.Core;

/// <summary>Dated copies of the schedule file beside it, newest kept.</summary>
public static class ScheduleBackups
{
    public static string? CreateAndPrune(string path, int keep, DateTime now)
    {
        if (!File.Exists(path)) return null;

        var dir = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var copy = Path.Combine(dir, $"{name}.backup-{now:yyyyMMdd-HHmmss}.json");
        if (File.Exists(copy)) return null;
        File.Copy(path, copy);

        var old = Directory.GetFiles(dir, $"{name}.backup-*.json").OrderByDescending(f => f, StringComparer.Ordinal).Skip(Math.Max(1, keep));
        foreach (var file in old)
        {
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return copy;
    }
}
