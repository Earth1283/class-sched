namespace ClassSchedule.Core;

public enum DayKind
{
    /// <summary>A normal day of the term.</summary>
    InTerm,
    BeforeTerm,
    AfterTerm,
    Break
}

/// <summary>What a date is, as far as the term goes, and the short label to show for it when classes don't run.</summary>
public readonly record struct DayStatus(DayKind Kind, string Label)
{
    public bool IsOff => Kind != DayKind.InTerm;
}

/// <summary>Which dates fall inside the term, and which of those are breaks.</summary>
public static class TermCalendar
{
    public static DayStatus Of(Settings s, DateTime date)
    {
        var day = date.Date;
        if (day < s.TermStart.Date) return new DayStatus(DayKind.BeforeTerm, "Before term");
        if (day > s.TermEnd.Date) return new DayStatus(DayKind.AfterTerm, "Term ended");
        return BreakOn(s, day) is { } found ? new DayStatus(DayKind.Break, found.Name) : new DayStatus(DayKind.InTerm, "");
    }

    public static TermBreak? BreakOn(Settings s, DateTime date) => s.Breaks.FirstOrDefault(b => b.Covers(date));

    /// <summary>Whether classes meet on this date: not on a break, and inside the term unless the setting says otherwise.</summary>
    public static bool ClassesRun(Settings s, DateTime date)
    {
        var status = Of(s, date);
        return status.Kind == DayKind.InTerm || (s.ShowOutsideTerm && status.Kind != DayKind.Break);
    }

    /// <summary>A label for a run of days when none of them has classes, such as “Fall break”, or an empty string when any does.</summary>
    public static string OffLabel(Settings s, IEnumerable<DateTime> dates)
    {
        var labels = dates.Select(d => ClassesRun(s, d) ? "" : Of(s, d).Label).ToList();
        if (labels.Count == 0 || labels.Any(l => l.Length == 0)) return "";
        return labels.Distinct().Count() == 1 ? labels[0] : "No classes";
    }
}
