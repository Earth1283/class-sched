using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

/// <summary>Starting values for a new class: a day and time from a right-click, or a copy of an existing class.</summary>
public sealed record ClassPrefill(DayOfWeek? Day = null, TimeSpan? Start = null, ClassSession? CopyOf = null);

public sealed class ClassDialog : DialogWindow
{
    private static readonly DayOfWeek[] Days =
    {
        DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
        DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
    };

    private readonly ScheduleData data;
    private readonly ClassSession? existing;
    private readonly TextBox name, code, start, end, location, instructor, credits, kind;
    private readonly Dictionary<DayOfWeek, ToggleButton> dayChips = new();
    private readonly Dictionary<string, ToggleButton> swatches = new();
    private readonly List<UIElement> basicRows = new();
    private TextBox? pattern;
    private TextBlock? patternPreview;
    private StackPanel? advancedBox;
    private TextBlock? advancedLabel;
    private bool advanced;

    public ClassDialog(Window owner, ScheduleData data, ClassSession? existing, ClassPrefill? prefill = null)
        : base(owner,
            existing is null ? (prefill?.CopyOf is null ? "New class" : "Copy of a class") : "Edit class",
            existing is null ? (prefill?.CopyOf is null ? "Add a class" : "Duplicate class") : "Edit class")
    {
        this.data = data;
        this.existing = existing;
        var source = existing ?? prefill?.CopyOf;
        var course = source is null ? null : data.FindCourse(source.CourseId);
        var begins = existing?.Start ?? prefill?.Start ?? prefill?.CopyOf?.Start;
        var finishes = existing?.End ?? (prefill?.Start is { } at ? at + ClassDefaults.Length : prefill?.CopyOf?.End);

        name = Box(course?.Name ?? "");
        code = Box(course?.Code ?? "");
        start = Box(TimeText.Format(begins ?? ClassDefaults.Start));
        end = Box(TimeText.Format(finishes ?? (begins ?? ClassDefaults.Start) + ClassDefaults.Length));
        location = Box(source?.Location ?? "");
        instructor = Box(course?.Instructor ?? "");
        credits = Box((course?.Credits ?? data.Settings.NewClassCredits).ToString());
        kind = Box(source?.Kind ?? data.Settings.NewClassKind);

        AddRow(("Name", name, 0), ("Code", code, 130));

        var basicMark = Mark();
        var dayPanel = new WrapPanel();
        foreach (var d in Days)
        {
            var chosen = existing is not null ? existing.Days.Contains(d)
                : prefill?.Day is { } only ? d == only
                : prefill?.CopyOf is not null ? false
                : data.Settings.NewClassDays.Contains(d);
            var chip = Chip(DayParser.Short(d), chosen);
            chip.Width = 52;
            AutomationName(chip, d.ToString());
            dayChips[d] = chip;
            dayPanel.Children.Add(chip);
        }
        Add("Days", dayPanel, 8);

        AddRow(("Starts", start, 0), ("Ends", end, 0));
        AddAdvanced(basicMark);
        AddRow(("Location", location, 0), ("Kind", kind, 140));
        AddRow(("Instructor", instructor, 0), ("Credits", credits, 80));

        var selected = course?.Color ?? CoursePalette.ForIndex(data.Courses.Count);
        var swatchPanel = new WrapPanel { Width = 8 * 30 };
        foreach (var key in CoursePalette.Keys)
        {
            var swatch = new RadioButton { GroupName = "color", IsChecked = key == selected, ToolTip = key };
            swatch.SetResourceReference(FrameworkElement.StyleProperty, "Swatch");
            swatch.SetResourceReference(Control.BackgroundProperty, $"Course.{key}.Dot");
            AutomationName(swatch, key);
            swatches[key] = swatch;
            swatchPanel.Children.Add(swatch);
        }
        Add("Color", swatchPanel);

        if (course is not null && data.Sessions.Count(s => s.CourseId == course.Id) > 1)
            Add("", Ui.Text("Name, code, instructor, credits and color apply to every class in this course.", 12, "Brush.Ink3"), 4);

        if (existing is not null)
        {
            var delete = Ui.Button("Delete", null, "BtnQuiet");
            delete.Click += (_, _) => { data.RemoveSession(existing.Id); DialogResult = true; };
            AddLeftAction(delete);
        }
        else
        {
            var another = Ui.Button("Save & add another", null, "BtnSecondary");
            another.Click += (_, _) =>
            {
                if (TrySave(out var message)) { AddAnother = true; DialogResult = true; }
                else ShowError(message);
            };
            AddRightAction(another);
        }
    }

    public bool AddAnother { get; private set; }

    private void AddAdvanced(int basicMark)
    {
        basicRows.AddRange(Since(basicMark));

        pattern = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 76,
            Padding = new Thickness(12, 8, 12, 8),
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        pattern.SetResourceReference(FrameworkElement.StyleProperty, "FieldArea");
        pattern.TextChanged += (_, _) => UpdatePatternPreview();

        patternPreview = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 8, 0, 0) };

        advancedBox = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
        var caption = Ui.Caption("Weekly schedule");
        caption.Margin = new Thickness(0, 0, 0, 5);
        advancedBox.Children.Add(caption);
        advancedBox.Children.Add(pattern);
        advancedBox.Children.Add(patternPreview);
        AddElement(advancedBox);

        var toggle = Ui.Button("Advanced: a different time for each day", "Icon.Sliders", "BtnQuiet", 32);
        advancedLabel = (TextBlock)((StackPanel)toggle.Content).Children[1];
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Margin = new Thickness(-14, 0, 0, 12);
        toggle.Click += (_, _) => SetAdvanced(!advanced);
        AddElement(toggle);
        UpdatePatternPreview();
    }

    private void SetAdvanced(bool on)
    {
        if (pattern is null || advancedBox is null || advancedLabel is null) return;
        advanced = on;
        foreach (var row in basicRows) row.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        advancedBox.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        advancedLabel.Text = on ? "Back to one time for every day" : "Advanced: a different time for each day";
        ShowError("");

        if (!on) return;
        var chosen = Days.Where(d => dayChips[d].IsChecked == true).ToList();
        if (pattern.Text.Trim().Length == 0 && chosen.Count > 0 && TimeText.TryParse(start.Text, out var from) && TimeText.TryParse(end.Text, out var to))
            pattern.Text = WeeklyPattern.Write(chosen, from, to);
        pattern.Focus();
        pattern.CaretIndex = pattern.Text.Length;
        UpdatePatternPreview();
    }

    private void UpdatePatternPreview()
    {
        if (pattern is null || patternPreview is null) return;
        var parsed = WeeklyPattern.Parse(pattern.Text);

        if (parsed.IsEmpty)
        {
            patternPreview.Text = "For example: Mon 9:00-9:50, Wed 10:00-10:50, Fri 2-3:15";
            patternPreview.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Ink3");
        }
        else if (parsed.Errors.Count > 0)
        {
            patternPreview.Text = string.Join(Environment.NewLine, parsed.Errors.Take(2));
            patternPreview.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warn");
        }
        else
        {
            patternPreview.Text = string.Join(Environment.NewLine, parsed.Groups().Select(g => g.ToString()));
            patternPreview.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Ink2");
        }
    }

    private static void AutomationName(DependencyObject element, string text) =>
        System.Windows.Automation.AutomationProperties.SetName(element, text);

    protected override bool TrySave(out string message)
    {
        message = "";
        var nameText = name.Text.Trim();
        var codeText = code.Text.Trim();
        if (nameText.Length == 0 && codeText.Length == 0) { message = "Enter a class name or code."; return false; }
        if (codeText.Length == 0) codeText = nameText;

        List<(List<DayOfWeek> Days, TimeSpan Start, TimeSpan End)> groups;
        if (advanced && pattern is not null)
        {
            var parsed = WeeklyPattern.Parse(pattern.Text);
            if (parsed.Errors.Count > 0) { message = parsed.Errors[0]; return false; }
            if (!parsed.Ok) { message = "Enter at least one day and time, like Mon 9:00-9:50."; return false; }
            groups = parsed.Groups().Select(g => (g.Days.ToList(), g.Start, g.End)).ToList();
        }
        else
        {
            var days = Days.Where(d => dayChips[d].IsChecked == true).ToList();
            if (days.Count == 0) { message = "Pick at least one day."; return false; }
            if (!TimeText.TryParse(start.Text, out var startTime)) { message = "Enter the start time like 9:00 AM."; return false; }
            if (!TimeText.TryParse(end.Text, out var endTime)) { message = "Enter the end time like 9:50 AM."; return false; }
            if (endTime <= startTime) { message = "The end time must be after the start time."; return false; }
            groups = new() { (days, startTime, endTime) };
        }
        if (!int.TryParse(credits.Text, out var creditCount) || creditCount < 0 || creditCount > 30) { message = "Credits must be a whole number."; return false; }

        var color = swatches.First(s => s.Value.IsChecked == true).Key;
        var course = existing is null ? data.FindCourseByCode(codeText) : data.FindCourse(existing.CourseId);
        if (course is null)
        {
            course = new Course { Color = color };
            data.Courses.Add(course);
        }
        else if (existing is not null) course.Color = color;

        course.Code = codeText;
        if (nameText.Length > 0 || existing is not null) course.Name = nameText;
        if (instructor.Text.Trim().Length > 0 || existing is not null) course.Instructor = instructor.Text.Trim();
        course.Credits = creditCount;

        var session = existing ?? new ClassSession();
        session.CourseId = course.Id;
        session.Kind = kind.Text.Trim().Length > 0 ? kind.Text.Trim() : "Lecture";
        session.Days = groups[0].Days;
        session.Start = groups[0].Start;
        session.End = groups[0].End;
        session.Location = location.Text.Trim();
        if (existing is null) data.Sessions.Add(session);
        foreach (var group in groups.Skip(1))
            data.Sessions.Add(new ClassSession { CourseId = course.Id, Kind = session.Kind, Days = group.Days, Start = group.Start, End = group.End, Location = session.Location });
        return true;
    }
}
