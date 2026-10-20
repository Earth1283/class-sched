namespace ClassSchedule.Core;

/// <summary>Reads day lists written by people: "MWF", "TTh", "Mon/Wed", "Tuesday, Thursday".</summary>
public static class DayParser
{
    // Longest names first so "thurs" wins over "th" and "t".
    private static readonly (string Name, DayOfWeek Day)[] Names =
    {
        ("monday", DayOfWeek.Monday), ("tuesday", DayOfWeek.Tuesday), ("wednesday", DayOfWeek.Wednesday),
        ("thursday", DayOfWeek.Thursday), ("friday", DayOfWeek.Friday), ("saturday", DayOfWeek.Saturday),
        ("sunday", DayOfWeek.Sunday),
        ("thurs", DayOfWeek.Thursday), ("tues", DayOfWeek.Tuesday), ("thur", DayOfWeek.Thursday),
        ("mon", DayOfWeek.Monday), ("tue", DayOfWeek.Tuesday), ("wed", DayOfWeek.Wednesday),
        ("thu", DayOfWeek.Thursday), ("fri", DayOfWeek.Friday), ("sat", DayOfWeek.Saturday),
        ("sun", DayOfWeek.Sunday),
        ("th", DayOfWeek.Thursday), ("tu", DayOfWeek.Tuesday), ("sa", DayOfWeek.Saturday),
        ("su", DayOfWeek.Sunday), ("mo", DayOfWeek.Monday), ("we", DayOfWeek.Wednesday),
        ("fr", DayOfWeek.Friday),
        ("m", DayOfWeek.Monday), ("t", DayOfWeek.Tuesday), ("w", DayOfWeek.Wednesday),
        ("r", DayOfWeek.Thursday), ("f", DayOfWeek.Friday), ("s", DayOfWeek.Saturday),
        ("u", DayOfWeek.Sunday)
    };

    public static bool TryParse(string? text, out List<DayOfWeek> days)
    {
        days = new List<DayOfWeek>();
        if (string.IsNullOrWhiteSpace(text)) return false;

        var letters = new string(text.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        if (letters.Length == 0) return false;

        var found = new HashSet<DayOfWeek>();
        var i = 0;
        while (i < letters.Length)
        {
            var matched = false;
            foreach (var (name, day) in Names)
            {
                if (!letters.AsSpan(i).StartsWith(name)) continue;
                found.Add(day);
                i += name.Length;
                matched = true;
                break;
            }
            if (!matched) return false;
        }

        days = found.OrderBy(Order).ToList();
        return days.Count > 0;
    }

    /// <summary>The day each week starts on.</summary>
    public static DayOfWeek FirstDay { get; set; } = DayOfWeek.Monday;

    /// <summary>0 for the first day of the week, up to 6 for the last (Monday = 0 unless the week starts on Sunday).</summary>
    public static int Order(DayOfWeek d) => ((int)d - (int)FirstDay + 7) % 7;

    public static string Abbreviations(IEnumerable<DayOfWeek> days) =>
        string.Join(" · ", days.OrderBy(Order).Select(Short));

    public static string Short(DayOfWeek d) => d.ToString()[..3];

    public static string Iso(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "MO",
        DayOfWeek.Tuesday => "TU",
        DayOfWeek.Wednesday => "WE",
        DayOfWeek.Thursday => "TH",
        DayOfWeek.Friday => "FR",
        DayOfWeek.Saturday => "SA",
        _ => "SU"
    };

    public static bool TryParseIso(string code, out DayOfWeek day)
    {
        day = code.ToUpperInvariant() switch
        {
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            "SA" => DayOfWeek.Saturday,
            "SU" => DayOfWeek.Sunday,
            _ => (DayOfWeek)(-1)
        };
        return (int)day >= 0;
    }
}
