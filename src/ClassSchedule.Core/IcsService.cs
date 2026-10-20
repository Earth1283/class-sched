using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClassSchedule.Core;

public static class IcsService
{
    private const string LocalFormat = "yyyyMMdd'T'HHmmss";

    public static string Export(ScheduleData data, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        void Line(string text) => sb.Append(Fold(text)).Append("\r\n");

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//ClassSchedule//EN");
        Line("CALSCALE:GREGORIAN");
        Line("X-WR-CALNAME:" + Escape(data.Settings.TermName));

        var stamp = nowUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var termStart = data.Settings.TermStart.Date;
        var termEnd = data.Settings.TermEnd.Date;

        foreach (var s in data.Sessions)
        {
            var course = data.FindCourse(s.CourseId);
            if (course is null || s.Days.Count == 0) continue;

            var first = Enumerable.Range(0, 7).Select(i => termStart.AddDays(i)).First(d => s.Days.Contains(d.DayOfWeek));
            var summary = $"{course.Code} - {course.Name} ({s.Kind})";

            Line("BEGIN:VEVENT");
            Line($"UID:{s.Id}@classschedule");
            Line($"DTSTAMP:{stamp}");
            Line("DTSTART:" + (first + s.Start).ToString(LocalFormat, CultureInfo.InvariantCulture));
            Line("DTEND:" + (first + s.End).ToString(LocalFormat, CultureInfo.InvariantCulture));
            Line("RRULE:FREQ=WEEKLY;BYDAY=" + string.Join(",", s.Days.OrderBy(DayParser.Order).Select(DayParser.Iso))
                 + ";UNTIL=" + termEnd.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "T235959");
            Line("SUMMARY:" + Escape(summary));
            if (s.Location.Length > 0) Line("LOCATION:" + Escape(s.Location));
            if (course.Instructor.Length > 0) Line("DESCRIPTION:" + Escape(course.Instructor));
            Line("END:VEVENT");
        }

        foreach (var t in data.Tasks)
        {
            var course = data.FindCourse(t.CourseId);
            Line("BEGIN:VTODO");
            Line($"UID:{t.Id}@classschedule");
            Line($"DTSTAMP:{stamp}");
            Line("DUE:" + t.Due.ToString(LocalFormat, CultureInfo.InvariantCulture));
            Line("SUMMARY:" + Escape($"{course?.Code}: {t.Title}".TrimStart(':', ' ')));
            Line("CATEGORIES:" + t.Kind);
            Line("STATUS:" + (t.Done ? "COMPLETED" : "NEEDS-ACTION"));
            Line("END:VTODO");
        }

        Line("END:VCALENDAR");
        return sb.ToString();
    }

    public static ImportResult Import(string text, ScheduleData data)
    {
        var result = new ImportResult();
        var lines = Unfold(text);
        if (!lines.Any(l => l.Equals("BEGIN:VCALENDAR", StringComparison.OrdinalIgnoreCase)))
        {
            result.Errors.Add("This is not a calendar (.ics) file.");
            return result;
        }

        List<(string Name, string Params, string Value)>? block = null;
        string? kind = null;
        foreach (var raw in lines)
        {
            if (raw.StartsWith("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("BEGIN:VTODO", StringComparison.OrdinalIgnoreCase))
            {
                block = new();
                kind = raw[6..].ToUpperInvariant();
                continue;
            }
            if (raw.StartsWith("END:VEVENT", StringComparison.OrdinalIgnoreCase) || raw.StartsWith("END:VTODO", StringComparison.OrdinalIgnoreCase))
            {
                if (block is not null)
                {
                    if (kind == "VEVENT") ImportEvent(block, data, result);
                    else ImportTodo(block, data, result);
                }
                block = null;
                continue;
            }
            if (block is null) continue;

            var colon = raw.IndexOf(':');
            if (colon <= 0) continue;
            var head = raw[..colon];
            var semi = head.IndexOf(';');
            var name = (semi < 0 ? head : head[..semi]).ToUpperInvariant();
            var prm = semi < 0 ? "" : head[(semi + 1)..];
            block.Add((name, prm, raw[(colon + 1)..]));
        }
        return result;
    }

    private static void ImportEvent(List<(string Name, string Params, string Value)> props, ScheduleData data, ImportResult result)
    {
        string? Get(string name) => props.FirstOrDefault(p => p.Name == name).Value;

        var summary = Unescape(Get("SUMMARY") ?? "").Trim();
        if (summary.Length == 0) { result.Errors.Add("An event without a title was skipped."); return; }

        var startProp = props.FirstOrDefault(p => p.Name == "DTSTART");
        var endProp = props.FirstOrDefault(p => p.Name == "DTEND");
        if (startProp.Value is null || startProp.Params.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) && !startProp.Value.Contains('T'))
        {
            result.Errors.Add($"\"{summary}\" is an all-day event and was skipped.");
            return;
        }
        if (!TryParseDateTime(startProp.Value, out var start))
        {
            result.Errors.Add($"\"{summary}\" has a start time that could not be read.");
            return;
        }
        var end = endProp.Value is not null && TryParseDateTime(endProp.Value, out var e) ? e : start.AddHours(1);
        if (end.Date != start.Date || end <= start)
        {
            result.Errors.Add($"\"{summary}\" does not start and end on the same day and was skipped.");
            return;
        }

        var days = new List<DayOfWeek>();
        var rule = Get("RRULE");
        if (rule is not null)
        {
            var parts = rule.Split(';').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].ToUpperInvariant(), p => p[1]);
            if (parts.TryGetValue("FREQ", out var freq) && freq.Equals("WEEKLY", StringComparison.OrdinalIgnoreCase) && parts.TryGetValue("BYDAY", out var byDay))
            {
                foreach (var token in byDay.Split(','))
                {
                    var code = Regex.Replace(token, @"^[+-]?\d+", "");
                    if (DayParser.TryParseIso(code, out var d) && !days.Contains(d)) days.Add(d);
                }
            }
        }
        if (days.Count == 0) days.Add(start.DayOfWeek);
        days = days.OrderBy(DayParser.Order).ToList();

        var (code2, name, kind) = SplitSummary(summary);
        var course = data.FindCourseByCode(code2);
        if (course is null)
        {
            course = new Course
            {
                Code = code2,
                Name = name,
                Instructor = Unescape(Get("DESCRIPTION") ?? "").Split('\n')[0].Trim(),
                Color = CoursePalette.ForIndex(data.Courses.Count)
            };
            data.Courses.Add(course);
            result.CoursesAdded++;
        }

        var session = new ClassSession
        {
            CourseId = course.Id,
            Kind = kind,
            Days = days,
            Start = start.TimeOfDay,
            End = end.TimeOfDay,
            Location = Unescape(Get("LOCATION") ?? "").Trim()
        };
        if (CsvImporter.IsDuplicate(data, session)) { result.Skipped++; return; }
        data.Sessions.Add(session);
        result.SessionsAdded++;
    }

    private static void ImportTodo(List<(string Name, string Params, string Value)> props, ScheduleData data, ImportResult result)
    {
        string? Get(string name) => props.FirstOrDefault(p => p.Name == name).Value;

        var summary = Unescape(Get("SUMMARY") ?? "").Trim();
        var colon = summary.IndexOf(':');
        var course = colon > 0 ? data.FindCourseByCode(summary[..colon].Trim()) : null;
        if (course is null || Get("DUE") is not { } due || !TryParseDateTime(due, out var dueAt))
        {
            result.Errors.Add($"The task \"{summary}\" needs a course code before the colon and a due date, so it was skipped.");
            return;
        }

        var title = summary[(colon + 1)..].Trim();
        if (data.Tasks.Any(t => t.CourseId == course.Id && t.Title == title && t.Due == dueAt)) { result.Skipped++; return; }

        data.Tasks.Add(new TaskItem
        {
            CourseId = course.Id,
            Title = title,
            Due = dueAt,
            Kind = (Get("CATEGORIES") ?? "").Contains("Exam", StringComparison.OrdinalIgnoreCase) ? TaskKind.Exam : TaskKind.Assignment,
            Done = string.Equals(Get("STATUS"), "COMPLETED", StringComparison.OrdinalIgnoreCase)
        });
        result.TasksAdded++;
    }

    private static (string Code, string Name, string Kind) SplitSummary(string summary)
    {
        var kind = "Lecture";
        var m = Regex.Match(summary, @"\s*\(([^)]+)\)\s*$");
        if (m.Success)
        {
            kind = m.Groups[1].Value.Trim();
            summary = summary[..m.Index];
        }

        var parts = Regex.Match(summary, @"^([A-Za-z]{2,6}\s?\d{2,4}[A-Za-z]?)\s*[-–:·]\s*(.+)$");
        return parts.Success
            ? (parts.Groups[1].Value.Trim(), parts.Groups[2].Value.Trim(), kind)
            : (summary.Trim(), "", kind);
    }

    private static bool TryParseDateTime(string value, out DateTime result)
    {
        value = value.Trim();
        var utc = value.EndsWith('Z');
        if (utc) value = value[..^1];
        if (!DateTime.TryParseExact(value, LocalFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out result)) return false;
        if (utc) result = DateTime.SpecifyKind(result, DateTimeKind.Utc).ToLocalTime();
        return true;
    }

    private static List<string> Unfold(string text)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t') && lines.Count > 0) lines[^1] += raw[1..];
            else if (raw.Length > 0) lines.Add(raw);
        }
        return lines;
    }

    private static string Fold(string line)
    {
        if (line.Length <= 70) return line;
        var sb = new StringBuilder();
        for (var i = 0; i < line.Length; i += 70)
        {
            if (i > 0) sb.Append("\r\n ");
            sb.Append(line, i, Math.Min(70, line.Length - i));
        }
        return sb.ToString();
    }

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");

    private static string Unescape(string s) =>
        Regex.Replace(s, @"\\([\\;,nN])", m => m.Groups[1].Value is "n" or "N" ? "\n" : m.Groups[1].Value);
}
