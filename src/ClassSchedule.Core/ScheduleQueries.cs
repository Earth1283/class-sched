namespace ClassSchedule.Core;

public sealed record PlacedSession(ClassSession Session, Course Course);

public enum AgendaStatus
{
    Done,
    Now,
    Upcoming
}

public sealed record AgendaItem(PlacedSession Item, AgendaStatus Status);

public sealed record NowInfo(PlacedSession? Current, PlacedSession? Next, DateTime? NextStart);

/// <summary>Read-only questions the window asks about a schedule.</summary>
public static class ScheduleQueries
{
    public const int DefaultStartHour = 8;
    public const int DefaultEndHour = 17;

    public static DateTime WeekStart(DateTime date) => date.Date.AddDays(-DayParser.Order(date.DayOfWeek));

    private static IEnumerable<PlacedSession> Placed(ScheduleData data, bool onlyVisible)
    {
        foreach (var s in data.Sessions)
        {
            var c = data.FindCourse(s.CourseId);
            if (c is null || (onlyVisible && !c.Visible)) continue;
            yield return new PlacedSession(s, c);
        }
    }

    /// <summary>Monday to Friday, plus Saturday and Sunday as the weekends setting says (by default, when a class meets then).</summary>
    public static IReadOnlyList<DayOfWeek> VisibleDays(ScheduleData data)
    {
        var days = new List<DayOfWeek>
        {
            DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday
        };
        foreach (var weekend in new[] { DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            var show = data.Settings.Weekends switch
            {
                "Always" => true,
                "Never" => false,
                _ => data.Sessions.Any(s => s.Days.Contains(weekend))
            };
            if (show) days.Add(weekend);
        }
        return days.OrderBy(DayParser.Order).ToList();
    }

    public static IReadOnlyList<PlacedSession> SessionsOn(ScheduleData data, DayOfWeek day, bool onlyVisible = true) =>
        Placed(data, onlyVisible).Where(p => p.Session.Days.Contains(day))
            .OrderBy(p => p.Session.Start).ThenBy(p => p.Session.End).ToList();

    /// <summary>The classes that meet on one date: none on a break, and none outside the term unless the setting shows them.</summary>
    public static IReadOnlyList<PlacedSession> SessionsOnDate(ScheduleData data, DateTime date, bool onlyVisible = true) =>
        TermCalendar.ClassesRun(data.Settings, date) ? SessionsOn(data, date.DayOfWeek, onlyVisible) : Array.Empty<PlacedSession>();

    /// <summary>Whole hours the grid should show: 8 AM to 5 PM (or the range chosen in settings), widened to fit every class.</summary>
    public static (int StartHour, int EndHour) HourRange(ScheduleData data)
    {
        var start = data.Settings.HoursAuto ? DefaultStartHour : Math.Clamp(data.Settings.HourStart, 0, 22);
        var end = data.Settings.HoursAuto ? DefaultEndHour : Math.Clamp(data.Settings.HourEnd, start + 1, 24);
        foreach (var s in data.Sessions)
        {
            start = Math.Min(start, (int)Math.Floor(s.Start.TotalHours));
            end = Math.Max(end, (int)Math.Ceiling(s.End.TotalHours));
        }
        return (Math.Clamp(start, 0, 23), Math.Clamp(end, start + 1, 24));
    }

    /// <summary>How far ahead “next class” looks, which is long enough to get past a week-long break.</summary>
    private const int NextClassLookahead = 60;

    public static NowInfo Now(ScheduleData data, DateTime now)
    {
        var today = SessionsOnDate(data, now, onlyVisible: false);
        var current = today.FirstOrDefault(p => p.Session.Start <= now.TimeOfDay && now.TimeOfDay < p.Session.End);

        for (var offset = 0; offset <= NextClassLookahead; offset++)
        {
            var date = now.Date.AddDays(offset);
            var sessions = offset == 0 ? today : SessionsOnDate(data, date, onlyVisible: false);
            var next = sessions.FirstOrDefault(p => date + p.Session.Start > now);
            if (next is not null) return new NowInfo(current, next, date + next.Session.Start);
        }
        return new NowInfo(current, null, null);
    }

    public static IReadOnlyList<AgendaItem> Agenda(ScheduleData data, DateTime now) =>
        SessionsOnDate(data, now, onlyVisible: false)
            .Select(p => new AgendaItem(p,
                p.Session.End <= now.TimeOfDay ? AgendaStatus.Done
                : p.Session.Start <= now.TimeOfDay ? AgendaStatus.Now
                : AgendaStatus.Upcoming))
            .ToList();

    /// <summary>Unfinished work that is overdue or due within the next <paramref name="days"/> days.</summary>
    public static IReadOnlyList<TaskItem> Upcoming(ScheduleData data, DateTime now, int days = 14) =>
        data.Tasks.Where(t => !t.Done && t.Due <= now.AddDays(days)).OrderBy(t => t.Due).ToList();

    /// <summary>Unfinished work due from the start of <paramref name="first"/> through the end of <paramref name="last"/>, soonest first.</summary>
    public static IReadOnlyList<TaskItem> DueBetween(ScheduleData data, DateTime first, DateTime last) =>
        data.Tasks.Where(t => !t.Done && t.Due >= first.Date && t.Due < last.Date.AddDays(1)).OrderBy(t => t.Due).ToList();

    /// <summary>The earliest unfinished task due on a later day than today; today's are listed with the day's classes.</summary>
    public static TaskItem? NextDue(ScheduleData data, DateTime now) =>
        data.Tasks.Where(t => !t.Done && t.Due.Date > now.Date).OrderBy(t => t.Due).FirstOrDefault();

    public static double WeeklyHours(ScheduleData data, Guid courseId) =>
        data.Sessions.Where(s => s.CourseId == courseId).Sum(s => s.Days.Count * s.Duration.TotalHours);

    public static string Stats(ScheduleData data)
    {
        var hours = data.Courses.Sum(c => WeeklyHours(data, c.Id));
        var credits = data.Courses.Sum(c => c.Credits);
        return $"{data.Courses.Count} {(data.Courses.Count == 1 ? "course" : "courses")} · {credits} credits · {hours:0.#} h / week";
    }
}
