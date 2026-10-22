using System.Linq;
using System.Windows;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public partial class MainWindow
{
    private void ShowCourseMenu(Course course, FrameworkElement anchor)
    {
        if (!data.Settings.ContextMenus) return;
        DetailPopup.IsOpen = false;

        var menu = new MenuPopup(course.Name.Length > 0 ? $"{course.Code} · {course.Name}" : course.Code);
        menu.Add("Icon.Edit", "Edit course…", "", () => EditCourse(course));
        menu.AddSwatches(CoursePalette.Keys, course.Color, key =>
        {
            course.Color = key;
            Commit();
        });
        menu.Separator();
        menu.Add("Icon.Plus", $"Add a class to {course.Code}…", "", () => AddClass(new ClassPrefill(Course: course)));
        menu.Add("Icon.Check", "Add assignment or exam…", "", () => AddTask(null, course.Id));
        menu.Separator();
        menu.Add("Icon.EyeOff", course.Visible ? $"Hide {course.Code}" : $"Show {course.Code}", "", () => ToggleCourse(course));
        if (data.Courses.Count > 1) menu.Add("Icon.Grid", $"Show only {course.Code}", "", () => ShowOnly(course));
        if (data.Courses.Any(c => !c.Visible)) menu.Add("Icon.Rows", "Show all courses", "", () => ShowOnly(null));
        menu.Separator();
        menu.Add("Icon.Trash", "Delete course…", "", () => DeleteCourse(course), danger: true);
        menu.Open(anchor);
    }

    private void EditCourse(Course course)
    {
        if (new CourseDialog(this, data, course).ShowDialog() == true) Commit();
    }

    /// <summary>Shows one course and hides the rest, or every course when <paramref name="only"/> is null.</summary>
    private void ShowOnly(Course? only)
    {
        var before = data.Courses.ToDictionary(c => c.Id, c => c.Visible);
        foreach (var c in data.Courses) c.Visible = only is null || c == only;
        animateNext = true;
        Commit();
        ShowUndo(only is null ? "Showing every course" : $"Showing only {only.Code}", () =>
        {
            foreach (var c in data.Courses)
                if (before.TryGetValue(c.Id, out var visible)) c.Visible = visible;
        });
    }

    private void DeleteCourse(Course course)
    {
        var classes = data.Sessions.Count(s => s.CourseId == course.Id);
        var tasks = data.Tasks.Count(t => t.CourseId == course.Id);
        if (!Confirm($"Delete {course.Code} with its {classes} classes and {tasks} assignments and exams?")) return;

        var undo = data.RemoveCourseWithUndo(course.Id);
        if (undo is null) return;
        Commit();
        ShowUndo($"Deleted {course.Code}", undo);
    }
}
