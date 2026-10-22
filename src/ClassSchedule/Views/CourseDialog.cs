using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

/// <summary>Edits what a course is, apart from any one of its classes: its name, code, instructor, credits and color.</summary>
public sealed class CourseDialog : DialogWindow
{
    private readonly ScheduleData data;
    private readonly Course course;
    private readonly TextBox name, code, instructor, credits;
    private readonly Dictionary<string, ToggleButton> swatches = new();

    public CourseDialog(Window owner, ScheduleData data, Course course)
        : base(owner, "Course", "Edit course")
    {
        this.data = data;
        this.course = course;

        name = Box(course.Name);
        code = Box(course.Code);
        instructor = Box(course.Instructor);
        credits = Box(course.Credits.ToString());

        AddRow(("Name", name, 0), ("Code", code, 130));
        AddRow(("Instructor", instructor, 0), ("Credits", credits, 80));

        var panel = new WrapPanel { Width = 8 * 30 };
        foreach (var key in CoursePalette.Keys)
        {
            var swatch = new RadioButton { GroupName = "color", IsChecked = key == course.Color, ToolTip = key };
            swatch.SetResourceReference(FrameworkElement.StyleProperty, "Swatch");
            swatch.SetResourceReference(Control.BackgroundProperty, $"Course.{key}.Dot");
            System.Windows.Automation.AutomationProperties.SetName(swatch, key);
            swatches[key] = swatch;
            panel.Children.Add(swatch);
        }
        Add("Color", panel);

        var classes = data.Sessions.Count(s => s.CourseId == course.Id);
        var work = data.Tasks.Count(t => t.CourseId == course.Id);
        Add("", Ui.Text($"{classes} {(classes == 1 ? "class" : "classes")} and {work} {(work == 1 ? "task" : "tasks")} use this course.", 12, "Brush.Ink3"), 4);
    }

    protected override bool TrySave(out string message)
    {
        message = "";
        var nameText = name.Text.Trim();
        var codeText = code.Text.Trim();
        if (nameText.Length == 0 && codeText.Length == 0) { message = "Enter a course name or code."; return false; }
        if (codeText.Length == 0) codeText = nameText;
        if (data.Courses.Any(c => c != course && string.Equals(c.Code, codeText, StringComparison.OrdinalIgnoreCase)))
        {
            message = $"Another course already uses the code {codeText}.";
            return false;
        }
        if (!int.TryParse(credits.Text, out var creditCount) || creditCount < 0 || creditCount > 30) { message = "Credits must be a whole number."; return false; }

        course.Name = nameText;
        course.Code = codeText;
        course.Instructor = instructor.Text.Trim();
        course.Credits = creditCount;
        course.Color = swatches.First(s => s.Value.IsChecked == true).Key;
        return true;
    }
}
