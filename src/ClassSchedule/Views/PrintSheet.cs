using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

/// <summary>One page with a week of classes on it, always in the light look so it prints well and saves ink.</summary>
internal static class PrintSheet
{
    private const double PageMargin = 26;
    private const double GutterWidth = 46;
    private const double DayHeaderHeight = 44;
    private const double MaxHourHeight = 84;
    private const int LegendColumns = 3;
    private const int MaxDueRows = 6;

    /// <param name="page">The size to fill, in device-independent units (96 to the inch).</param>
    public static FrameworkElement Build(ScheduleData data, IReadOnlyList<DateTime> dates, Size page)
    {
        var theme = ThemeManager.Create(dark: false);
        var root = new Grid { Width = page.Width, Height = page.Height, Background = Brushes.White };
        root.Resources.MergedDictionaries.Add(theme);

        var stack = new StackPanel { Margin = new Thickness(PageMargin) };
        root.Children.Add(stack);

        var header = Header(data, dates);
        var dayRow = NewColumns(dates.Count);
        dayRow.Height = DayHeaderHeight;
        for (var i = 0; i < dates.Count; i++)
        {
            var cell = DayHeader(data.Settings, dates[i]);
            Grid.SetColumn(cell, i + 1);
            dayRow.Children.Add(cell);
        }
        var body = NewColumns(dates.Count);
        var footer = Footer(data, dates, theme);

        stack.Children.Add(header);
        stack.Children.Add(dayRow);
        stack.Children.Add(body);
        stack.Children.Add(footer);

        // The header and footer take what their text and margins need; the grid gets the rest of the page.
        Layout(root, page);
        var (startHour, endHour) = ScheduleQueries.HourRange(data);
        var rows = endHour - startHour;
        var free = page.Height - 2 * PageMargin - header.DesiredSize.Height - dayRow.DesiredSize.Height - footer.DesiredSize.Height;
        var hourHeight = Math.Clamp(free / rows, 20, MaxHourHeight);
        FillBody(body, data, dates, startHour, rows, hourHeight, theme);

        Layout(root, page);
        return root;
    }

    /// <summary>Draws the page to pixels at <paramref name="dpi"/>, as three bytes per pixel (red, green, blue).</summary>
    public static (int Width, int Height, byte[] Rgb) Rasterize(FrameworkElement sheet, Size page, double dpi)
    {
        var width = (int)Math.Round(page.Width * dpi / 96);
        var height = (int)Math.Round(page.Height * dpi / 96);
        var bitmap = new RenderTargetBitmap(width, height, dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(sheet);

        var stride = width * 4;
        var bgra = new byte[stride * height];
        bitmap.CopyPixels(bgra, stride, 0);

        var rgb = new byte[width * height * 3];
        for (int i = 0, o = 0; i < bgra.Length; i += 4, o += 3)
        {
            rgb[o] = bgra[i + 2];
            rgb[o + 1] = bgra[i + 1];
            rgb[o + 2] = bgra[i];
        }
        return (width, height, rgb);
    }

    private static void Layout(FrameworkElement root, Size page)
    {
        root.Measure(page);
        root.Arrange(new Rect(page));
        root.UpdateLayout();
    }

    /// <summary>“October 19 – 25, 2026”, or with both months when the week crosses one.</summary>
    public static string RangeText(IReadOnlyList<DateTime> dates)
    {
        var first = dates[0];
        var last = dates[^1];
        if (first.Year == last.Year && first.Month == last.Month)
            return $"{first.ToString("MMMM d", CultureInfo.CurrentCulture)} – {last.Day}, {last.Year}";
        return first.Year == last.Year
            ? $"{first.ToString("MMM d", CultureInfo.CurrentCulture)} – {last.ToString("MMM d", CultureInfo.CurrentCulture)}, {last.Year}"
            : $"{first.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} – {last.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}";
    }

    private static Grid NewColumns(int days)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GutterWidth) });
        for (var i = 0; i < days; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static FrameworkElement Header(ScheduleData data, IReadOnlyList<DateTime> dates)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        left.Children.Add(Ui.Text(data.Settings.TermName, 24, "Brush.Ink", FontWeights.Medium, "Font.Display"));
        left.Children.Add(Ui.Text(RangeText(dates), 13, "Brush.Ink3"));
        grid.Children.Add(left);

        var off = TermCalendar.OffLabel(data.Settings, dates);
        if (off.Length > 0)
        {
            var note = Ui.Badge(off);
            note.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(note, 1);
            grid.Children.Add(note);
        }
        return grid;
    }

    private static FrameworkElement DayHeader(Settings settings, DateTime date)
    {
        var off = !TermCalendar.ClassesRun(settings, date);
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var name = Ui.Text(date.ToString("ddd", CultureInfo.CurrentCulture).ToUpperInvariant(), 10.5, "Brush.Ink3", FontWeights.Medium);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        var number = Ui.Text(date.Day.ToString(CultureInfo.CurrentCulture), 20, off ? "Brush.Ink4" : "Brush.Ink", FontWeights.Normal, "Font.Display");
        number.HorizontalAlignment = HorizontalAlignment.Center;
        panel.Children.Add(name);
        panel.Children.Add(number);
        return panel;
    }

    private static void FillBody(Grid body, ScheduleData data, IReadOnlyList<DateTime> dates, int startHour, int rows, double hourHeight, ResourceDictionary theme)
    {
        body.Height = rows * hourHeight;

        for (var h = 0; h <= rows; h++)
        {
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, h * hourHeight - (h == rows ? 1 : 0), 0, 0) };
            line.SetResourceReference(Border.BackgroundProperty, "Brush.Line");
            Grid.SetColumn(line, 1);
            Grid.SetColumnSpan(line, dates.Count);
            body.Children.Add(line);
        }

        var gutter = new Canvas();
        for (var h = 1; h <= rows; h++)
        {
            var label = Ui.Text(TimeText.HourLabel(startHour + h), 9.5, "Brush.Ink3");
            label.Width = GutterWidth - 8;
            label.TextAlignment = TextAlignment.Right;
            Canvas.SetTop(label, h * hourHeight - 7);
            gutter.Children.Add(label);
        }
        body.Children.Add(gutter);

        for (var i = 0; i < dates.Count; i++)
        {
            var column = Column(data, dates[i], startHour, rows, hourHeight, theme);
            Grid.SetColumn(column, i + 1);
            body.Children.Add(column);
        }
    }

    private static FrameworkElement Column(ScheduleData data, DateTime date, int startHour, int rows, double hourHeight, ResourceDictionary theme)
    {
        var cell = new Border { BorderThickness = new Thickness(1, 0, 0, 0) };
        cell.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");
        var layers = new Grid();

        if (!TermCalendar.ClassesRun(data.Settings, date))
        {
            var shade = new Border();
            shade.SetResourceReference(Border.BackgroundProperty, "Brush.S2");
            shade.Opacity = 0.6;
            layers.Children.Add(shade);

            var label = Ui.Text(TermCalendar.Of(data.Settings, date).Label, 10, "Brush.Ink3", FontWeights.Medium);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(4, 8, 4, 0);
            label.VerticalAlignment = VerticalAlignment.Top;
            layers.Children.Add(label);
        }

        var panel = new EventColumnPanel { HourHeight = hourHeight, OriginMinutes = startHour * 60, TotalMinutes = rows * 60 };
        foreach (var placed in ScheduleQueries.SessionsOnDate(data, date))
        {
            var block = Block(data.Settings, placed, (placed.Session.End - placed.Session.Start).TotalMinutes * hourHeight / 60, theme);
            EventColumnPanel.SetStart(block, placed.Session.Start.TotalMinutes);
            EventColumnPanel.SetEnd(block, placed.Session.End.TotalMinutes);
            panel.Children.Add(block);
        }
        layers.Children.Add(panel);

        cell.Child = layers;
        return cell;
    }

    /// <summary>A class as a pale tint of its colour with a solid bar down the left, so it stays readable in black and white.</summary>
    private static FrameworkElement Block(Settings settings, PlacedSession placed, double height, ResourceDictionary theme)
    {
        var strong = CourseColor(theme, placed.Course.Color);
        var lines = new StackPanel { Margin = new Thickness(7, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        lines.Children.Add(Ui.Text(placed.Course.Code, 11.5, "Brush.Ink", FontWeights.SemiBold));

        var detail = settings.CardDetail == "Code"
            ? ""
            : string.Join(" · ", new[] { settings.CardDetail == "Room" ? "" : placed.Session.Kind, placed.Session.Location }.Where(s => s.Length > 0));
        if (height >= 34 && detail.Length > 0) lines.Children.Add(Ui.Text(detail, 10, "Brush.Ink2"));
        if (height >= 48) lines.Children.Add(Ui.Text(TimeText.Range(placed.Session.Start, placed.Session.End), 10, "Brush.Ink2"));

        var bar = new Border { Width = 4, HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(strong) };
        return new Border
        {
            Margin = new Thickness(2, 1, 3, 1),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Background = new SolidColorBrush(Mix(strong, Colors.White, 0.82)),
            Child = new Grid { Children = { bar, lines } }
        };
    }

    private static FrameworkElement Footer(ScheduleData data, IReadOnlyList<DateTime> dates, ResourceDictionary theme)
    {
        var grid = new Grid { Margin = new Thickness(GutterWidth, 12, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });

        var courses = dates.SelectMany(d => ScheduleQueries.SessionsOnDate(data, d)).Select(p => p.Course).DistinctBy(c => c.Id).ToList();
        var legend = new UniformGrid { Columns = LegendColumns };
        foreach (var course in courses)
        {
            var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(CourseColor(theme, course.Color)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            var text = Ui.Text(course.Name.Length > 0 ? $"{course.Code}  {course.Name}" : course.Code, 10.5, "Brush.Ink2");
            var row = new DockPanel { Margin = new Thickness(0, 0, 10, 4), VerticalAlignment = VerticalAlignment.Top };
            text.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(dot, Dock.Left);
            row.Children.Add(dot);
            row.Children.Add(text);
            legend.Children.Add(row);
        }
        grid.Children.Add(legend);

        var due = ScheduleQueries.DueBetween(data, dates[0], dates[^1]);
        if (due.Count > 0)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            var list = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            list.Children.Add(Ui.Text("Due this week", 10.5, "Brush.Ink", FontWeights.SemiBold));
            foreach (var task in due.Take(MaxDueRows))
            {
                var code = data.FindCourse(task.CourseId)?.Code ?? "";
                var when = $"{task.Due.ToString("ddd", CultureInfo.CurrentCulture)} {TimeText.Format(task.Due.TimeOfDay)}";
                list.Children.Add(Ui.Text($"{when} · {task.Kind}: {task.Title}{(code.Length > 0 ? $" ({code})" : "")}", 10.5, "Brush.Ink2"));
            }
            if (due.Count > MaxDueRows) list.Children.Add(Ui.Text($"…and {due.Count - MaxDueRows} more", 10.5, "Brush.Ink3"));
            Grid.SetColumn(list, 1);
            grid.Children.Add(list);
        }
        return grid;
    }

    private static Color CourseColor(ResourceDictionary theme, string key) =>
        theme[$"Course.{key}.Line"] is SolidColorBrush brush ? brush.Color : Colors.SteelBlue;

    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * amount), (byte)Math.Round(a.G + (b.G - a.G) * amount), (byte)Math.Round(a.B + (b.B - a.B) * amount));
}
