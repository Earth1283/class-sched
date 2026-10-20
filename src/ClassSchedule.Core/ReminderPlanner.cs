namespace ClassSchedule.Core;

public sealed record Reminder(string Key, string Title, string Body);

/// <summary>Decides which reminders to show now. Call it on a timer; each reminder is returned once.</summary>
public static class ReminderPlanner
{
    /// <param name="sent">Keys already shown. New keys are added to it.</param>
    public static IReadOnlyList<Reminder> Due(ScheduleData data, DateTime now, ISet<string> sent)
    {
        var result = new List<Reminder>();
        if (!data.Settings.RemindersEnabled) return result;

        var lead = TimeSpan.FromMinutes(Math.Max(1, data.Settings.ReminderMinutes));

        foreach (var p in ScheduleQueries.SessionsOn(data, now.DayOfWeek))
        {
            var start = now.Date + p.Session.Start;
            var until = start - now;
            if (until <= TimeSpan.Zero || until > lead) continue;

            var key = $"class:{p.Session.Id}:{now:yyyyMMdd}";
            if (!sent.Add(key)) continue;

            var where = string.IsNullOrWhiteSpace(p.Session.Location) ? "" : $" · {p.Session.Location}";
            result.Add(new Reminder(key,
                $"{p.Course.Code} {p.Session.Kind.ToLowerInvariant()} starts in {TimeText.Until(until)}",
                $"{TimeText.Format(p.Session.Start)}{where}"));
        }

        if (data.Settings.TaskReminders)
        {
            foreach (var t in data.Tasks)
            {
                if (t.Done) continue;
                foreach (var (tag, start) in TaskWindows(data.Settings.TaskReminderWhen, t.Due))
                {
                    if (now < start || now >= t.Due) continue;

                    var key = $"task:{t.Id}:{tag}";
                    if (!sent.Add(key)) continue;

                    var until = t.Due - now;
                    var course = data.FindCourse(t.CourseId)?.Code ?? "";
                    result.Add(new Reminder(key,
                        $"{t.Kind}: {t.Title} is due in {TimeText.Until(until)}",
                        $"{course} · {t.Due:ddd MMM d}, {TimeText.Format(t.Due.TimeOfDay)}".Trim(' ', '·')));
                }
            }
        }

        return result;
    }

    /// <summary>When each reminder for a task starts to apply; it stays due until the deadline, and each is shown once.</summary>
    private static IEnumerable<(string Tag, DateTime Start)> TaskWindows(string when, DateTime due)
    {
        switch (when)
        {
            case "HourBefore":
                yield return ("hour", due.AddHours(-1));
                break;
            case "Morning":
                yield return ("morning", due.Date.AddHours(8) < due ? due.Date.AddHours(8) : due.AddHours(-1));
                break;
            case "DayAndHour":
                yield return ("day", due.AddHours(-24));
                yield return ("hour", due.AddHours(-1));
                break;
            default:
                yield return ("day", due.AddHours(-24));
                break;
        }
    }
}
