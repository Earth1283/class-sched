namespace ClassSchedule.Core;

/// <summary>What a new class looks like before anyone types anything; follows the settings.</summary>
public static class ClassDefaults
{
    public static int Minutes { get; set; } = 40;

    public static TimeSpan Start { get; set; } = new(9, 0, 0);

    public static TimeSpan Length => TimeSpan.FromMinutes(Minutes);
}
