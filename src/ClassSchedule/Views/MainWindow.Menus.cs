using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public partial class MainWindow
{
    private void ShowDetails(PlacedSession placed, FrameworkElement target)
    {
        var key = placed.Course.Color;
        var session = placed.Session;

        var stack = new StackPanel { Width = 292 };
        var kind = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, $"Course.{key}.Dot");
        kind.Children.Add(dot);
        kind.Children.Add(Ui.Caption(session.Kind));
        stack.Children.Add(kind);

        var heading = placed.Course.Name.Length > 0 ? $"{placed.Course.Code} · {placed.Course.Name}" : placed.Course.Code;
        var title = Ui.Heading(heading, 20);
        title.TextWrapping = TextWrapping.Wrap;
        title.Margin = new Thickness(0, 8, 0, 14);
        stack.Children.Add(title);

        void Row(string icon, string text)
        {
            if (text.Length == 0) return;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new Icon(icon, 16, "Brush.Ink3"));
            var label = Ui.Text(text, 13, "Brush.Ink2");
            label.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(label);
            stack.Children.Add(row);
        }
        Row("Icon.Clock", $"{DayParser.Abbreviations(session.Days)} · {TimeText.Range(session.Start, session.End)}");
        Row("Icon.Pin", session.Location);
        Row("Icon.User", placed.Course.Instructor);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var edit = Ui.Button("Edit", null, "BtnSecondary", 30);
        edit.Margin = new Thickness(0, 0, 6, 0);
        edit.Click += (_, _) =>
        {
            DetailPopup.IsOpen = false;
            EditSession(session);
        };
        var delete = Ui.Button("Delete", null, "BtnQuiet", 30);
        delete.Click += (_, _) =>
        {
            DetailPopup.IsOpen = false;
            DeleteSession(placed, target);
        };
        actions.Children.Add(edit);
        actions.Children.Add(delete);
        stack.Children.Add(actions);

        var back = Ui.Popover(stack, new Thickness(16));
        back.Margin = new Thickness(8);

        DetailPopup.Child = back;
        DetailPopup.PlacementTarget = target;
        DetailPopup.IsOpen = true;

        MarkOpen(target, key);
    }

    private void MarkOpen(FrameworkElement target, string key)
    {
        ClearHighlight();
        if (target is Border card && card.Tag is "event")
        {
            highlighted = card;
            highlightKey = key;
            card.Tag = "open";
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.Ink");
        }
    }

    private void EditSession(ClassSession session)
    {
        var name = data.FindCourse(session.CourseId)?.Code ?? "class";
        var dialog = new ClassDialog(this, data, session);
        if (dialog.ShowDialog() != true) return;

        Commit();
        if (dialog.UndoDelete is { } undo) ShowUndo($"Deleted {name} {session.Kind}", undo);
    }

    private void ShowEventMenu(PlacedSession placed, FrameworkElement target, DayOfWeek day)
    {
        if (!data.Settings.ContextMenus) return;
        DetailPopup.IsOpen = false;
        var session = placed.Session;
        var course = placed.Course;

        var menu = new MenuPopup($"{course.Code} · {session.Kind} · {TimeText.Range(session.Start, session.End)}");
        menu.Add("Icon.Edit", "Edit…", "", () => EditSession(session));
        menu.Add("Icon.Duplicate", "Duplicate to another day…", "", () => AddClass(new ClassPrefill(CopyOf: session)));
        menu.Add("Icon.Copy", "Copy details", "", () => CopyDetails(placed));
        menu.Separator();
        menu.Add("Icon.EyeOff", $"Hide {course.Code} from the schedule", "", () => HideCourse(course, target));
        if (session.Days.Count > 1)
        {
            menu.Add("Icon.Trash", $"Delete {day} only", "", () => DeleteDay(placed, target, day), danger: true);
            menu.Add("Icon.Trash", $"Delete all {session.Days.Count} days ({DayParser.Abbreviations(session.Days)})", "", () => DeleteSession(placed, target), danger: true);
        }
        else menu.Add("Icon.Trash", "Delete", "", () => DeleteSession(placed, target), danger: true);

        MarkOpen(target, course.Color);
        menu.Closed += ClearHighlight;
        menu.Open(target);
    }

    private void ShowSlotMenu(DateTime date, TimeSpan time, FrameworkElement anchor)
    {
        if (!data.Settings.ContextMenus) return;
        DetailPopup.IsOpen = false;
        grid.ShowSlotHint(date, time);

        var menu = new MenuPopup($"{date:dddd} · {TimeText.Format(time)}");
        menu.Add("Icon.Plus", "New class here…", "Ctrl+N", () => AddClass(new ClassPrefill(date.DayOfWeek, time)));
        menu.Add("Icon.Check", "New assignment or exam…", "Ctrl+T", () => AddTask(date.Date + time));
        menu.Add("Icon.Rows", "Paste classes…", "Ctrl+B", OpenBulkAdd);
        menu.Separator();
        menu.Add("Icon.Calendar", "Go to this day", "", () => GoToDay(date));
        menu.Closed += grid.ClearSlotHint;
        menu.Open(anchor);
    }

    private void ShowDayMenu(DateTime date, FrameworkElement anchor)
    {
        if (!data.Settings.ContextMenus) return;
        DetailPopup.IsOpen = false;
        var menu = new MenuPopup($"{date:dddd, MMM d}");
        menu.Add("Icon.Calendar", "Open day view", "", () => GoToDay(date));
        menu.Add("Icon.Plus", "New class on this day…", "", () => AddClass(new ClassPrefill(date.DayOfWeek)));
        menu.Separator();
        if (TermCalendar.BreakOn(data.Settings, date) is { } found)
        {
            menu.Add("Icon.Edit", $"Edit “{found.Name}”…", "", () => EditBreak(found));
            menu.Add("Icon.Trash", $"Remove “{found.Name}”", "", () => RemoveBreak(found), danger: true);
        }
        else
        {
            var week = ScheduleQueries.WeekStart(date);
            menu.Add("Icon.EyeOff", "Mark this day off…", "", () => AddBreak(date, date));
            menu.Add("Icon.EyeOff", "Mark this week off…", "", () => AddBreak(week, week.AddDays(6)));
        }
        menu.Open(anchor);
    }

    private void DeleteSession(PlacedSession placed, FrameworkElement target)
    {
        if (!Confirm($"Delete {placed.Course.Code} {placed.Session.Kind} ({DayParser.Abbreviations(placed.Session.Days)})?")) return;
        FadeDeleteSession(placed, target);
    }

    private void FadeDeleteSession(PlacedSession placed, FrameworkElement target) =>
        Motion.FadeOut(target, 200, () =>
        {
            var undo = data.RemoveSessionWithUndo(placed.Session.Id);
            if (undo is null) return;
            Commit();
            ShowUndo($"Deleted {placed.Course.Code} {placed.Session.Kind}", undo);
        });

    private void DeleteDay(PlacedSession placed, FrameworkElement target, DayOfWeek day)
    {
        if (!Confirm($"Delete {placed.Course.Code} {placed.Session.Kind} on {day}?")) return;
        FadeDeleteDay(placed, target, day);
    }

    private void FadeDeleteDay(PlacedSession placed, FrameworkElement target, DayOfWeek day) =>
        Motion.FadeOut(target, 200, () =>
        {
            var undo = data.RemoveDayWithUndo(placed.Session.Id, day);
            if (undo is null) return;
            Commit();
            ShowUndo($"Deleted {placed.Course.Code} {placed.Session.Kind} on {day}", undo);
        });

    private void HideCourse(Course course, FrameworkElement target) =>
        Motion.FadeOut(target, 200, () =>
        {
            course.Visible = false;
            Commit();
            ShowUndo($"Hid {course.Code}", () => course.Visible = true);
        });

    private void CopyDetails(PlacedSession placed)
    {
        var s = placed.Session;
        var c = placed.Course;
        var lines = new[]
        {
            c.Name.Length > 0 ? $"{c.Code} · {c.Name}" : c.Code,
            $"{s.Kind}, {DayParser.Abbreviations(s.Days)} {TimeText.Range(s.Start, s.End)}",
            s.Location,
            c.Instructor
        }.Where(line => line.Length > 0);

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            ShowToast("Copied class details", "");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ShowToast("The clipboard is busy", "Try again in a moment.");
        }
    }

    private void OnDetailClosed(object? sender, EventArgs e) => ClearHighlight();

    private void ClearHighlight()
    {
        if (highlighted is null) return;
        highlighted.Tag = "event";
        highlighted.SetResourceReference(Border.BorderBrushProperty, $"Course.{highlightKey}.Line");
        highlighted = null;
    }
}
