using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public static class SidePanels
{
    private static Border Card(UIElement content)
    {
        var panel = Ui.Panel(content, new Thickness(20, Ui.Row(16, 11), 20, Ui.Row(16, 11)));
        panel.Margin = new Thickness(0, 0, 0, 12);
        return panel;
    }

    /// <param name="animate">Whether the progress bar sweeps in. The clock redraws this card every 30 seconds, and that must not replay it.</param>
    public static UIElement NowCard(ScheduleData data, DateTime now, bool animate = true)
    {
        var info = ScheduleQueries.Now(data, now);
        var stack = new StackPanel();

        if (info.Current is { } cur)
        {
            var key = cur.Course.Color;
            var live = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            live.SetResourceReference(Shape.FillProperty, "Brush.Now");
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(live);
            head.Children.Add(Ui.Caption("In class now"));
            stack.Children.Add(head);

            var title = Ui.Heading(cur.Course.Code, 20);
            title.Margin = new Thickness(0, 10, 0, 0);
            stack.Children.Add(title);
            if (cur.Course.Name.Length > 0) stack.Children.Add(Ui.Text(cur.Course.Name, 13, "Brush.Ink2"));
            var where = string.Join(" · ", new[] { cur.Session.Location, cur.Course.Instructor }.Where(s => s.Length > 0));
            if (where.Length > 0)
            {
                var line = Ui.Text(where, 12.5, "Brush.Ink3");
                line.Margin = new Thickness(0, 2, 0, 0);
                stack.Children.Add(line);
            }

            var span = (cur.Session.End - cur.Session.Start).TotalMinutes;
            var done = Math.Clamp((now.TimeOfDay - cur.Session.Start).TotalMinutes / span, 0, 1);
            stack.Children.Add(ProgressBar(done, key, animate));

            var foot = new Grid();
            foot.Children.Add(Ui.Text(TimeText.Format(cur.Session.Start), 11.5));
            var left = Ui.Text($"{TimeText.Until(cur.Session.End - now.TimeOfDay)} left", 11.5);
            left.HorizontalAlignment = HorizontalAlignment.Right;
            foot.Children.Add(left);
            stack.Children.Add(foot);
        }
        else
        {
            var off = TermCalendar.Of(data.Settings, now);
            stack.Children.Add(Ui.Caption(TermCalendar.ClassesRun(data.Settings, now) ? "No class right now" : $"No classes today · {off.Label}"));
        }

        if (info.Next is { } next && info.NextStart is { } at)
        {
            var box = new StackPanel();
            box.Children.Add(Ui.Rule(14, 12));
            box.Children.Add(Ui.Caption("Next"));
            var when = at.Date == now.Date ? $"in {TimeText.Until(at - now)}" : at.ToString("ddd") + " " + TimeText.Format(next.Session.Start);
            var line = Ui.Heading($"{next.Course.Code} {next.Session.Kind}", 14);
            line.Margin = new Thickness(0, 4, 0, 1);
            box.Children.Add(line);
            box.Children.Add(Ui.Text(string.Join(" · ", new[] { when, next.Session.Location }.Where(s => s.Length > 0)), 12.5, "Brush.Ink3"));
            stack.Children.Add(box);
        }
        return Card(stack);
    }

    private static UIElement ProgressBar(double fraction, string colorKey, bool animate)
    {
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 14, 0, 8), ClipToBounds = true };
        track.SetResourceReference(Border.BackgroundProperty, "Brush.S4");
        var fill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left, RenderTransformOrigin = new Point(0, 0.5) };
        fill.SetResourceReference(Border.BackgroundProperty, $"Course.{colorKey}.Dot");
        var scale = new ScaleTransform(0, 1);
        fill.RenderTransform = scale;
        track.Child = fill;
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width;
        scale.ScaleX = fraction;
        if (animate)
            track.Loaded += (_, _) => scale.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0, fraction, TimeSpan.FromMilliseconds(1200)) { EasingFunction = Ui.Smooth });
        return track;
    }

    public static UIElement TodayCard(ScheduleData data, DateTime now)
    {
        var stack = new StackPanel();
        var head = new Grid();
        head.Children.Add(Ui.Caption("Today"));
        var date = Ui.Text(now.ToString("ddd, MMM d"), 11.5, "Brush.Ink4");
        date.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(date);
        stack.Children.Add(head);

        var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var item in ScheduleQueries.Agenda(data, now))
        {
            var p = item.Item;
            var isNow = item.Status == AgendaStatus.Now;

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            row.ColumnDefinitions.Add(new ColumnDefinition());

            var dot = new Ellipse { Width = 6, Height = 6, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
            dot.SetResourceReference(Shape.FillProperty, $"Course.{p.Course.Color}.Dot");
            var time = Ui.Text(TimeText.Format(p.Session.Start, false), 11.5, isNow ? "Brush.Ink" : "Brush.Ink3");
            time.Margin = new Thickness(6, 2, 0, 0);
            Grid.SetColumn(time, 1);

            var text = new StackPanel();
            text.Children.Add(Ui.Heading($"{p.Course.Code} {p.Session.Kind}", 13));
            if (p.Session.Location.Length > 0) text.Children.Add(Ui.Text(p.Session.Location, 12, "Brush.Ink3"));
            Grid.SetColumn(text, 2);

            row.Children.Add(dot);
            row.Children.Add(time);
            row.Children.Add(text);

            var inner = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 6, 8, 6), Child = row };
            if (isNow) inner.SetResourceReference(Border.BackgroundProperty, "Brush.S3");
            if (item.Status == AgendaStatus.Done) inner.Opacity = 0.45;
            list.Children.Add(inner);
        }

        foreach (var task in data.Tasks.Where(t => !t.Done && t.Due.Date == now.Date).OrderBy(t => t.Due))
        {
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var time = Ui.Text(TimeText.Format(task.Due.TimeOfDay, false), 11.5, "Brush.Warn");
            time.Margin = new Thickness(6, 2, 0, 0);
            row.Children.Add(time);
            var text = Ui.Heading($"{task.Kind} due: {task.Title}", 13);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            list.Children.Add(new Border { Padding = new Thickness(8, 6, 8, 6), Child = row });
        }

        if (list.Children.Count == 0)
        {
            var off = TermCalendar.Of(data.Settings, now);
            list.Children.Add(Ui.Text(TermCalendar.ClassesRun(data.Settings, now) ? "Nothing scheduled today." : $"{off.Label}. No classes today.", 13, "Brush.Ink4"));
        }
        stack.Children.Add(list);
        return Card(stack);
    }

    public static UIElement ComingUpCard(ScheduleData data, DateTime now, Action onAdd, Action<TaskItem> onEdit, Action<TaskItem> onToggleDone)
    {
        var stack = new StackPanel();
        var head = new Grid();
        head.Children.Add(Ui.Caption("Coming up"));
        var add = Ui.IconButton("Icon.Plus", "Add assignment or exam");
        add.Width = add.Height = 24;
        add.HorizontalAlignment = HorizontalAlignment.Right;
        add.Click += (_, _) => onAdd();
        head.Children.Add(add);
        stack.Children.Add(head);

        var list = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var tasks = ScheduleQueries.Upcoming(data, now);
        foreach (var task in tasks.Take(6))
        {
            var course = data.FindCourse(task.CourseId);
            var overdue = task.Due < now;

            var check = new ToggleButton { Width = 16, Height = 16, Cursor = Cursors.Hand, ToolTip = "Mark done", VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 10, 0), Content = new Icon("Icon.Check", 11, "Brush.Ink") };
            check.SetResourceReference(Control.BorderBrushProperty, "Brush.Ink4");
            check.Template = CircleCheckTemplate();
            check.Click += (_, _) => onToggleDone(task);

            var text = new StackPanel();
            var title = Ui.Text(task.Title, 13, "Brush.Ink", FontWeights.Medium);
            text.Children.Add(title);
            var sub = $"{course?.Code} · {task.Kind} · {Due(task.Due, now)}";
            text.Children.Add(Ui.Text(sub, 12, overdue ? "Brush.Warn" : "Brush.Ink3"));

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(text, 1);
            row.Children.Add(check);
            row.Children.Add(text);

            var wrap = new Border { Padding = new Thickness(8, 6, 8, 6), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = row };
            wrap.MouseEnter += (_, _) => wrap.SetResourceReference(Border.BackgroundProperty, "Brush.Hover");
            wrap.MouseLeave += (_, _) => wrap.Background = Brushes.Transparent;
            wrap.MouseLeftButtonUp += (_, e) => { if (!e.Handled) onEdit(task); };
            list.Children.Add(wrap);
        }
        if (tasks.Count == 0) list.Children.Add(Ui.Text("No assignments or exams due in the next two weeks.", 12.5, "Brush.Ink4"));
        else if (tasks.Count > 6) list.Children.Add(Ui.Text($"+ {tasks.Count - 6} more", 12, "Brush.Ink4"));

        stack.Children.Add(list);
        return Card(stack);
    }

    private static ControlTemplate CircleCheckTemplate()
    {
        var template = new ControlTemplate(typeof(ToggleButton));
        var grid = new FrameworkElementFactory(typeof(Grid));
        var ring = new FrameworkElementFactory(typeof(Ellipse)) { Name = "ring" };
        ring.SetValue(Shape.StrokeThicknessProperty, 1.3);
        ring.SetValue(Shape.StrokeProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
        ring.SetValue(Shape.FillProperty, Brushes.Transparent);
        var mark = new FrameworkElementFactory(typeof(ContentPresenter)) { Name = "mark" };
        mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        mark.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        mark.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        grid.AppendChild(ring);
        grid.AppendChild(mark);
        template.VisualTree = grid;

        var checkedTrigger = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "mark"));
        template.Triggers.Add(checkedTrigger);
        return template;
    }

    private static string Due(DateTime due, DateTime now)
    {
        var days = (due.Date - now.Date).Days;
        var time = TimeText.Format(due.TimeOfDay);
        return days switch
        {
            < 0 => $"overdue since {due:MMM d}",
            0 => $"today {time}",
            1 => $"tomorrow {time}",
            < 7 => $"{due:ddd} {time}",
            _ => $"{due:MMM d}"
        };
    }

    public static UIElement CoursesList(ScheduleData data, Action<Course> onToggle, Action<Course, FrameworkElement> onContext, Guid? popped = null)
    {
        var stack = new StackPanel();
        var title = Ui.Text("My courses", 14, "Brush.Ink", FontWeights.Medium, "Font.Display");
        title.Margin = new Thickness(4, 0, 0, 6);
        stack.Children.Add(title);

        foreach (var course in data.Courses)
        {
            var key = course.Color;
            var box = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(2), VerticalAlignment = VerticalAlignment.Center };
            box.SetResourceReference(Border.BorderBrushProperty, $"Course.{key}.Dot");
            if (course.Visible)
            {
                box.SetResourceReference(Border.BackgroundProperty, $"Course.{key}.Dot");
                box.Child = new Icon("Icon.Check", 14, "Brush.OnEvent");
            }
            if (popped == course.Id) Motion.Pop(box);

            var label = Ui.Text(course.Name.Length > 0 ? $"{course.Code} · {course.Name}" : course.Code, 13.5, "Brush.Ink");
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(12, 0, 8, 0);
            var hours = Ui.Text($"{ScheduleQueries.WeeklyHours(data, course.Id):0.#} h", 12, "Brush.Ink4");
            hours.VerticalAlignment = VerticalAlignment.Center;

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(label, 1);
            Grid.SetColumn(hours, 2);
            row.Children.Add(box);
            row.Children.Add(label);
            row.Children.Add(hours);

            var button = new Button { Content = row, Height = Ui.Row(34, 28), Padding = new Thickness(4, 0, 8, 0), ToolTip = (course.Visible ? "Hide from the schedule" : "Show on the schedule") + ". Right-click for more." };
            button.SetResourceReference(FrameworkElement.StyleProperty, "MenuItem");
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            button.Click += (_, _) => onToggle(course);
            button.MouseRightButtonUp += (_, e) =>
            {
                onContext(course, button);
                e.Handled = true;
            };
            stack.Children.Add(button);
        }
        return stack;
    }
}
