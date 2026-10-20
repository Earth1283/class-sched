using System.Text;

namespace ClassSchedule.Core;

public sealed record CsvRow(int Line, string[] Cells);

public static class CsvReader
{
    public static List<CsvRow> Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var delimiter = DetectDelimiter(text);
        var rows = new List<CsvRow>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var line = 1;
        var rowLine = 1;

        void EndRow()
        {
            cells.Add(cell.ToString());
            cell.Clear();
            if (cells.Any(c => c.Trim().Length > 0)) rows.Add(new CsvRow(rowLine, cells.ToArray()));
            cells.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else { cell.Append(c); if (c == '\n') line++; }
                continue;
            }

            if (c == '"') quoted = true;
            else if (c == delimiter) { cells.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n') { EndRow(); line++; rowLine = line; }
            else if (c != '\r') cell.Append(c);
        }
        if (cell.Length > 0 || cells.Count > 0) EndRow();
        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOf('\n');
        var first = end < 0 ? text : text[..end];
        var best = ',';
        var count = first.Count(c => c == ',');
        foreach (var candidate in new[] { ';', '\t' })
        {
            var n = first.Count(c => c == candidate);
            if (n > count) { best = candidate; count = n; }
        }
        return best;
    }
}

public static class CsvImporter
{
    internal static readonly string[] CodeNames = { "code", "course", "course code", "id" };
    internal static readonly string[] NameNames = { "name", "title", "course name", "class" };
    internal static readonly string[] DayNames = { "days", "day", "meets" };
    internal static readonly string[] StartNames = { "start", "start time", "begin", "from" };
    internal static readonly string[] EndNames = { "end", "end time", "finish", "to" };
    internal static readonly string[] LocationNames = { "location", "room", "where", "place" };
    internal static readonly string[] InstructorNames = { "instructor", "teacher", "professor", "lecturer" };
    internal static readonly string[] KindNames = { "kind", "type", "section" };
    internal static readonly string[] CreditNames = { "credits", "units", "credit hours" };

    public static ImportResult Import(string text, ScheduleData data)
    {
        var result = new ImportResult();
        var rows = CsvReader.Parse(text);
        if (rows.Count == 0)
        {
            result.Errors.Add("The file is empty.");
            return result;
        }

        var header = rows[0].Cells.Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Find(string[] names) => header.FindIndex(h => names.Contains(h));

        var iCode = Find(CodeNames);
        var iName = Find(NameNames);
        var iDays = Find(DayNames);
        var iStart = Find(StartNames);
        var iEnd = Find(EndNames);
        var iLocation = Find(LocationNames);
        var iInstructor = Find(InstructorNames);
        var iKind = Find(KindNames);
        var iCredits = Find(CreditNames);

        var missing = new List<string>();
        if (iCode < 0 && iName < 0) missing.Add("code or name");
        if (iDays < 0) missing.Add("days");
        if (iStart < 0) missing.Add("start");
        if (iEnd < 0) missing.Add("end");
        if (missing.Count > 0)
        {
            result.Errors.Add($"The first row needs these columns: {string.Join(", ", missing)}.");
            return result;
        }

        string Cell(string[] cells, int index) => index >= 0 && index < cells.Length ? cells[index].Trim() : "";

        foreach (var row in rows.Skip(1))
        {
            var code = Cell(row.Cells, iCode);
            var name = Cell(row.Cells, iName);
            if (code.Length == 0 && name.Length == 0)
            {
                result.Errors.Add($"Line {row.Line}: the row has no course code or name.");
                continue;
            }
            if (!DayParser.TryParse(Cell(row.Cells, iDays), out var days))
            {
                result.Errors.Add($"Line {row.Line}: \"{Cell(row.Cells, iDays)}\" is not a list of days.");
                continue;
            }
            if (!TimeText.TryParse(Cell(row.Cells, iStart), out var start))
            {
                result.Errors.Add($"Line {row.Line}: \"{Cell(row.Cells, iStart)}\" is not a start time.");
                continue;
            }
            if (!TimeText.TryParse(Cell(row.Cells, iEnd), out var end) || end <= start)
            {
                result.Errors.Add($"Line {row.Line}: the end time must come after the start time.");
                continue;
            }

            var course = (code.Length > 0 ? data.FindCourseByCode(code) : null)
                         ?? (name.Length > 0 ? data.FindCourseByName(name) : null);
            if (course is null)
            {
                course = new Course
                {
                    Code = code.Length > 0 ? code : name,
                    Name = name,
                    Instructor = Cell(row.Cells, iInstructor),
                    Color = CoursePalette.ForIndex(data.Courses.Count)
                };
                if (int.TryParse(Cell(row.Cells, iCredits), out var credits) && credits > 0) course.Credits = credits;
                data.Courses.Add(course);
                result.CoursesAdded++;
            }

            var kind = Cell(row.Cells, iKind);
            var session = new ClassSession
            {
                CourseId = course.Id,
                Kind = kind.Length > 0 ? kind : "Lecture",
                Days = days,
                Start = start,
                End = end,
                Location = Cell(row.Cells, iLocation)
            };

            if (IsDuplicate(data, session)) { result.Skipped++; continue; }
            data.Sessions.Add(session);
            result.SessionsAdded++;
        }
        return result;
    }

    internal static bool IsDuplicate(ScheduleData data, ClassSession s) =>
        data.Sessions.Any(o => o.CourseId == s.CourseId && o.Start == s.Start && o.End == s.End
                               && o.Kind.Equals(s.Kind, StringComparison.OrdinalIgnoreCase)
                               && o.Days.OrderBy(d => d).SequenceEqual(s.Days.OrderBy(d => d)));
}
