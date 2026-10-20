using ClassSchedule.Core;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ClassSchedule.Core.Tests;

public class PreferenceTests
{
    private static void Restore()
    {
        SettingsRuntime.Apply(new Settings());
    }

    [Fact]
    public void Defaults_match_the_original_behaviour()
    {
        var s = new Settings();
        Assert.Equal("Week", s.DefaultView);
        Assert.Equal("WidgetOnly", s.CloseBehavior);
        Assert.False(s.Use24Hour);
        Assert.Equal(DayOfWeek.Monday, s.WeekStartsOn);
        Assert.Equal("Auto", s.Weekends);
        Assert.True(s.HoursAuto);
        Assert.Equal(40, s.NewClassMinutes);
        Assert.Equal(new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday }, s.NewClassDays);
        Assert.Equal("Google", s.Palette);
        Assert.Equal("Blue", s.Accent);
    }

    [Fact]
    public void Reset_puts_preferences_back_but_keeps_the_term_and_window()
    {
        var s = new Settings
        {
            TermName = "Spring", Use24Hour = true, Accent = "Rose", WidgetEnabled = true, BackupCount = 12,
            WindowWidth = 1000, WindowLeft = 30
        };
        s.NewClassDays.Add(DayOfWeek.Sunday);

        s.ResetPreferences();

        Assert.Equal("Spring", s.TermName);
        Assert.Equal(1000, s.WindowWidth);
        Assert.Equal(30, s.WindowLeft);
        Assert.False(s.Use24Hour);
        Assert.Equal("Blue", s.Accent);
        Assert.False(s.WidgetEnabled);
        Assert.Equal(5, s.BackupCount);
        Assert.Equal(3, s.NewClassDays.Count);
    }

    [Fact]
    public void Preferences_follow_you_to_another_file_but_the_term_does_not()
    {
        var mine = new Settings { TermName = "Fall", Use24Hour = true, Palette = "Pastel", NewClassMinutes = 55 };
        mine.NewClassDays = new List<DayOfWeek> { DayOfWeek.Tuesday };
        var theirs = new Settings { TermName = "Spring" };

        theirs.KeepPreferencesFrom(mine);

        Assert.Equal("Spring", theirs.TermName);
        Assert.True(theirs.Use24Hour);
        Assert.Equal("Pastel", theirs.Palette);
        Assert.Equal(55, theirs.NewClassMinutes);
        Assert.Equal(new[] { DayOfWeek.Tuesday }, theirs.NewClassDays);
        mine.NewClassDays.Add(DayOfWeek.Thursday);
        Assert.Single(theirs.NewClassDays);
    }

    [Fact]
    public void Every_preference_survives_a_save_and_load()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cs-test-" + Guid.NewGuid());
        try
        {
            var store = new ScheduleStore(Path.Combine(dir, "schedule.json"));
            var data = new ScheduleData();
            var s = data.Settings;
            s.DefaultView = "List"; s.Use24Hour = true; s.WeekStartsOn = DayOfWeek.Sunday; s.Weekends = "Always"; s.HoursAuto = false;
            s.HourStart = 6; s.HourEnd = 21; s.HourHeightMode = "Spacious"; s.CardDetail = "Room"; s.DimPast = true;
            s.NewClassMinutes = 75; s.NewClassStart = new TimeSpan(13, 30, 0); s.NewClassDays = new List<DayOfWeek> { DayOfWeek.Tuesday, DayOfWeek.Thursday };
            s.TaskReminderWhen = "DayAndHour"; s.Accent = "Green"; s.InterfaceSize = "Large"; s.Density = "Compact";
            s.CloseBehavior = "Quit"; s.UndoSeconds = 12; s.BackupsEnabled = true; s.WidgetShowTomorrow = true;
            store.Save(data);

            var loaded = store.Load(DateTime.Today).Settings;
            Assert.Equal("List", loaded.DefaultView);
            Assert.True(loaded.Use24Hour);
            Assert.Equal(DayOfWeek.Sunday, loaded.WeekStartsOn);
            Assert.Equal("Always", loaded.Weekends);
            Assert.False(loaded.HoursAuto);
            Assert.Equal(6, loaded.HourStart);
            Assert.Equal(21, loaded.HourEnd);
            Assert.Equal("Spacious", loaded.HourHeightMode);
            Assert.Equal("Room", loaded.CardDetail);
            Assert.True(loaded.DimPast);
            Assert.Equal(75, loaded.NewClassMinutes);
            Assert.Equal(new TimeSpan(13, 30, 0), loaded.NewClassStart);
            Assert.Equal(new[] { DayOfWeek.Tuesday, DayOfWeek.Thursday }, loaded.NewClassDays);
            Assert.Equal("DayAndHour", loaded.TaskReminderWhen);
            Assert.Equal("Green", loaded.Accent);
            Assert.Equal("Large", loaded.InterfaceSize);
            Assert.Equal("Compact", loaded.Density);
            Assert.Equal("Quit", loaded.CloseBehavior);
            Assert.Equal(12, loaded.UndoSeconds);
            Assert.True(loaded.BackupsEnabled);
            Assert.True(loaded.WidgetShowTomorrow);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Clock_style_follows_the_setting()
    {
        try
        {
            SettingsRuntime.Apply(new Settings { Use24Hour = true });
            Assert.Equal("13:05", TimeText.Format(new TimeSpan(13, 5, 0)));
            Assert.Equal("09:00–09:50", TimeText.Range(new TimeSpan(9, 0, 0), new TimeSpan(9, 50, 0)));
            Assert.True(TimeText.TryParse("1:05 PM", out var t));
            Assert.Equal(new TimeSpan(13, 5, 0), t);
        }
        finally { Restore(); }
        Assert.Equal("1:05 PM", TimeText.Format(new TimeSpan(13, 5, 0)));
    }

    [Fact]
    public void A_week_can_start_on_sunday()
    {
        try
        {
            var wednesday = new DateTime(2026, 10, 21);
            Assert.Equal(new DateTime(2026, 10, 19), ScheduleQueries.WeekStart(wednesday));

            SettingsRuntime.Apply(new Settings { WeekStartsOn = DayOfWeek.Sunday });
            Assert.Equal(new DateTime(2026, 10, 18), ScheduleQueries.WeekStart(wednesday));
            Assert.Equal(0, DayParser.Order(DayOfWeek.Sunday));
            Assert.Equal(6, DayParser.Order(DayOfWeek.Saturday));

            var data = new ScheduleData { Settings = { Weekends = "Always", WeekStartsOn = DayOfWeek.Sunday } };
            var days = ScheduleQueries.VisibleDays(data);
            Assert.Equal(DayOfWeek.Sunday, days[0]);
            Assert.Equal(DayOfWeek.Saturday, days[^1]);
        }
        finally { Restore(); }
    }

    [Theory]
    [InlineData("Auto", 5)]
    [InlineData("Never", 5)]
    [InlineData("Always", 7)]
    public void Weekends_follow_the_setting(string mode, int expected)
    {
        var data = new ScheduleData { Settings = { Weekends = mode } };
        Assert.Equal(expected, ScheduleQueries.VisibleDays(data).Count);
    }

    [Fact]
    public void Auto_weekends_appear_when_a_class_meets_on_one_but_never_stays_hidden()
    {
        var data = new ScheduleData();
        var course = new Course();
        data.Courses.Add(course);
        data.Sessions.Add(new ClassSession { CourseId = course.Id, Days = { DayOfWeek.Saturday }, Start = TimeSpan.FromHours(9), End = TimeSpan.FromHours(10) });
        Assert.Equal(6, ScheduleQueries.VisibleDays(data).Count);
        data.Settings.Weekends = "Never";
        Assert.Equal(5, ScheduleQueries.VisibleDays(data).Count);
    }

    [Fact]
    public void Chosen_hours_are_a_minimum_and_still_widen_for_classes()
    {
        var data = new ScheduleData { Settings = { HoursAuto = false, HourStart = 7, HourEnd = 19 } };
        Assert.Equal((7, 19), ScheduleQueries.HourRange(data));

        var course = new Course();
        data.Courses.Add(course);
        data.Sessions.Add(new ClassSession { CourseId = course.Id, Days = { DayOfWeek.Monday }, Start = TimeSpan.FromHours(20), End = TimeSpan.FromHours(21.5) });
        Assert.Equal((7, 22), ScheduleQueries.HourRange(data));
    }

    [Fact]
    public void The_default_hours_are_unchanged_in_auto_mode()
    {
        Assert.Equal((8, 17), ScheduleQueries.HourRange(new ScheduleData()));
    }

    private static ScheduleData WithTask(DateTime due, string when, out TaskItem task)
    {
        var data = new ScheduleData { Settings = { TaskReminderWhen = when } };
        var course = new Course { Code = "X 1" };
        data.Courses.Add(course);
        task = new TaskItem { CourseId = course.Id, Title = "Essay", Due = due };
        data.Tasks.Add(task);
        return data;
    }

    [Fact]
    public void Day_before_reminds_once_a_day_ahead()
    {
        var due = new DateTime(2026, 10, 21, 17, 0, 0);
        var data = WithTask(due, "DayBefore", out _);
        var sent = new HashSet<string>();
        Assert.Empty(ReminderPlanner.Due(data, due.AddHours(-25), sent));
        Assert.Single(ReminderPlanner.Due(data, due.AddHours(-23), sent));
        Assert.Empty(ReminderPlanner.Due(data, due.AddHours(-22), sent));
    }

    [Fact]
    public void Hour_before_waits_until_the_last_hour()
    {
        var due = new DateTime(2026, 10, 21, 17, 0, 0);
        var data = WithTask(due, "HourBefore", out _);
        var sent = new HashSet<string>();
        Assert.Empty(ReminderPlanner.Due(data, due.AddHours(-5), sent));
        Assert.Single(ReminderPlanner.Due(data, due.AddMinutes(-50), sent));
    }

    [Fact]
    public void Morning_of_reminds_at_eight_on_the_due_date()
    {
        var due = new DateTime(2026, 10, 21, 17, 0, 0);
        var data = WithTask(due, "Morning", out _);
        var sent = new HashSet<string>();
        Assert.Empty(ReminderPlanner.Due(data, new DateTime(2026, 10, 21, 7, 59, 0), sent));
        Assert.Single(ReminderPlanner.Due(data, new DateTime(2026, 10, 21, 8, 1, 0), sent));
    }

    [Fact]
    public void Day_and_hour_gives_two_separate_reminders()
    {
        var due = new DateTime(2026, 10, 21, 17, 0, 0);
        var data = WithTask(due, "DayAndHour", out _);
        var sent = new HashSet<string>();
        Assert.Single(ReminderPlanner.Due(data, due.AddHours(-23), sent));
        Assert.Single(ReminderPlanner.Due(data, due.AddMinutes(-30), sent));
        Assert.Empty(ReminderPlanner.Due(data, due.AddMinutes(-10), sent));
    }

    [Fact]
    public void Task_reminders_can_be_turned_off_without_silencing_classes()
    {
        var due = new DateTime(2026, 10, 21, 17, 0, 0);
        var data = WithTask(due, "DayBefore", out _);
        data.Settings.TaskReminders = false;
        Assert.Empty(ReminderPlanner.Due(data, due.AddHours(-2), new HashSet<string>()));
    }

    [Fact]
    public void New_class_defaults_follow_the_settings()
    {
        try
        {
            SettingsRuntime.Apply(new Settings { NewClassMinutes = 75, NewClassStart = new TimeSpan(13, 0, 0) });
            Assert.Equal(75, ClassDefaults.Minutes);
            Assert.Equal(TimeSpan.FromMinutes(75), ClassDefaults.Length);
            Assert.Equal(new TimeSpan(13, 0, 0), ClassDefaults.Start);
            var row = BulkAdd.Parse("CS 101 Intro MWF 1pm").Single();
            Assert.Equal(new TimeSpan(14, 15, 0), row.End);
        }
        finally { Restore(); }
    }

    [Fact]
    public void Backups_are_dated_copies_and_only_the_newest_are_kept()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cs-test-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "schedule.json");
            File.WriteAllText(path, "{}");
            var start = new DateTime(2026, 10, 20, 8, 0, 0);
            for (var i = 0; i < 6; i++) Assert.NotNull(ScheduleBackups.CreateAndPrune(path, 3, start.AddMinutes(i)));

            var backups = Directory.GetFiles(dir, "schedule.backup-*.json").Select(Path.GetFileName).OrderBy(n => n).ToList();
            Assert.Equal(3, backups.Count);
            Assert.Equal("schedule.backup-20261020-080500.json", backups[^1]);
            Assert.Equal("{}", File.ReadAllText(path));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void A_missing_file_has_nothing_to_back_up()
    {
        Assert.Null(ScheduleBackups.CreateAndPrune(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid() + ".json"), 3, DateTime.Now));
    }
}
