using System.Text.RegularExpressions;

namespace ClassSchedule.Core;

public enum BulkStatus
{
    Ok,
    Assumed,
    Duplicate,
    Error
}

public sealed class BulkRow
{
    public int Line { get; init; }
    public string Source { get; init; } = "";
    public BulkStatus Status { get; set; } = BulkStatus.Ok;
    public string Message { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Lecture";
    public string Location { get; set; } = "";
    public string Instructor { get; set; } = "";
    public List<DayOfWeek> Days { get; set; } = new();
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }

    public bool CanAdd => Status is BulkStatus.Ok or BulkStatus.Assumed;
}

/// <summary>Turns pasted text, one class per line, into classes. Accepts sentences, spreadsheet rows and CSV.</summary>
public static class BulkAdd
{
    public const string Example =
        "CS 201 Data Structures  MWF 9:00-9:50  Room 204  Dr. Rivera\n" +
        "MATH 240, Tue/Thu, 10:30-11:45 AM, Hall B, Prof. Okafor\n" +
        "PHYS 110 Mechanics  M W  1-2:15pm  Sci 310\n" +
        "PHYS 110 Lab  Fri  2:00-4:50 PM  Sci 112\n" +
        "ENG 102 Writing  TTh 3:30pm-4:45pm  Library 2";

    private const string KindWords = "lecture|laboratory|lab|discussion|seminar|recitation|studio|tutorial|workshop";
    private static int AssumedMinutes => ClassDefaults.Minutes;

    private static readonly Regex Range = new(
        @"(?<![\d:.])(\d{1,2})(?::(\d{2}))?(?:\s*([ap])\.?m\b\.?)?\s*(?:-|to|until|till)\s*(\d{1,2})(?::(\d{2}))?(?:\s*([ap])\.?m\b\.?)?(?![\d:])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Single = new(
        @"(?<![\d:.])(\d{1,2})(?::(\d{2}))?(?:\s*([ap])\.?m\b\.?)?(?![\d:])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ClockCell = new(
        @"^\s*(\d{1,2})(?::(\d{2}))?(?:\s*([ap])\.?m\.?)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DayWord = new(
        @"^(?:mon(?:day)?|tue(?:s(?:day)?)?|wed(?:nesday)?|thu(?:r(?:s(?:day)?)?)?|fri(?:day)?|sat(?:urday)?|sun(?:day)?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DayCodes = new(@"^(?:Th|Tu|Sa|Su|[MTWRFSU])+$", RegexOptions.Compiled);
    private static readonly Regex CodeAtStart = new(@"^\s*([A-Za-z]{2,6}\s?\d{2,4}[A-Za-z]?)\b\s*[-–—:·]?\s*(.*)$", RegexOptions.Compiled);
    private static readonly Regex Parenthesised = new($@"\(\s*({KindWords})\s*\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TrailingKind = new($@"\b({KindWords})\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ExactKind = new($@"^({KindWords})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex InstructorTitle = new(@"^(?:prof(?:essor)?|dr|mr|ms|mrs|mx)\b\.?\s", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PlaceWord = new(@"\b(?:room|rm|hall|bldg|building|lab|center|centre|library|auditorium|theat(?:er|re)|gym|annex|online|zoom|remote)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PostSplit = new(@"\s+[-–—·|]\s+|[|;,]|\s+(?=(?:prof|professor|dr|mr|ms|mrs)\b\.?\s)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Punctuation = new(@"^[\W_]+$", RegexOptions.Compiled);

    internal readonly record struct Clock(int Hour, int Minute, bool? Pm);

    private sealed class Chunk
    {
        public List<DayOfWeek>? Days;
        public TimeSpan? Start;
        public TimeSpan? End;
        public bool EndAssumed;
        public string Pre = "";
        public string Post = "";
    }

    private sealed record Header(int Code, int Name, int Days, int Start, int End, int Location, int Instructor, int Kind);

    public static List<BulkRow> Parse(string text, ScheduleData? existing = null)
    {
        var rows = new List<BulkRow>();
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var first = Array.FindIndex(lines, IsContent);
        if (first < 0) return rows;

        var header = ReadHeader(lines[first]);
        if (header is not null)
        {
            var table = CsvReader.Parse(string.Join("\n", lines.Skip(first)));
            foreach (var r in table.Skip(1)) rows.Add(FromHeaderRow(header, r.Cells, r.Line + first));
        }
        else
        {
            for (var i = first; i < lines.Length; i++)
            {
                if (IsContent(lines[i])) rows.Add(FromLine(lines[i], i + 1));
            }
        }

        MarkDuplicates(rows, existing);
        return rows;
    }

    public static ImportResult Apply(IEnumerable<BulkRow> rows, ScheduleData data)
    {
        var result = new ImportResult();
        foreach (var row in rows)
        {
            if (row.Status == BulkStatus.Duplicate) { result.Skipped++; continue; }
            if (!row.CanAdd) continue;

            var code = row.Code.Length > 0 ? row.Code : row.Name;
            var course = data.FindCourseByCode(code) ?? (row.Name.Length > 0 ? data.FindCourseByName(row.Name) : null);
            if (course is null)
            {
                course = new Course
                {
                    Code = code,
                    Name = row.Name,
                    Instructor = row.Instructor,
                    Color = CoursePalette.ForIndex(data.Courses.Count)
                };
                data.Courses.Add(course);
                result.CoursesAdded++;
            }
            else
            {
                if (course.Name.Length == 0) course.Name = row.Name;
                if (course.Instructor.Length == 0) course.Instructor = row.Instructor;
            }

            var session = new ClassSession
            {
                CourseId = course.Id,
                Kind = row.Kind,
                Days = row.Days.ToList(),
                Start = row.Start,
                End = row.End,
                Location = row.Location
            };
            if (CsvImporter.IsDuplicate(data, session)) { result.Skipped++; continue; }
            data.Sessions.Add(session);
            result.SessionsAdded++;
        }
        return result;
    }

    private static bool IsContent(string line)
    {
        var t = line.Trim();
        return t.Length > 0 && !t.StartsWith('#') && !t.StartsWith("//");
    }

    private static Header? ReadHeader(string line)
    {
        var parsed = CsvReader.Parse(line);
        if (parsed.Count == 0 || parsed[0].Cells.Length < 3) return null;

        var names = parsed[0].Cells.Select(c => c.Trim().ToLowerInvariant()).ToList();
        int Find(string[] options) => names.FindIndex(options.Contains);
        var header = new Header(Find(CsvImporter.CodeNames), Find(CsvImporter.NameNames), Find(CsvImporter.DayNames),
            Find(CsvImporter.StartNames), Find(CsvImporter.EndNames), Find(CsvImporter.LocationNames),
            Find(CsvImporter.InstructorNames), Find(CsvImporter.KindNames));

        var identifies = header.Code >= 0 || header.Name >= 0;
        return identifies && header.Days >= 0 && header.Start >= 0 && header.End >= 0 ? header : null;
    }

    private static BulkRow FromHeaderRow(Header h, string[] cells, int line)
    {
        string Cell(int i) => i >= 0 && i < cells.Length ? cells[i].Trim() : "";
        var row = new BulkRow
        {
            Line = line,
            Source = string.Join(" | ", cells.Select(c => c.Trim()).Where(c => c.Length > 0)),
            Code = NormalizeCode(Cell(h.Code)),
            Name = Cell(h.Name),
            Location = Cell(h.Location),
            Instructor = Cell(h.Instructor)
        };
        if (Cell(h.Kind).Length > 0) row.Kind = Cell(h.Kind);

        var daysText = Cell(h.Days);
        List<DayOfWeek>? days = DayParser.TryParse(daysText, out var parsedDays) ? parsedDays : null;

        TimeSpan? start = null, end = null;
        if (ReadClock(Cell(h.Start)) is { } a && ReadClock(Cell(h.End)) is { } b && TryResolve(a, b, out var s, out var e))
        {
            start = s;
            end = e;
        }
        Finish(row, days, start, end, false);
        return row;
    }

    private static BulkRow FromLine(string line, int number)
    {
        var row = new BulkRow { Line = number, Source = line.Trim() };
        var texts = new List<string>();
        List<DayOfWeek>? days = null;
        TimeSpan? start = null, end = null;
        var assumed = false;
        var loose = new List<Clock>();

        void Take(Chunk chunk)
        {
            start = chunk.Start;
            end = chunk.End;
            assumed = chunk.EndAssumed;
            if (chunk.Days is not null) days = Union(days, chunk.Days);
            if (chunk.Pre.Trim().Length > 0) texts.Add(chunk.Pre);
            texts.AddRange(SplitPost(chunk.Post));
        }

        var cells = SplitCells(line);
        if (cells.Count <= 1)
        {
            var chunk = Extract(line, allowSingle: true);
            if (chunk.Start is not null) Take(chunk);
            else texts.Add(line);
            days ??= chunk.Days;
        }
        else
        {
            foreach (var cell in cells.Select(c => c.Trim()).Where(c => c.Length > 0))
            {
                if (ReadClock(cell, requireMarker: true) is { } clock) { loose.Add(clock); continue; }
                if (DaysOfCell(cell) is { } cellDays) { days = Union(days, cellDays); continue; }

                if (start is null)
                {
                    var chunk = Extract(cell, allowSingle: true);
                    if (chunk.Start is not null) { Take(chunk); continue; }
                }
                texts.Add(cell);
            }

            if (start is null && loose.Count >= 2 && TryResolve(loose[0], loose[1], out var s, out var e))
            {
                start = s;
                end = e;
            }
            else if (start is null && loose.Count == 1 && TrySingle(loose[0], out var only))
            {
                start = only;
                end = only + TimeSpan.FromMinutes(AssumedMinutes);
                assumed = true;
            }
        }

        Assign(row, texts);
        Finish(row, days, start, end, assumed);
        return row;
    }

    private static void Finish(BulkRow row, List<DayOfWeek>? days, TimeSpan? start, TimeSpan? end, bool endAssumed)
    {
        if (row.Code.Length == 0) row.Code = row.Name;
        row.Days = days ?? new List<DayOfWeek>();

        if (row.Code.Length == 0) Fail(row, "No course name or code.");
        else if ((days is null || days.Count == 0) && (start is null || end is null)) Fail(row, "No days or time found. Try: MWF 9:00-9:50.");
        else if (days is null || days.Count == 0) Fail(row, "No days found. Try MWF, TTh or Mon/Wed.");
        else if (start is null || end is null) Fail(row, "No time found. Try 9:00-9:50 AM.");
        else
        {
            row.Start = start.Value;
            row.End = end.Value;
            if (endAssumed)
            {
                row.Status = BulkStatus.Assumed;
                row.Message = $"No end time given, so it runs {AssumedMinutes} minutes.";
            }
        }
    }

    private static void Fail(BulkRow row, string message)
    {
        row.Status = BulkStatus.Error;
        row.Message = message;
    }

    private static void MarkDuplicates(List<BulkRow> rows, ScheduleData? existing)
    {
        var seen = new HashSet<string>();
        foreach (var row in rows.Where(r => r.CanAdd))
        {
            var code = row.Code.Length > 0 ? row.Code : row.Name;
            var key = string.Join("|", code.ToLowerInvariant(), row.Kind.ToLowerInvariant(),
                string.Join(",", row.Days.OrderBy(DayParser.Order)), row.Start, row.End);
            if (!seen.Add(key))
            {
                row.Status = BulkStatus.Duplicate;
                row.Message = "Listed twice.";
                continue;
            }

            var course = existing?.FindCourseByCode(code);
            if (course is null) continue;
            var probe = new ClassSession { CourseId = course.Id, Kind = row.Kind, Days = row.Days, Start = row.Start, End = row.End };
            if (CsvImporter.IsDuplicate(existing!, probe))
            {
                row.Status = BulkStatus.Duplicate;
                row.Message = "Already in your schedule.";
            }
        }
    }

    private static List<string> SplitCells(string line)
    {
        if (line.Contains('\t')) return line.Split('\t').ToList();
        if (line.Contains('|')) return line.Split('|').ToList();
        var delimiters = line.Count(c => c is ',' or ';');
        if (delimiters < 2) return new List<string> { line };
        var parsed = CsvReader.Parse(line);
        return parsed.Count > 0 ? parsed[0].Cells.ToList() : new List<string> { line };
    }

    private static void Assign(BulkRow row, List<string> texts)
    {
        for (var i = 0; i < texts.Count; i++)
        {
            var m = Parenthesised.Match(texts[i]);
            if (!m.Success) continue;
            row.Kind = NormalizeKind(m.Groups[1].Value);
            texts[i] = texts[i].Remove(m.Index, m.Length).Trim();
        }

        texts.RemoveAll(t =>
        {
            var m = ExactKind.Match(t.Trim());
            if (m.Success) row.Kind = NormalizeKind(m.Value);
            return m.Success;
        });

        if (texts.Count > 0)
        {
            var m = TrailingKind.Match(texts[0]);
            if (m.Success && m.Index > 0)
            {
                row.Kind = NormalizeKind(m.Groups[1].Value);
                texts[0] = texts[0][..m.Index].Trim();
            }
        }

        texts.RemoveAll(string.IsNullOrWhiteSpace);
        if (texts.Count == 0) return;

        var head = texts[0];
        texts.RemoveAt(0);
        var code = CodeAtStart.Match(head);
        if (code.Success)
        {
            row.Code = NormalizeCode(code.Groups[1].Value);
            row.Name = code.Groups[2].Value.Trim();
        }
        else row.Name = head.Trim();

        if (row.Name.Length == 0 && row.Code.Length > 0 && texts.Count > 0
            && (texts.Count > 1 || LooksLikeName(texts[0])) && !IsPlace(texts[0]) && !InstructorTitle.IsMatch(texts[0] + " "))
        {
            row.Name = texts[0];
            texts.RemoveAt(0);
        }

        var teacher = texts.FindIndex(t => InstructorTitle.IsMatch(t + " "));
        if (teacher >= 0)
        {
            row.Instructor = Regex.Replace(texts[teacher], @"^with\s+", "", RegexOptions.IgnoreCase);
            texts.RemoveAt(teacher);
        }

        if (texts.Count > 0)
        {
            row.Location = texts[0];
            texts.RemoveAt(0);
        }
        if (row.Instructor.Length == 0 && texts.Count > 0) row.Instructor = texts[0];
    }

    private static bool IsPlace(string text) => PlaceWord.IsMatch(text) || text.Any(char.IsDigit);

    private static bool LooksLikeName(string text) => !IsPlace(text) && text.Trim().Length >= 3;

    private static string NormalizeKind(string word)
    {
        var lower = word.Trim().ToLowerInvariant();
        if (lower == "laboratory") lower = "lab";
        return char.ToUpperInvariant(lower[0]) + lower[1..];
    }

    private static string NormalizeCode(string code) =>
        Regex.Replace(code.Trim(), @"^([A-Za-z]+)\s?(\d+[A-Za-z]?)$", m => $"{m.Groups[1].Value.ToUpperInvariant()} {m.Groups[2].Value.ToUpperInvariant()}");

    private static IEnumerable<string> SplitPost(string post)
    {
        foreach (var piece in PostSplit.Split(post))
        {
            var t = Regex.Replace(piece.Trim(' ', '\t', '-', '·', '@', ':', '(', ')'), @"^(?:in|at)\s+", "", RegexOptions.IgnoreCase);
            if (t.Length > 0) yield return t;
        }
    }

    private static List<DayOfWeek> Union(List<DayOfWeek>? a, List<DayOfWeek> b) =>
        (a ?? new List<DayOfWeek>()).Union(b).OrderBy(DayParser.Order).ToList();

    private static Clock? ReadClock(string text, bool requireMarker = false)
    {
        var m = ClockCell.Match(text);
        if (!m.Success) return null;
        if (requireMarker && !m.Groups[2].Success && !m.Groups[3].Success) return null;
        return ToClock(m, 1);
    }

    private static Clock ToClock(Match m, int group) => new(
        int.Parse(m.Groups[group].Value),
        m.Groups[group + 1].Success ? int.Parse(m.Groups[group + 1].Value) : 0,
        m.Groups[group + 2].Success ? char.ToLowerInvariant(m.Groups[group + 2].Value[0]) == 'p' : null);

    private static bool GuessPm(int hour) => hour is 12 or < 7;

    private static bool TrySingle(Clock c, out TimeSpan time)
    {
        time = default;
        if (c.Minute > 59 || c.Hour > 23) return false;
        if (c.Hour is 0 or > 12)
        {
            if (c.Pm is not null) return false;
            time = new TimeSpan(c.Hour, c.Minute, 0);
            return true;
        }
        var pm = c.Pm ?? GuessPm(c.Hour);
        time = new TimeSpan(c.Hour % 12 + (pm ? 12 : 0), c.Minute, 0);
        return true;
    }

    internal static bool TryResolve(Clock a, Clock b, out TimeSpan start, out TimeSpan end)
    {
        start = end = default;
        if (a.Hour > 23 || b.Hour > 23 || a.Minute > 59 || b.Minute > 59) return false;

        if (a.Hour is 0 or > 12 || b.Hour is 0 or > 12)
        {
            if (a.Pm is not null || b.Pm is not null) return false;
            start = new TimeSpan(a.Hour, a.Minute, 0);
            end = new TimeSpan(b.Hour, b.Minute, 0);
            return end > start;
        }

        var options = (a.Pm, b.Pm) switch
        {
            ({ } pa, { } pb) => new[] { (pa, pb) },
            (null, { } pb) => new[] { (pb, pb), (!pb, pb) },
            ({ } pa, null) => new[] { (pa, pa), (pa, !pa) },
            _ => new[] { (GuessPm(a.Hour), GuessPm(b.Hour)) }
        };

        foreach (var (pa, pb) in options)
        {
            var s = new TimeSpan(a.Hour % 12 + (pa ? 12 : 0), a.Minute, 0);
            var e = new TimeSpan(b.Hour % 12 + (pb ? 12 : 0), b.Minute, 0);
            if (e <= s) continue;
            start = s;
            end = e;
            return true;
        }
        return false;
    }

    private static Chunk Extract(string raw, bool allowSingle)
    {
        var s = raw.Replace('–', '-').Replace('—', '-').Replace('−', '-');
        var chunk = new Chunk { Pre = raw };
        int at = -1, length = 0;

        foreach (Match m in Range.Matches(s))
        {
            if (!TryResolve(ToClock(m, 1), ToClock(m, 4), out var st, out var en)) continue;
            chunk.Start = st;
            chunk.End = en;
            at = m.Index;
            length = m.Length;
            break;
        }

        if (chunk.Start is null && allowSingle)
        {
            foreach (Match m in Single.Matches(s))
            {
                if (!m.Groups[2].Success && !m.Groups[3].Success) continue;
                if (!TrySingle(ToClock(m, 1), out var st)) continue;
                chunk.Start = st;
                chunk.End = st + TimeSpan.FromMinutes(AssumedMinutes);
                chunk.EndAssumed = true;
                at = m.Index;
                length = m.Length;
                break;
            }
        }
        if (chunk.Start is null) return chunk;

        var before = raw[..at];
        var after = raw[(at + length)..];
        var leading = DaysBefore(before);
        var trailing = leading is null ? DaysAfter(after) : null;
        if (leading is { } lead)
        {
            chunk.Days = lead.Days;
            chunk.Pre = TrimJoiners(before[..lead.Start]);
            chunk.Post = after;
        }
        else if (trailing is { } trail)
        {
            chunk.Days = trail.Days;
            chunk.Pre = before;
            chunk.Post = after[trail.End..];
        }
        else
        {
            chunk.Pre = before;
            chunk.Post = after;
        }
        return chunk;
    }

    private static string TrimJoiners(string text) =>
        Regex.Replace(text, @"[\s,;|:@\-–—·]*(?:\b(?:on|every|at|meets)\b)?[\s,;|:@\-–—·]*$", "", RegexOptions.IgnoreCase);

    private static (List<DayOfWeek> Days, int Start)? DaysBefore(string text)
    {
        var tokens = Regex.Matches(text, @"\S+");
        List<DayOfWeek>? days = null;
        var start = -1;
        for (var i = tokens.Count - 1; i >= 0; i--)
        {
            var token = tokens[i].Value;
            if (Punctuation.IsMatch(token)) { if (days is null) continue; break; }
            if (DaysOfToken(token) is not { } found) break;
            days = Union(days, found);
            start = tokens[i].Index;
        }
        return days is null ? null : (days, start);
    }

    private static (List<DayOfWeek> Days, int End)? DaysAfter(string text)
    {
        var tokens = Regex.Matches(text, @"\S+");
        List<DayOfWeek>? days = null;
        var end = -1;
        foreach (Match token in tokens)
        {
            if (Punctuation.IsMatch(token.Value)) { if (days is null) continue; break; }
            if (DaysOfToken(token.Value) is not { } found) break;
            days = Union(days, found);
            end = token.Index + token.Length;
        }
        return days is null ? null : (days, end);
    }

    private static List<DayOfWeek>? DaysOfCell(string cell)
    {
        List<DayOfWeek>? days = null;
        var tokens = cell.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0 || tokens.Length > 7) return null;
        foreach (var token in tokens)
        {
            if (Punctuation.IsMatch(token)) continue;
            if (DaysOfToken(token) is not { } found) return null;
            days = Union(days, found);
        }
        return days;
    }

    private static List<DayOfWeek>? DaysOfToken(string token)
    {
        var parts = Regex.Split(token.Trim('(', ')', ',', ';', ':', '.'), @"[/,&+]").Where(p => p.Length > 0).ToList();
        if (parts.Count == 0) return null;

        List<DayOfWeek>? days = null;
        foreach (var part in parts)
        {
            if (!DayWord.IsMatch(part) && !DayCodes.IsMatch(part)) return null;
            if (!DayParser.TryParse(part, out var found)) return null;
            days = Union(days, found);
        }
        return days;
    }
}
