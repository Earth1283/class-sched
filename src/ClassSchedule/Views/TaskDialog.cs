using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public sealed class TaskDialog : DialogWindow
{
    private readonly ScheduleData data;
    private readonly TaskItem? existing;
    private readonly TextBox title, date, time;
    private readonly Dictionary<Guid, ToggleButton> courseChips = new();
    private readonly ToggleButton assignment, exam;

    public TaskDialog(Window owner, ScheduleData data, TaskItem? existing, DateTime? suggestedDue = null)
        : base(owner, existing is null ? "New task" : "Edit task", existing is null ? "Add assignment or exam" : "Edit task")
    {
        this.data = data;
        this.existing = existing;
        var due = existing?.Due ?? suggestedDue ?? DateTime.Today.AddDays(1).AddHours(23).AddMinutes(59);

        title = Box(existing?.Title ?? "");
        date = Box(due.ToString("d", CultureInfo.CurrentCulture));
        time = Box(TimeText.Format(due.TimeOfDay));

        Add("Title", title);

        var courses = new WrapPanel();
        foreach (var c in data.Courses)
        {
            var chip = Chip(c.Code, existing?.CourseId == c.Id || (existing is null && c == data.Courses[0]), "course");
            courseChips[c.Id] = chip;
            courses.Children.Add(chip);
        }
        Add("Course", data.Courses.Count == 0 ? Ui.Text("Add a class first, then attach tasks to it.", 12.5, "Brush.Ink3") : courses, 8);

        var kinds = new StackPanel { Orientation = Orientation.Horizontal };
        assignment = Chip("Assignment", existing?.Kind != TaskKind.Exam, "kind");
        exam = Chip("Exam", existing?.Kind == TaskKind.Exam, "kind");
        kinds.Children.Add(assignment);
        kinds.Children.Add(exam);
        Add("Type", kinds, 8);

        AddRow(("Due date", date, 0), ("Due time", time, 0));

        if (existing is not null)
        {
            var delete = Ui.Button("Delete", null, "BtnQuiet");
            delete.Click += (_, _) => { data.Tasks.Remove(existing); DialogResult = true; };
            AddLeftAction(delete);
        }
    }

    protected override bool TrySave(out string message)
    {
        message = "";
        if (title.Text.Trim().Length == 0) { message = "Enter a title."; return false; }
        var chosen = courseChips.FirstOrDefault(c => c.Value.IsChecked == true);
        if (courseChips.Count == 0 || chosen.Key == Guid.Empty) { message = "Pick a course."; return false; }
        if (!DateTime.TryParse(date.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var day)) { message = "Enter the due date like 2026-11-04."; return false; }
        if (!TimeText.TryParse(time.Text, out var at)) { message = "Enter the due time like 11:59 PM."; return false; }

        var task = existing ?? new TaskItem();
        task.Title = title.Text.Trim();
        task.CourseId = chosen.Key;
        task.Kind = exam.IsChecked == true ? TaskKind.Exam : TaskKind.Assignment;
        task.Due = day.Date + at;
        if (existing is null) data.Tasks.Add(task);
        return true;
    }
}
