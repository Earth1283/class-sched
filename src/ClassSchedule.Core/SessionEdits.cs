namespace ClassSchedule.Core;

/// <summary>How far a change to one meeting of a class reaches when the class meets on several days.</summary>
public enum EditScope
{
    AllDays,
    ThisDay
}

/// <summary>Moving and resizing classes, the way dragging them in the week grid does.</summary>
public static class SessionEdits
{
    public const int MinMinutes = 15;

    /// <summary>
    /// Gives the meeting on <paramref name="fromDay"/> a new day and time, and returns a way to take it back, or null when nothing changes
    /// or the times don't make sense. A time change can reach every day of the class; a move to another day only ever moves that one
    /// day, so a class that meets Mon/Wed/Fri and has Wednesday dragged to Thursday becomes a Mon/Fri class plus a Thursday class.
    /// </summary>
    public static Action? Reschedule(ScheduleData data, Guid sessionId, DayOfWeek fromDay, DayOfWeek toDay, TimeSpan start, TimeSpan end, EditScope scope)
    {
        var session = data.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null || !session.Days.Contains(fromDay)) return null;
        if (start < TimeSpan.Zero || end > TimeSpan.FromHours(24) || end - start < TimeSpan.FromMinutes(MinMinutes)) return null;
        if (toDay == fromDay && start == session.Start && end == session.End) return null;

        var days = session.Days.ToList();
        var oldStart = session.Start;
        var oldEnd = session.End;

        if (session.Days.Count == 1 || (scope == EditScope.AllDays && toDay == fromDay))
        {
            session.Days = session.Days.Count == 1 ? new List<DayOfWeek> { toDay } : session.Days;
            session.Start = start;
            session.End = end;
            return () =>
            {
                session.Days = days;
                session.Start = oldStart;
                session.End = oldEnd;
            };
        }

        var split = new ClassSession
        {
            CourseId = session.CourseId, Kind = session.Kind, Location = session.Location,
            Days = new List<DayOfWeek> { toDay }, Start = start, End = end
        };
        session.Days = days.Where(d => d != fromDay).ToList();
        data.Sessions.Insert(data.Sessions.IndexOf(session) + 1, split);
        return () =>
        {
            data.Sessions.Remove(split);
            session.Days = days;
        };
    }
}
