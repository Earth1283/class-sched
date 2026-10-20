namespace ClassSchedule.Core;

public sealed class ImportResult
{
    public int CoursesAdded { get; set; }
    public int SessionsAdded { get; set; }
    public int TasksAdded { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; } = new();

    public bool AddedAnything => CoursesAdded + SessionsAdded + TasksAdded > 0;

    public string Summary()
    {
        var parts = new List<string>();
        if (SessionsAdded > 0) parts.Add($"{SessionsAdded} {(SessionsAdded == 1 ? "class" : "classes")}");
        if (CoursesAdded > 0) parts.Add($"{CoursesAdded} new {(CoursesAdded == 1 ? "course" : "courses")}");
        if (TasksAdded > 0) parts.Add($"{TasksAdded} {(TasksAdded == 1 ? "task" : "tasks")}");
        var text = parts.Count == 0 ? "Nothing was imported." : "Imported " + string.Join(", ", parts) + ".";
        if (Skipped > 0) text += $" {Skipped} duplicate{(Skipped == 1 ? "" : "s")} skipped.";
        return text;
    }
}
