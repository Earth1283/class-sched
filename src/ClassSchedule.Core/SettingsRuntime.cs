namespace ClassSchedule.Core;

/// <summary>Pushes the settings that the core library reads from static state, such as the clock style and the first day of the week.</summary>
public static class SettingsRuntime
{
    public static void Apply(Settings s)
    {
        TimeText.Use24Hour = s.Use24Hour;
        DayParser.FirstDay = s.WeekStartsOn;
        ClassDefaults.Minutes = Math.Clamp(s.NewClassMinutes, 5, 480);
        ClassDefaults.Start = s.NewClassStart;
    }
}
