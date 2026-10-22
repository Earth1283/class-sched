using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public static class ListViewBuilder
{
    public static UIElement Build(ScheduleData data, IReadOnlyList<DateTime> dates, DateTime now, Action<PlacedSession, FrameworkElement> onClick, Action<PlacedSession, FrameworkElement, DayOfWeek> onContext)
    {
        var root = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (var date in dates)
        {
            var sessions = ScheduleQueries.SessionsOnDate(data, date);
            root.Children.Add(GroupHeader(date, sessions.Count, date.Date == now.Date));
            if (sessions.Count == 0) root.Children.Add(Empty(TermCalendar.ClassesRun(data.Settings, date) ? "" : TermCalendar.Of(data.Settings, date).Label));
            foreach (var placed in sessions) root.Children.Add(Row(placed, onClick, onContext, date.DayOfWeek));
        }
        return new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }

    private static UIElement GroupHeader(DateTime date, int count, bool today)
    {
        var number = Ui.Text(date.Day.ToString(), 15, today ? "Brush.OnPrimary" : "Brush.Ink", FontWeights.Medium, "Font.Display");
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.VerticalAlignment = VerticalAlignment.Center;
        var disc = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(16), Child = number };
        if (today) disc.SetResourceReference(Border.BackgroundProperty, "Brush.Primary");

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(disc);
        var label = Ui.Text($"{date:dddd}, {date:MMM d}", 14, today ? "Brush.Primary" : "Brush.Ink", FontWeights.Medium);
        label.Margin = new Thickness(12, 0, 0, 0);
        label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);

        var total = Ui.Text(count == 0 ? "" : $"{count} {(count == 1 ? "class" : "classes")}", 12, "Brush.Ink4");
        total.HorizontalAlignment = HorizontalAlignment.Right;
        total.VerticalAlignment = VerticalAlignment.Center;

        var grid = new Grid();
        grid.Children.Add(row);
        grid.Children.Add(total);

        var bar = new Border { Padding = new Thickness(20, 0, 24, 0), Height = Ui.Row(52, 42), Child = grid, BorderThickness = new Thickness(0, 0, 0, 1) };
        bar.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");
        return bar;
    }

    private static UIElement Empty(string reason)
    {
        var text = Ui.Text(reason.Length > 0 ? $"No classes · {reason}" : "No classes", 13, "Brush.Ink4");
        text.Margin = new Thickness(64, 0, 20, 12);
        return text;
    }

    private static UIElement Row(PlacedSession placed, Action<PlacedSession, FrameworkElement> onClick, Action<PlacedSession, FrameworkElement, DayOfWeek> onContext, DayOfWeek day)
    {
        var key = placed.Course.Color;
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new Ellipse { Width = 12, Height = 12, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shape.FillProperty, $"Course.{key}.Dot");

        var time = Ui.Text(TimeText.Range(placed.Session.Start, placed.Session.End), 12.5, "Brush.Ink3");
        time.VerticalAlignment = VerticalAlignment.Center;
        var code = Ui.Heading(placed.Course.Code, 13.5);
        code.VerticalAlignment = VerticalAlignment.Center;

        var name = Ui.Text(placed.Course.Name, 13, "Brush.Ink2");
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(0, 0, 12, 0);

        var tail = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (placed.Session.Location.Length > 0)
        {
            var room = Ui.Text(placed.Session.Location, 12.5, "Brush.Ink3");
            room.Margin = new Thickness(0, 0, 10, 0);
            room.VerticalAlignment = VerticalAlignment.Center;
            tail.Children.Add(room);
        }
        tail.Children.Add(Ui.Badge(placed.Session.Kind));

        Grid.SetColumn(time, 1);
        Grid.SetColumn(code, 2);
        Grid.SetColumn(name, 3);
        Grid.SetColumn(tail, 4);
        grid.Children.Add(dot);
        grid.Children.Add(time);
        grid.Children.Add(code);
        grid.Children.Add(name);
        grid.Children.Add(tail);

        var row = new Border
        {
            Child = grid,
            Height = Ui.Row(44, 36),
            Padding = new Thickness(20, 0, 24, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Cursor = Cursors.Hand,
            Focusable = true,
            FocusVisualStyle = null
        };
        row.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");
        row.GotKeyboardFocus += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Brush.Hover");
        row.LostKeyboardFocus += (_, _) => row.Background = Brushes.Transparent;
        row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "Brush.Hover");
        row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
        row.MouseLeftButtonUp += (_, e) => { onClick(placed, row); e.Handled = true; };
        row.MouseRightButtonUp += (_, e) => { onContext(placed, row, day); e.Handled = true; };
        row.KeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { onClick(placed, row); e.Handled = true; } };
        return row;
    }
}
