namespace ClassSchedule.Core;

public static class CoursePalette
{
    public static readonly string[] Keys =
    {
        "Blue", "Green", "Orange", "Violet", "Rose", "Teal",
        "Red", "Pink", "Indigo", "Lavender", "Cyan", "Lime", "Gold", "Brown", "Slate", "Coral"
    };

    public static string ForIndex(int index) => Keys[((index % Keys.Length) + Keys.Length) % Keys.Length];
}

public enum TaskKind
{
    Assignment,
    Exam
}

public sealed class Course
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Instructor { get; set; } = "";
    public int Credits { get; set; } = 3;
    public string Color { get; set; } = "Blue";
    public bool Visible { get; set; } = true;
}

public sealed class ClassSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public string Kind { get; set; } = "Lecture";
    public List<DayOfWeek> Days { get; set; } = new();
    public TimeSpan Start { get; set; }
    public TimeSpan End { get; set; }
    public string Location { get; set; } = "";

    public TimeSpan Duration => End - Start;
}

public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CourseId { get; set; }
    public string Title { get; set; } = "";
    public TaskKind Kind { get; set; } = TaskKind.Assignment;
    public DateTime Due { get; set; }
    public bool Done { get; set; }
}

/// <summary>A stretch of days with no classes inside the term, such as a holiday or reading week. Both ends are included.</summary>
public sealed class TermBreak
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Break";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    public bool Covers(DateTime date) => date.Date >= Start.Date && date.Date <= End.Date;
}

public sealed class Settings
{
    // Term (travels with the schedule file)
    public string TermName { get; set; } = "My term";
    public DateTime TermStart { get; set; } = DateTime.Today;
    public DateTime TermEnd { get; set; } = DateTime.Today.AddDays(15 * 7);
    public List<TermBreak> Breaks { get; set; } = new();

    // General
    /// <summary>"Week", "Day" or "List".</summary>
    public string DefaultView { get; set; } = "Week";
    public bool RememberPosition { get; set; }
    public DateTime? LastDate { get; set; }
    public bool LaunchAtLogin { get; set; }
    public bool StartHidden { get; set; }

    /// <summary>"WidgetOnly" (keep running only while the widget shows), "Quit" or "Background".</summary>
    public string CloseBehavior { get; set; } = "WidgetOnly";
    public bool ConfirmDeletes { get; set; }
    public int UndoSeconds { get; set; } = 8;
    public bool ContextMenus { get; set; } = true;

    // Calendar
    public bool Use24Hour { get; set; }
    public DayOfWeek WeekStartsOn { get; set; } = DayOfWeek.Monday;
    public bool ShowWeekNumber { get; set; } = true;

    /// <summary>"Auto" (only when a class meets then), "Always" or "Never".</summary>
    public string Weekends { get; set; } = "Auto";
    public bool HoursAuto { get; set; } = true;
    public int HourStart { get; set; } = 7;
    public int HourEnd { get; set; } = 19;

    /// <summary>"Compact", "Comfortable" or "Spacious".</summary>
    public string HourHeightMode { get; set; } = "Comfortable";

    /// <summary>"Code", "Room" or "Fit" (everything that fits).</summary>
    public string CardDetail { get; set; } = "Fit";
    public bool ShowNowLine { get; set; } = true;
    public bool DimPast { get; set; }

    /// <summary>Draw classes in weeks before and after the term too. Breaks stay empty either way.</summary>
    public bool ShowOutsideTerm { get; set; }

    // New classes
    public int NewClassMinutes { get; set; } = 40;
    public TimeSpan NewClassStart { get; set; } = new(9, 0, 0);
    public List<DayOfWeek> NewClassDays { get; set; } = new() { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday };
    public string NewClassKind { get; set; } = "Lecture";
    public int NewClassCredits { get; set; } = 3;

    /// <summary>"Google", "Pastel" or "Vivid".</summary>
    public string Palette { get; set; } = "Google";

    // Reminders
    public bool RemindersEnabled { get; set; } = true;
    public int ReminderMinutes { get; set; } = 10;
    public bool TaskReminders { get; set; } = true;

    /// <summary>"HourBefore", "DayBefore", "Morning" or "DayAndHour".</summary>
    public string TaskReminderWhen { get; set; } = "DayBefore";
    public bool ReminderViaWindows { get; set; } = true;
    public bool ReminderSound { get; set; }

    // Appearance
    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "Light";

    /// <summary>"Blue", "Violet", "Green", "Rose" or "Orange".</summary>
    public string Accent { get; set; } = "Blue";

    /// <summary>"Small", "Normal" or "Large".</summary>
    public string InterfaceSize { get; set; } = "Normal";

    /// <summary>"Comfortable" or "Compact".</summary>
    public string Density { get; set; } = "Comfortable";
    public bool SidebarVisible { get; set; } = true;
    public bool MiniMonthVisible { get; set; } = true;
    public bool CoursesVisible { get; set; } = true;
    public bool RailVisible { get; set; } = true;
    public bool AnimationsEnabled { get; set; } = true;

    /// <summary>Last window placement in device-independent pixels; a width of 0 means none was saved yet.</summary>
    public double WindowLeft { get; set; }
    public double WindowTop { get; set; }
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    // The always-behind "Today's schedule" desktop widget
    public bool WidgetEnabled { get; set; }
    public bool WidgetBehind { get; set; } = true;
    public bool WidgetCompact { get; set; }

    /// <summary>Background opacity in percent, 55 to 100.</summary>
    public int WidgetOpacity { get; set; } = 90;
    public double? WidgetLeft { get; set; }
    public double? WidgetTop { get; set; }
    public bool WidgetShowDone { get; set; } = true;
    public bool WidgetShowDue { get; set; } = true;
    public bool WidgetShowTomorrow { get; set; }

    /// <summary>"Card" (solid), "Glass" (see-through with an outline) or "Minimal" (no background, just text).</summary>
    public string WidgetStyle { get; set; } = "Card";

    /// <summary>"Match" (the app's theme), "Light" or "Dark".</summary>
    public string WidgetTheme { get; set; } = "Match";

    /// <summary>Size of the whole widget in percent, 80 to 150.</summary>
    public int WidgetScale { get; set; } = 100;

    /// <summary>Corner radius of the card in pixels, 0 to 32.</summary>
    public int WidgetRadius { get; set; } = 20;
    public bool WidgetShadow { get; set; } = true;
    public bool WidgetShowHeader { get; set; } = true;
    public bool WidgetShowClock { get; set; }
    public bool WidgetShowProgress { get; set; } = true;
    public bool WidgetShowLocation { get; set; } = true;

    /// <summary>The most class rows to list; 0 shows them all.</summary>
    public int WidgetMaxRows { get; set; }
    public bool WidgetLocked { get; set; }

    /// <summary>Hide the widget once nothing is left today, and bring it back the next day with classes.</summary>
    public bool WidgetHideWhenDone { get; set; }

    // Data
    public bool BackupsEnabled { get; set; }
    public int BackupCount { get; set; } = 5;

    private static readonly string[] TermFields = { nameof(TermName), nameof(TermStart), nameof(TermEnd), nameof(Breaks) };

    private static readonly string[] WindowFields =
        { nameof(WindowLeft), nameof(WindowTop), nameof(WindowWidth), nameof(WindowHeight), nameof(WindowMaximized) };

    /// <summary>Takes every preference from <paramref name="other"/> and leaves this schedule's term alone, so preferences follow you from file to file.</summary>
    public void KeepPreferencesFrom(Settings other) => Copy(other, TermFields);

    /// <summary>Puts every preference back to its default. The term and the window placement stay.</summary>
    public void ResetPreferences() => Copy(new Settings(), TermFields.Concat(WindowFields).ToArray());

    private void Copy(Settings from, string[] skip)
    {
        foreach (var property in typeof(Settings).GetProperties())
        {
            if (!property.CanWrite || skip.Contains(property.Name)) continue;
            var value = property.GetValue(from);
            if (value is List<DayOfWeek> days) value = new List<DayOfWeek>(days);
            property.SetValue(this, value);
        }
    }
}

public sealed class ScheduleData
{
    public int Version { get; set; } = 1;
    public Settings Settings { get; set; } = new();
    public List<Course> Courses { get; set; } = new();
    public List<ClassSession> Sessions { get; set; } = new();
    public List<TaskItem> Tasks { get; set; } = new();

    public Course? FindCourse(Guid id) => Courses.FirstOrDefault(c => c.Id == id);

    public Course? FindCourseByCode(string code) =>
        Courses.FirstOrDefault(c => string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));

    public Course? FindCourseByName(string name) =>
        Courses.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Swaps in another schedule's contents while keeping this object, and anything holding it, valid.</summary>
    public void ReplaceContentsWith(ScheduleData other)
    {
        Settings = other.Settings;
        Courses.Clear();
        Courses.AddRange(other.Courses);
        Sessions.Clear();
        Sessions.AddRange(other.Sessions);
        Tasks.Clear();
        Tasks.AddRange(other.Tasks);
    }

    public void Clear()
    {
        Courses.Clear();
        Sessions.Clear();
        Tasks.Clear();
    }

    /// <summary>Empties the schedule like <see cref="Clear"/> and returns a way to bring everything back.</summary>
    public Action ClearWithUndo()
    {
        var courses = Courses.ToList();
        var sessions = Sessions.ToList();
        var tasks = Tasks.ToList();
        Clear();
        return () =>
        {
            if (Courses.Count + Sessions.Count + Tasks.Count > 0) return;
            Courses.AddRange(courses);
            Sessions.AddRange(sessions);
            Tasks.AddRange(tasks);
        };
    }

    /// <summary>Removes an assignment or exam and returns a way to put it back in its place, or null when it wasn't found.</summary>
    public Action? RemoveTaskWithUndo(Guid taskId)
    {
        var task = Tasks.FirstOrDefault(t => t.Id == taskId);
        if (task is null) return null;

        var index = Tasks.IndexOf(task);
        Tasks.Remove(task);
        return () =>
        {
            if (Tasks.Any(t => t.Id == task.Id)) return;
            Tasks.Insert(Math.Min(index, Tasks.Count), task);
        };
    }

    /// <summary>Removes a course with its classes and its assignments and exams, and returns a way to put them all back.</summary>
    public Action? RemoveCourseWithUndo(Guid courseId)
    {
        var course = FindCourse(courseId);
        if (course is null) return null;

        var courseIndex = Courses.IndexOf(course);
        var sessions = Sessions.Select((s, i) => (Item: s, Index: i)).Where(x => x.Item.CourseId == courseId).ToList();
        var tasks = Tasks.Select((t, i) => (Item: t, Index: i)).Where(x => x.Item.CourseId == courseId).ToList();
        Courses.Remove(course);
        Sessions.RemoveAll(s => s.CourseId == courseId);
        Tasks.RemoveAll(t => t.CourseId == courseId);

        return () =>
        {
            if (FindCourse(courseId) is not null) return;
            Courses.Insert(Math.Min(courseIndex, Courses.Count), course);
            foreach (var (item, index) in sessions) Sessions.Insert(Math.Min(index, Sessions.Count), item);
            foreach (var (item, index) in tasks) Tasks.Insert(Math.Min(index, Tasks.Count), item);
        };
    }

    /// <summary>Takes one weekday out of a class that meets on several; the whole class goes when it was the only day.</summary>
    public Action? RemoveDayWithUndo(Guid sessionId, DayOfWeek day)
    {
        var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null || !session.Days.Contains(day)) return null;
        if (session.Days.Count == 1) return RemoveSessionWithUndo(sessionId);

        var before = session.Days.ToList();
        session.Days.Remove(day);
        return () => session.Days = before;
    }

    /// <summary>Removes a session like <see cref="RemoveSession"/> and returns a way to put everything back, or null when it wasn't found.</summary>
    public Action? RemoveSessionWithUndo(Guid sessionId)
    {
        var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null) return null;

        var index = Sessions.IndexOf(session);
        var course = FindCourse(session.CourseId);
        var courseIndex = course is null ? -1 : Courses.IndexOf(course);
        RemoveSession(sessionId);
        var courseRemoved = course is not null && FindCourse(course.Id) is null;

        return () =>
        {
            if (Sessions.Any(s => s.Id == session.Id)) return;
            if (courseRemoved && course is not null && FindCourse(course.Id) is null)
                Courses.Insert(Math.Min(courseIndex, Courses.Count), course);
            Sessions.Insert(Math.Min(index, Sessions.Count), session);
        };
    }

    /// <summary>Removes a session, and its course when no other session or task still uses it.</summary>
    public void RemoveSession(Guid sessionId)
    {
        var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null) return;
        Sessions.Remove(session);
        if (!Sessions.Any(s => s.CourseId == session.CourseId) && !Tasks.Any(t => t.CourseId == session.CourseId))
            Courses.RemoveAll(c => c.Id == session.CourseId);
    }
}
