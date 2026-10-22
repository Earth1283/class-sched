using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public static class MiniMonth
{
    private const double Cell = 32;

    public static UIElement Build(DateTime month, DateTime today, DateTime weekStart, int shownDays, Settings settings, Action<DateTime> onPick, Action<int> onStep)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var root = new StackPanel();

        var title = Ui.Text(first.ToString("MMMM yyyy"), 14, "Brush.Ink", FontWeights.Medium, "Font.Display");
        title.VerticalAlignment = VerticalAlignment.Center;

        var prev = Ui.IconButton("Icon.ChevL", "Previous month");
        var next = Ui.IconButton("Icon.ChevR", "Next month");
        prev.Width = prev.Height = next.Width = next.Height = 28;
        prev.Click += (_, _) => onStep(-1);
        next.Click += (_, _) => onStep(1);
        var arrows = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        arrows.Children.Add(prev);
        arrows.Children.Add(next);

        var head = new Grid { Margin = new Thickness(4, 0, 0, 6) };
        head.Children.Add(title);
        head.Children.Add(arrows);
        root.Children.Add(head);

        var grid = new UniformGrid { Columns = 7 };
        foreach (var weekday in Enumerable.Range(0, 7).Select(i => (DayOfWeek)(((int)DayParser.FirstDay + i) % 7)))
        {
            var t = Ui.Text(weekday.ToString()[..1], 11, "Brush.Ink3", FontWeights.Medium);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Height = 24;
            t.Margin = new Thickness(0, 4, 0, 0);
            grid.Children.Add(t);
        }

        var start = ScheduleQueries.WeekStart(first);
        for (var i = 0; i < 42; i++)
        {
            var date = start.AddDays(i);
            if (i >= 35 && date.Month != first.Month) { grid.Children.Add(new Border { Height = Cell }); continue; }
            grid.Children.Add(Day(date, first.Month, today, weekStart, shownDays, TermCalendar.Of(settings, date), onPick));
        }
        root.Children.Add(grid);
        return root;
    }

    private static UIElement Day(DateTime date, int month, DateTime today, DateTime weekStart, int shownDays, DayStatus status, Action<DateTime> onPick)
    {
        var isToday = date.Date == today.Date;
        var inWeek = date.Date >= weekStart.Date && date.Date < weekStart.Date.AddDays(shownDays);
        var foreground = isToday ? "Brush.OnPrimary" : date.Month == month && !status.IsOff ? "Brush.Ink" : "Brush.Ink4";

        var label = Ui.Text(date.Day.ToString(), 12, foreground, isToday ? FontWeights.Medium : FontWeights.Normal);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;

        var disc = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = label, Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center };
        if (isToday) disc.SetResourceReference(Border.BackgroundProperty, "Brush.Primary");

        var cell = new Border { Height = Cell, Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
        if (inWeek)
        {
            var first = date.Date == weekStart.Date;
            var last = date.Date == weekStart.Date.AddDays(shownDays - 1);
            var band = new Border { Margin = new Thickness(0, 2, 0, 2), Child = disc, CornerRadius = new CornerRadius(first ? 14 : 0, last ? 14 : 0, last ? 14 : 0, first ? 14 : 0) };
            band.SetResourceReference(Border.BackgroundProperty, "Brush.PrimaryTint");
            cell.Child = band;
        }
        else
        {
            cell.Child = disc;
            cell.MouseEnter += (_, _) => { if (!isToday) disc.SetResourceReference(Border.BackgroundProperty, "Brush.Hover"); };
            cell.MouseLeave += (_, _) => { if (!isToday) disc.Background = Brushes.Transparent; };
        }
        if (status.Kind == DayKind.Break)
        {
            var mark = new System.Windows.Shapes.Ellipse { Width = 4, Height = 4, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1), IsHitTestVisible = false };
            mark.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.Ink3");
            var inner = cell.Child;
            cell.Child = null;
            cell.Child = new Grid { Children = { inner, mark } };
        }
        if (status.IsOff) cell.ToolTip = status.Label;
        cell.MouseLeftButtonUp += (_, e) => { onPick(date); e.Handled = true; };
        return cell;
    }
}
