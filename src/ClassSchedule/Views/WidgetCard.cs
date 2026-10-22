using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

/// <summary>
/// What the desktop widget looks like: today's classes on a card, a glass panel or bare text. The widget window wraps one of these,
/// and the Settings window shows another as a live preview.
/// </summary>
public sealed class WidgetCard : Grid
{
    private const double Gutter = 14;

    private readonly Border background = new();
    private readonly Border outline = new() { IsHitTestVisible = false };
    private readonly Border body = new() { Padding = new Thickness(20, 16, 20, 16) };
    private readonly Border scaled = new();
    private Settings? applied;
    private bool compact;

    public WidgetCard()
    {
        var card = new Grid { Margin = new Thickness(Gutter) };
        card.Children.Add(background);
        card.Children.Add(outline);
        card.Children.Add(body);
        scaled.Child = card;
        Children.Add(scaled);

        ThemeManager.Changed += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.Changed -= OnThemeChanged;
        Loaded += (_, _) =>
        {
            ThemeManager.Changed -= OnThemeChanged;
            ThemeManager.Changed += OnThemeChanged;
        };
    }

    /// <summary>Whether anything is left to show today: a class that hasn't finished, or work due.</summary>
    public bool HasContent { get; private set; } = true;

    /// <summary>The window width the card needs for these settings, shadow margin and scale included.</summary>
    public static double WidthFor(Settings s) => ((s.WidgetCompact ? 262 : 296) + 2 * Gutter) * Scale(s);

    /// <summary>The transparent border around the card that holds its shadow, after scaling.</summary>
    public static double GutterFor(Settings s) => Gutter * Scale(s);

    private static double Scale(Settings s) => Math.Clamp(s.WidgetScale, 80, 150) / 100.0;

    private void OnThemeChanged()
    {
        if (applied is not null) Apply(applied);
    }

    public void Apply(Settings s)
    {
        applied = s;
        compact = s.WidgetCompact;
        var style = s.WidgetStyle;
        var minimal = style == "Minimal";

        Resources.MergedDictionaries.Clear();
        if (s.WidgetTheme is "Light" or "Dark") Resources.MergedDictionaries.Add(ThemeManager.Create(s.WidgetTheme == "Dark"));
        var dark = s.WidgetTheme switch { "Dark" => true, "Light" => false, _ => ThemeManager.IsDark };

        var radius = new CornerRadius(Math.Clamp(s.WidgetRadius, 0, 32));
        background.CornerRadius = radius;
        outline.CornerRadius = radius;
        var percent = Math.Clamp(s.WidgetOpacity, 55, 100) / 100.0;

        if (minimal)
        {
            background.Background = Brushes.Transparent;
            background.Effect = null;
            outline.BorderThickness = new Thickness(0);
            body.Effect = s.WidgetShadow
                ? new DropShadowEffect { BlurRadius = 9, ShadowDepth = 1, Direction = 270, Opacity = 0.7, Color = dark ? Colors.Black : Colors.White }
                : null;
        }
        else
        {
            var glass = style == "Glass";
            background.SetResourceReference(Border.BackgroundProperty, "Brush.S1");
            background.Opacity = glass ? Math.Max(0.3, percent * 0.55) : percent;
            background.Effect = s.WidgetShadow
                ? new DropShadowEffect { BlurRadius = glass ? 16 : 22, ShadowDepth = 4, Direction = 270, Opacity = glass ? 0.2 : 0.28, Color = Colors.Black }
                : null;
            outline.BorderThickness = new Thickness(glass ? 1 : 0);
            outline.SetResourceReference(Border.BorderBrushProperty, "Brush.LineStrong");
            body.Effect = null;
        }

        var scale = Scale(s);
        scaled.LayoutTransform = scale == 1 ? Transform.Identity : new ScaleTransform(scale, scale);
        scaled.Width = (compact ? 262 : 296) + 2 * Gutter;
    }

    public void Refresh(ScheduleData data, DateTime now)
    {
        var prefs = data.Settings;
        var stack = new StackPanel();

        var agenda = ScheduleQueries.Agenda(data, now).Where(i => prefs.WidgetShowDone || i.Status != AgendaStatus.Done).ToList();
        var todays = data.Tasks.Where(t => !t.Done && t.Due.Date == now.Date).OrderBy(t => t.Due).ToList();
        HasContent = agenda.Any(i => i.Status != AgendaStatus.Done) || todays.Count > 0;

        var hidden = 0;
        if (prefs.WidgetMaxRows > 0)
        {
            while (agenda.Count > prefs.WidgetMaxRows && agenda[0].Status == AgendaStatus.Done)
            {
                agenda.RemoveAt(0);
                hidden++;
            }
            if (agenda.Count > prefs.WidgetMaxRows)
            {
                hidden += agenda.Count - prefs.WidgetMaxRows;
                agenda = agenda.Take(prefs.WidgetMaxRows).ToList();
            }
        }

        var showHeader = prefs.WidgetShowHeader;
        if (showHeader || prefs.WidgetShowClock) stack.Children.Add(Header(now, agenda.Count + hidden, showHeader, prefs.WidgetShowClock));

        var list = new StackPanel { Margin = new Thickness(-8, showHeader || prefs.WidgetShowClock ? 12 : -4, -8, 0) };
        foreach (var item in agenda) list.Children.Add(ClassRow(item, now, compact, prefs));
        if (hidden > 0)
        {
            var more = Ui.Text($"+ {hidden} more", 12, "Brush.Ink3");
            more.Margin = new Thickness(10, 2, 10, 4);
            list.Children.Add(more);
        }
        foreach (var task in todays) list.Children.Add(TaskRow(task));

        if (prefs.WidgetShowTomorrow && agenda.All(i => i.Status == AgendaStatus.Done))
        {
            var tomorrow = now.Date.AddDays(1);
            var coming = ScheduleQueries.SessionsOnDate(data, tomorrow, onlyVisible: false);
            if (coming.Count > 0)
            {
                var label = Ui.Caption($"Tomorrow, {tomorrow:ddd MMM d}");
                label.Margin = new Thickness(10, list.Children.Count > 0 ? 10 : 0, 0, 2);
                list.Children.Add(label);
                foreach (var p in coming) list.Children.Add(ClassRow(new AgendaItem(p, AgendaStatus.Upcoming), now, compact, prefs));
            }
        }

        if (list.Children.Count == 0) list.Children.Add(EmptyNote(data, now));
        stack.Children.Add(list);

        if (prefs.WidgetShowDue && ScheduleQueries.NextDue(data, now) is { } due)
        {
            stack.Children.Add(Ui.Rule(12, 10));
            var foot = new Grid();
            foot.Children.Add(Ui.Text("Next due", 12.5, "Brush.Ink3"));
            var what = Ui.Heading($"{due.Title}  ·  {Due(due.Due, now)}", 12.5);
            what.HorizontalAlignment = HorizontalAlignment.Right;
            what.MaxWidth = compact ? 170 : 200;
            foot.Children.Add(what);
            stack.Children.Add(foot);
        }

        body.Child = stack;
    }

    private UIElement Header(DateTime now, int classes, bool date, bool clock)
    {
        var head = new Grid();
        if (date)
        {
            var titles = new StackPanel();
            titles.Children.Add(Ui.Caption("Today"));
            titles.Children.Add(Ui.Heading(now.ToString("ddd, MMM d"), compact ? 18 : 22));
            head.Children.Add(titles);
        }

        var right = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top };
        if (clock)
        {
            var time = Ui.Heading(TimeText.Format(now.TimeOfDay), compact ? 18 : 22);
            time.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(time);
        }
        if (date && classes > 0)
        {
            var count = Ui.Text($"{classes} {(classes == 1 ? "class" : "classes")}", 12, "Brush.Ink3");
            count.HorizontalAlignment = HorizontalAlignment.Right;
            right.Children.Add(count);
        }
        head.Children.Add(right);
        return head;
    }

    private static UIElement EmptyNote(ScheduleData data, DateTime now)
    {
        var off = TermCalendar.Of(data.Settings, now);
        var info = ScheduleQueries.Now(data, now);
        var lead = TermCalendar.ClassesRun(data.Settings, now) ? "Nothing today" : off.Label;
        var text = info.Next is { } next && info.NextStart is { } at
            ? $"{lead}. Next: {next.Course.Code} {next.Session.Kind}, {at:ddd} {TimeText.Format(next.Session.Start)}."
            : $"{lead}.";
        var empty = Ui.Text(text, 13, "Brush.Ink3");
        empty.TextWrapping = TextWrapping.Wrap;
        empty.TextTrimming = TextTrimming.None;
        empty.Margin = new Thickness(8, 4, 8, 4);
        return empty;
    }

    private static UIElement ClassRow(AgendaItem item, DateTime now, bool compact, Settings prefs)
    {
        var p = item.Item;
        var isNow = item.Status == AgendaStatus.Now;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var time = Ui.Text(TimeText.Format(p.Session.Start), 12, isNow ? "Brush.Now" : "Brush.Ink3", isNow ? FontWeights.SemiBold : FontWeights.Normal);
        time.Margin = new Thickness(0, 2, 0, 0);
        grid.Children.Add(time);

        var text = new StackPanel();
        var dot = new Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, $"Course.{p.Course.Color}.Dot");
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(dot);
        title.Children.Add(Ui.Heading($"{p.Course.Code} {p.Session.Kind}", 14));
        text.Children.Add(title);

        if (!compact)
        {
            var where = string.Join(" · ", new[] { prefs.WidgetShowLocation ? p.Session.Location : "", isNow ? p.Course.Instructor : "" }.Where(s => s.Length > 0));
            if (where.Length > 0) text.Children.Add(Ui.Text(where, 12.5, "Brush.Ink3"));

            if (isNow && prefs.WidgetShowProgress)
            {
                var span = (p.Session.End - p.Session.Start).TotalMinutes;
                var done = Math.Clamp((now.TimeOfDay - p.Session.Start).TotalMinutes / span, 0, 1);
                text.Children.Add(Bar(done, p.Course.Color));
                var foot = new Grid();
                foot.Children.Add(Ui.Text(TimeText.Format(p.Session.Start), 11.5, "Brush.Ink3"));
                var left = Ui.Text($"{TimeText.Until(p.Session.End - now.TimeOfDay)} left", 11.5, "Brush.Ink3");
                left.HorizontalAlignment = HorizontalAlignment.Right;
                foot.Children.Add(left);
                text.Children.Add(foot);
            }
        }
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var row = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, compact ? 5 : 8, 10, compact ? 5 : 8), Child = grid };
        if (isNow && prefs.WidgetStyle != "Minimal") row.SetResourceReference(Border.BackgroundProperty, "Brush.S2");
        if (item.Status == AgendaStatus.Done) row.Opacity = 0.45;
        return row;
    }

    private static UIElement TaskRow(TaskItem task)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(66) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var time = Ui.Text(TimeText.Format(task.Due.TimeOfDay), 12, "Brush.Warn");
        time.Margin = new Thickness(0, 2, 0, 0);
        grid.Children.Add(time);
        var text = Ui.Heading($"{task.Kind} due: {task.Title}", 13.5);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return new Border { Padding = new Thickness(10, 6, 10, 6), Child = grid };
    }

    private static UIElement Bar(double fraction, string colorKey)
    {
        var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 9, 0, 5), ClipToBounds = true };
        track.SetResourceReference(Border.BackgroundProperty, "Brush.S4");
        var fill = new Border { CornerRadius = new CornerRadius(2), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, $"Course.{colorKey}.Dot");
        track.Child = fill;
        track.SizeChanged += (_, e) => fill.Width = e.NewSize.Width * fraction;
        return track;
    }

    private static string Due(DateTime due, DateTime now)
    {
        var days = (due.Date - now.Date).Days;
        var time = TimeText.Format(due.TimeOfDay);
        return days switch
        {
            1 => $"tomorrow {time}",
            < 7 => $"{due:ddd} {time}",
            _ => $"{due:MMM d}"
        };
    }
}
