using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public sealed class ScheduleGridView : UserControl
{
    public ScheduleGridView()
    {
        Focusable = false;
        FocusVisualStyle = null;
    }

    private double HourHeight = 76;
    private const double GutterWidth = 60;
    private const double ScrollBarWidth = 10;
    private const double TwoLineHeight = 50;
    private const double ThreeLineHeight = 66;

    private ScrollViewer? scroller;
    private readonly Dictionary<DateTime, Grid> columns = new();
    private readonly List<FrameworkElement> cards = new();
    private FrameworkElement? hint;
    private double originMinutes;

    public event Action<PlacedSession, FrameworkElement>? SessionClicked;
    public event Action<PlacedSession, FrameworkElement, DayOfWeek>? SessionContext;
    public event Action<DateTime, TimeSpan, FrameworkElement>? SlotContext;
    public event Action<DateTime, FrameworkElement>? DayContext;

    /// <summary>Marks the half hour a right-click landed on while its menu is open.</summary>
    public void ShowSlotHint(DateTime date, TimeSpan start)
    {
        ClearSlotHint();
        if (!columns.TryGetValue(date.Date, out var layers)) return;

        var rect = new System.Windows.Shapes.Rectangle { RadiusX = 10, RadiusY = 10, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 } };
        rect.SetResourceReference(Shape.FillProperty, "Brush.PrimaryTint");
        rect.SetResourceReference(Shape.StrokeProperty, "Brush.Primary");
        var label = Ui.Text($"New class · {TimeText.Format(start)}", 12, "Brush.Primary", FontWeights.SemiBold);
        label.Margin = new Thickness(10, 5, 6, 0);
        label.VerticalAlignment = VerticalAlignment.Top;

        var box = new Grid { VerticalAlignment = VerticalAlignment.Top, Height = HourHeight * ClassDefaults.Minutes / 60, IsHitTestVisible = false };
        box.Margin = new Thickness(4, (start.TotalMinutes - originMinutes) * HourHeight / 60 + 1, 10, 0);
        box.Children.Add(rect);
        box.Children.Add(label);
        layers.Children.Add(box);
        hint = box;
        Motion.Rise(box, 0, 200, 0, Motion.Out, 0.94);
    }

    public void ClearSlotHint()
    {
        if (hint?.Parent is Panel parent) parent.Children.Remove(hint);
        hint = null;
    }

    /// <summary>Drops the rendered week so it can be collected while the window is hidden.</summary>
    public void Release()
    {
        Content = null;
        scroller = null;
    }

    public void Render(ScheduleData data, IReadOnlyList<DateTime> dates, DateTime now, Guid? selected, bool keepScroll, bool animate = false)
    {
        var keptOffset = keepScroll ? scroller?.VerticalOffset : null;
        HourHeight = data.Settings.HourHeightMode switch { "Compact" => 58, "Spacious" => 94, _ => 76 };
        columns.Clear();
        cards.Clear();
        hint = null;
        var (startHour, endHour) = ScheduleQueries.HourRange(data);
        originMinutes = startHour * 60;
        var rows = endHour - startHour;
        var total = rows * HourHeight;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var header = NewColumns(dates.Count);
        header.Margin = new Thickness(0, 12, ScrollBarWidth, 6);
        for (var i = 0; i < dates.Count; i++)
        {
            var cell = DayHeader(dates[i], dates[i].Date == now.Date);
            Grid.SetColumn(cell, i + 1);
            header.Children.Add(cell);
        }
        root.Children.Add(header);

        var body = NewColumns(dates.Count);
        body.Height = total;

        for (var h = 0; h <= rows; h++)
        {
            var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, h * HourHeight - (h == rows ? 1 : 0), 0, 0), IsHitTestVisible = false };
            line.SetResourceReference(Border.BackgroundProperty, "Brush.Line");
            Grid.SetColumn(line, 1);
            Grid.SetColumnSpan(line, dates.Count);
            body.Children.Add(line);
        }

        var gutter = new Canvas { Height = total };
        for (var h = 1; h <= rows; h++)
        {
            var hour = startHour + h;
            var label = Ui.Text(TimeText.HourLabel(hour), 10.5, "Brush.Ink3");
            label.Width = GutterWidth - 10;
            label.TextAlignment = TextAlignment.Right;
            Canvas.SetTop(label, h * HourHeight - 7);
            gutter.Children.Add(label);
        }
        body.Children.Add(gutter);

        var nowMinutes = now.TimeOfDay.TotalMinutes;
        for (var i = 0; i < dates.Count; i++)
        {
            var column = Column(data, dates[i], now, startHour, rows, selected);
            Grid.SetColumn(column, i + 1);
            body.Children.Add(column);
        }

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = body,
            Padding = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);
        Content = new Border { Padding = new Thickness(4, 0, 0, 0), Child = root };
        scroller = scroll;

        var focus = keptOffset ?? (dates.Any(d => d.Date == now.Date) ? Math.Max(0, (nowMinutes - startHour * 60) * HourHeight / 60 - HourHeight * 1.5) : 0);
        scroll.Loaded += (_, _) => scroll.ScrollToVerticalOffset(focus);

        if (animate)
            for (var i = 0; i < cards.Count; i++) Motion.Rise(cards[i], 8, 480, i * 28, Motion.Out, 0.96);
    }

    private static Grid NewColumns(int days)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(GutterWidth) });
        for (var i = 0; i < days; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 90 });
        return grid;
    }

    private UIElement DayHeader(DateTime date, bool today)
    {
        var name = Ui.Text(date.ToString("ddd").ToUpperInvariant(), 11, today ? "Brush.Primary" : "Brush.Ink3", FontWeights.Medium);
        name.HorizontalAlignment = HorizontalAlignment.Center;

        var number = Ui.Text(date.Day.ToString(), 24, today ? "Brush.OnPrimary" : "Brush.Ink", FontWeights.Normal, "Font.Display");
        number.HorizontalAlignment = HorizontalAlignment.Center;
        number.VerticalAlignment = VerticalAlignment.Center;
        var disc = new Border { Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Margin = new Thickness(0, 2, 0, 0), Child = number };
        if (today) disc.SetResourceReference(Border.BackgroundProperty, "Brush.Primary");

        var column = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        column.Children.Add(name);
        column.Children.Add(disc);

        var hit = new Border { Child = column, CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 2, 10, 4), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center };
        hit.MouseEnter += (_, _) =>
        {
            hit.SetResourceReference(Border.BackgroundProperty, "Brush.Hover");
            Motion.Pop(disc, 0.92, 300);
        };
        hit.MouseLeave += (_, _) => hit.Background = Brushes.Transparent;
        hit.MouseRightButtonUp += (_, e) =>
        {
            DayContext?.Invoke(date, hit);
            e.Handled = true;
        };
        return hit;
    }

    private UIElement Column(ScheduleData data, DateTime date, DateTime now, int startHour, int rows, Guid? selected)
    {
        var today = date.Date == now.Date;
        var nowMinutes = now.TimeOfDay.TotalMinutes;
        var cell = new Border { BorderThickness = new Thickness(1, 0, 0, 0) };
        cell.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");
        if (today) cell.SetResourceReference(Border.BackgroundProperty, "Brush.Today");
        else cell.Background = Brushes.Transparent;

        var layers = new Grid();
        columns[date.Date] = layers;
        cell.MouseRightButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            var slot = Math.Clamp((int)Math.Floor(e.GetPosition(layers).Y / HourHeight * 2), 0, rows * 2 - 1);
            SlotContext?.Invoke(date, TimeSpan.FromMinutes(startHour * 60 + slot * 30), cell);
            e.Handled = true;
        };
        var panel = new EventColumnPanel { HourHeight = HourHeight, OriginMinutes = startHour * 60, TotalMinutes = rows * 60 };
        foreach (var placed in ScheduleQueries.SessionsOn(data, date.DayOfWeek))
        {
            var card = Card(data.Settings, placed, placed.Session.Id == selected, date.DayOfWeek,
                data.Settings.DimPast && (date.Date < now.Date || (today && placed.Session.End <= now.TimeOfDay)));
            EventColumnPanel.SetStart(card, placed.Session.Start.TotalMinutes);
            EventColumnPanel.SetEnd(card, placed.Session.End.TotalMinutes);
            panel.Children.Add(card);
            cards.Add(card);
        }
        layers.Children.Add(panel);

        if (today && data.Settings.ShowNowLine && nowMinutes >= startHour * 60 && nowMinutes <= (startHour + rows) * 60)
        {
            var y = (nowMinutes - startHour * 60) * HourHeight / 60;
            var line = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, y - 1, 0, 0), IsHitTestVisible = false };
            line.SetResourceReference(Border.BackgroundProperty, "Brush.Now");
            var ring = new Ellipse { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, y - 6, 0, 0), IsHitTestVisible = false, Opacity = 0 };
            ring.SetResourceReference(Shape.FillProperty, "Brush.Now");
            layers.Children.Add(ring);
            Motion.Ping(ring);
            var dot = new Ellipse { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(-6, y - 6, 0, 0), IsHitTestVisible = false };
            dot.SetResourceReference(Shape.FillProperty, "Brush.Now");
            layers.Children.Add(line);
            layers.Children.Add(dot);
        }

        cell.Child = layers;
        return cell;
    }

    private FrameworkElement Card(Settings settings, PlacedSession placed, bool selected, DayOfWeek day, bool past)
    {
        var key = placed.Course.Color;
        var height = (placed.Session.End - placed.Session.Start).TotalMinutes * HourHeight / 60;
        var range = TimeText.Range(placed.Session.Start, placed.Session.End);

        // A card loses about 13 px to margin, border and padding, and its lines are about 17 px, 15 px and 15 px tall.
        // A line is only shown when all of it fits, so short classes get one tidy line instead of a half-cut second one.
        var codeOnly = settings.CardDetail == "Code";
        var single = codeOnly || height < TwoLineHeight;
        var stack = new StackPanel { Margin = new Thickness(8, 0, 6, 0), VerticalAlignment = single ? VerticalAlignment.Center : VerticalAlignment.Top };
        var heading = codeOnly ? placed.Course.Code : single ? $"{placed.Course.Code}, {TimeText.Format(placed.Session.Start)}" : placed.Course.Code;
        stack.Children.Add(Ui.Text(heading, 12, "Brush.OnEvent", FontWeights.Medium));
        if (!single)
        {
            var parts = settings.CardDetail == "Room" ? new[] { placed.Session.Location } : new[] { placed.Session.Kind, placed.Session.Location };
            var detail = string.Join(" · ", parts.Where(s => s.Length > 0));
            if (detail.Length > 0) stack.Children.Add(Ui.Text(detail, 11, "Brush.OnEvent"));
        }
        if (settings.CardDetail == "Fit" && !single && height >= ThreeLineHeight) stack.Children.Add(Ui.Text(range, 11, "Brush.OnEvent"));

        var card = new Border
        {
            Margin = new Thickness(2, 1, 8, 1),
            Padding = new Thickness(0, 4, 0, 3),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(2),
            Cursor = Cursors.Hand,
            Focusable = true,
            FocusVisualStyle = null,
            ClipToBounds = true,
            Tag = "event",
            Child = stack
        };
        if (past)
        {
            var veil = new Border { IsHitTestVisible = false, Opacity = 0.55, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, -4, 0, -3) };
            veil.SetResourceReference(Border.BackgroundProperty, "Brush.S1");
            card.Child = new Grid { Children = { stack, veil } };
        }
        var rest = $"Course.{key}.Line";
        card.SetResourceReference(Border.BackgroundProperty, $"Course.{key}.Bg");
        card.SetResourceReference(Border.BorderBrushProperty, selected ? "Brush.Ink" : rest);
        AutomationProperties.SetName(card, $"{placed.Course.Code} {placed.Session.Kind}, {range}");

        card.GotKeyboardFocus += (_, _) => { if (!ReferenceEquals(card.Tag, "open")) card.SetResourceReference(Border.BorderBrushProperty, "Brush.Ink"); };
        card.LostKeyboardFocus += (_, _) => { if (!ReferenceEquals(card.Tag, "open")) card.SetResourceReference(Border.BorderBrushProperty, rest); };
        Motion.AttachLift(card);
        card.MouseRightButtonUp += (_, e) =>
        {
            SessionContext?.Invoke(placed, card, day);
            e.Handled = true;
        };
        card.MouseLeftButtonUp += (_, e) => { SessionClicked?.Invoke(placed, card); e.Handled = true; };
        card.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { SessionClicked?.Invoke(placed, card); e.Handled = true; }
        };
        return card;
    }
}
