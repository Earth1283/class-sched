using System.Text.RegularExpressions;

namespace ClassSchedule.Core;

/// <summary>Parsing and formatting of class times ("9:00 AM", "13:15", "9am").</summary>
public static class TimeText
{
    private static readonly Regex Pattern = new(
        @"^\s*(\d{1,2})(?::(\d{2}))?\s*(?:([ap])(?:\.?m\.?)?)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var m = Pattern.Match(text);
        if (!m.Success) return false;

        var hour = int.Parse(m.Groups[1].Value);
        var minute = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0;
        if (minute > 59) return false;

        if (m.Groups[3].Success)
        {
            if (hour < 1 || hour > 12) return false;
            var pm = char.ToLowerInvariant(m.Groups[3].Value[0]) == 'p';
            hour = hour % 12 + (pm ? 12 : 0);
        }
        else if (hour > 23) return false;

        time = new TimeSpan(hour, minute, 0);
        return true;
    }

    /// <summary>"9:00 AM", or "9:00" without the period.</summary>
    /// <summary>Show "13:00" instead of "1:00 PM".</summary>
    public static bool Use24Hour { get; set; }

    /// <summary>The label on the grid's hour lines: "9 AM", or "09:00" in 24-hour mode.</summary>
    public static string HourLabel(int hour) =>
        Use24Hour ? $"{hour % 24:00}:00" : $"{(hour + 11) % 12 + 1} {(hour % 24 >= 12 ? "PM" : "AM")}";

    public static string Format(TimeSpan t, bool withPeriod = true)
    {
        if (Use24Hour) return $"{t.Hours:00}:{t.Minutes:00}";
        var hour12 = (t.Hours + 11) % 12 + 1;
        var text = $"{hour12}:{t.Minutes:00}";
        return withPeriod ? $"{text} {Period(t)}" : text;
    }

    /// <summary>"9:00–9:50 AM" when both ends share a period, else "11:00 AM–12:15 PM".</summary>
    public static string Range(TimeSpan start, TimeSpan end) =>
        Use24Hour ? $"{Format(start)}–{Format(end)}"
        : Period(start) == Period(end)
            ? $"{Format(start, false)}–{Format(end)}"
            : $"{Format(start)}–{Format(end)}";

    /// <summary>"in 2h 8m", "in 45m".</summary>
    public static string Until(TimeSpan span)
    {
        var minutes = (int)Math.Ceiling(span.TotalMinutes);
        if (minutes < 60) return $"{minutes}m";
        var h = minutes / 60;
        var m = minutes % 60;
        if (h >= 24) return $"{h / 24}d {h % 24}h";
        return m == 0 ? $"{h}h" : $"{h}h {m}m";
    }

    private static string Period(TimeSpan t) => t.Hours >= 12 ? "PM" : "AM";
}
