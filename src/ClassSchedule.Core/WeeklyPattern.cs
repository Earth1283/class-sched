using System.Text.RegularExpressions;

namespace ClassSchedule.Core;

public sealed record WeeklySlot(DayOfWeek Day, TimeSpan Start, TimeSpan End);

/// <summary>Days that share exactly the same start and end.</summary>
public sealed record WeeklyGroup(IReadOnlyList<DayOfWeek> Days, TimeSpan Start, TimeSpan End)
{
    public override string ToString() => $"{DayParser.Abbreviations(Days)}  {TimeText.Range(Start, End)}";
}

public sealed class WeeklyPatternResult
{
    public List<WeeklySlot> Slots { get; } = new();
    public List<string> Errors { get; } = new();

    public bool IsEmpty => Slots.Count == 0 && Errors.Count == 0;

    public bool Ok => Errors.Count == 0 && Slots.Count > 0;

    /// <summary>One entry per distinct time range, so "Mon 9-9:50, Wed 9-9:50" becomes a single class meeting on both days.</summary>
    public List<WeeklyGroup> Groups() =>
        Slots.GroupBy(s => (s.Start, s.End))
            .Select(g => new WeeklyGroup(g.Select(s => s.Day).Distinct().OrderBy(DayParser.Order).ToList(), g.Key.Start, g.Key.End))
            .OrderBy(g => DayParser.Order(g.Days[0]))
            .ThenBy(g => g.Start)
            .ToList();
}

/// <summary>
/// Reads a weekly repeating schedule where every day can have its own times, for example
/// "Mon 9:00-9:50, Wed 10:00-10:50, Fri 2-3:15" or "MWF 9-9:50, TTh 11-12:15".
/// A day may list several slots ("Mon 9-9:50 2-3:15"), and times may be written as a start and end pair in any style.
/// </summary>
public static class WeeklyPattern
{
    private static readonly Regex Token = new(
        @"(?<time>(?<![\d:.])(?<h>\d{1,2})(?::(?<m>\d{2}))?(?:\s*(?<ap>[ap])\.?m\b\.?)?(?![\d:]))|(?<word>[A-Za-z]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly HashSet<string> Connectors = new(StringComparer.OrdinalIgnoreCase)
    {
        "to", "and", "until", "till", "from", "at", "on", "every", "weekly", "then", "through"
    };

    public static WeeklyPatternResult Parse(string? text)
    {
        var result = new WeeklyPatternResult();
        if (string.IsNullOrWhiteSpace(text)) return result;

        var days = new List<DayOfWeek>();
        var times = new List<BulkAdd.Clock>();
        var seen = new HashSet<WeeklySlot>();
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Flush()
        {
            if (days.Count == 0 && times.Count == 0) return;
            var label = days.Count == 0 ? "The times" : DayParser.Abbreviations(days);

            if (days.Count == 0) result.Errors.Add("Start each part with a day, like Mon 9:00-9:50.");
            else if (times.Count == 0) result.Errors.Add($"{label} needs a start and an end time.");
            else if (times.Count % 2 != 0) result.Errors.Add($"{label} has a time without a partner. Give every slot a start and an end.");
            else
            {
                for (var i = 0; i < times.Count; i += 2)
                {
                    if (!BulkAdd.TryResolve(times[i], times[i + 1], out var start, out var end))
                    {
                        result.Errors.Add($"{label}: {Describe(times[i])} to {Describe(times[i + 1])} isn't a valid time range.");
                        continue;
                    }
                    foreach (var day in days)
                    {
                        var slot = new WeeklySlot(day, start, end);
                        if (seen.Add(slot)) result.Slots.Add(slot);
                    }
                }
            }
            days = new List<DayOfWeek>();
            times = new List<BulkAdd.Clock>();
        }

        foreach (Match match in Token.Matches(text))
        {
            if (match.Groups["time"].Success)
            {
                var pm = match.Groups["ap"].Success ? char.ToLowerInvariant(match.Groups["ap"].Value[0]) == 'p' : (bool?)null;
                var minute = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value) : 0;
                times.Add(new BulkAdd.Clock(int.Parse(match.Groups["h"].Value), minute, pm));
                continue;
            }

            var word = match.Groups["word"].Value;
            if (Connectors.Contains(word)) continue;
            if (!DayParser.TryParse(word, out var found))
            {
                if (unknown.Add(word)) result.Errors.Add($"“{word}” isn't a day. Try Mon, Tue, Wed, Thu, Fri, Sat or Sun.");
                continue;
            }

            if (times.Count > 0) Flush();
            days = days.Union(found).OrderBy(DayParser.Order).ToList();
        }
        Flush();

        result.Slots.Sort((a, b) => DayParser.Order(a.Day) != DayParser.Order(b.Day)
            ? DayParser.Order(a.Day).CompareTo(DayParser.Order(b.Day))
            : a.Start.CompareTo(b.Start));
        return result;
    }

    /// <summary>Text for filling the box from a single day list and time range.</summary>
    public static string Write(IEnumerable<DayOfWeek> days, TimeSpan start, TimeSpan end) =>
        $"{string.Join(" ", days.OrderBy(DayParser.Order).Select(DayParser.Short))} {TimeText.Format(start)} - {TimeText.Format(end)}";

    private static string Describe(BulkAdd.Clock c) =>
        $"{c.Hour}:{c.Minute:00}{(c.Pm is null ? "" : c.Pm.Value ? " PM" : " AM")}";
}
