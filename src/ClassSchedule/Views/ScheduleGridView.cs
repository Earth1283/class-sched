using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

/// <summary>A class meeting dragged to a new day or time, or resized, and not yet applied.</summary>
public sealed record SessionMove(PlacedSession Placed, DayOfWeek FromDay, DayOfWeek ToDay, TimeSpan Start, TimeSpan End, FrameworkElement Card);

public sealed class ScheduleGridView : UserControl
{
    public ScheduleGridView()
    {
        Focusable = false;
        FocusVisualStyle = null;
        autoscroll.Tick += (_, _) => AutoScroll();
    }

    private double HourHeight = 76;
    private const double GutterWidth = 60;
    private const double ScrollBarWidth = 10;
    private const double TwoLineHeight = 50;
    private const double ThreeLineHeight = 66;
    private const double MinColumnWidth = 90;
    private const double EdgeZone = 7;
    private const double DragThreshold = 4;
    private const int SnapMinutes = 5;

    private enum DragMode { Move, ResizeTop, ResizeBottom }

    private sealed class Drag
    {
        public required PlacedSession Placed { get; init; }
        public required FrameworkElement Card { get; init; }
        public required DayOfWeek Day { get; init; }
        public required int Column { get; set; }
        public required DragMode Mode { get; init; }
        public required Point Press { get; init; }
        public required double GrabMinutes { get; init; }
        public bool Active { get; set; }
        public double Start { get; set; }
        public double End { get; set; }
        public Border? Ghost { get; set; }
        public TextBlock? Detail { get; set; }
    }

    private ScrollViewer? scroller;
    private Grid? body;
    private Canvas? dragLayer;
    private IReadOnlyList<DateTime> dates = Array.Empty<DateTime>();
    private readonly Dictionary<DateTime, Grid> columns = new();
    private readonly List<FrameworkElement> cards = new();
    private readonly DispatcherTimer autoscroll = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private FrameworkElement? hint;
    private Drag? drag;
    private double originMinutes;
    private double totalMinutes;

    /// <summary>The narrowest the grid can be drawn without clipping a column, for a given number of days.</summary>
    public static double MinWidthFor(int days) => GutterWidth + ScrollBarWidth + 12 + days * MinColumnWidth;

    public event Action<PlacedSession, FrameworkElement>? SessionClicked;
    public event Action<PlacedSession, FrameworkElement, DayOfWeek>? SessionContext;
    public event Action<DateTime, TimeSpan, FrameworkElement>? SlotContext;
    public event Action<DateTime, FrameworkElement>? DayContext;
    public event Action<SessionMove>? SessionMoved;

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
        CancelDrag();
        Content = null;
        scroller = null;
        body = null;
        dragLayer = null;
    }

    public void Render(ScheduleData data, IReadOnlyList<DateTime> dates, DateTime now, Guid? selected, bool keepScroll, bool animate = false)
    {
        CancelDrag();
        this.dates = dates;
        var keptOffset = keepScroll ? scroller?.VerticalOffset : null;
        HourHeight = data.Settings.HourHeightMode switch { "Compact" => 58, "Spacious" => 94, _ => 76 };
        columns.Clear();
        cards.Clear();
        hint = null;
        var (startHour, endHour) = ScheduleQueries.HourRange(data);
        originMinutes = startHour * 60;
        var rows = endHour - startHour;
        var total = rows * HourHeight;
        totalMinutes = rows * 60;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var header = NewColumns(dates.Count);
        header.Margin = new Thickness(0, 12, ScrollBarWidth, 6);
        for (var i = 0; i < dates.Count; i++)
        {
            var cell = DayHeader(dates[i], dates[i].Date == now.Date, !TermCalendar.ClassesRun(data.Settings, dates[i]));
            Grid.SetColumn(cell, i + 1);
            header.Children.Add(cell);
        }
        root.Children.Add(header);

        var body = NewColumns(dates.Count);
        body.Height = total;
        this.body = body;

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

        var layer = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        Grid.SetColumn(layer, 1);
        Grid.SetColumnSpan(layer, dates.Count);
        Panel.SetZIndex(layer, 20);
        body.Children.Add(layer);
        dragLayer = layer;

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
        for (var i = 0; i < days; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = MinColumnWidth });
        return grid;
    }

    private UIElement DayHeader(DateTime date, bool today, bool off)
    {
        var name = Ui.Text(date.ToString("ddd").ToUpperInvariant(), 11, today ? "Brush.Primary" : "Brush.Ink3", FontWeights.Medium);
        name.HorizontalAlignment = HorizontalAlignment.Center;

        var number = Ui.Text(date.Day.ToString(), 24, today ? "Brush.OnPrimary" : off ? "Brush.Ink4" : "Brush.Ink", FontWeights.Normal, "Font.Display");
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
        if (!TermCalendar.ClassesRun(data.Settings, date)) layers.Children.Add(OffDay(TermCalendar.Of(data.Settings, date).Label));
        var panel = new EventColumnPanel { HourHeight = HourHeight, OriginMinutes = startHour * 60, TotalMinutes = rows * 60 };
        foreach (var placed in ScheduleQueries.SessionsOnDate(data, date))
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

    /// <summary>The shading over a day with no classes (a break, or outside the term), with its name at the top.</summary>
    private static UIElement OffDay(string label)
    {
        var shade = new Rectangle { IsHitTestVisible = false };
        shade.SetResourceReference(Shape.FillProperty, "Brush.S2");
        shade.Opacity = 0.6;

        var lines = new Rectangle { IsHitTestVisible = false, Opacity = 0.55 };
        if (Application.Current.TryFindResource("Brush.Line") is Brush line)
        {
            var stripe = new GeometryDrawing(null, new Pen(line, 1), new LineGeometry(new Point(-2, 12), new Point(12, -2)));
            lines.Fill = new DrawingBrush(stripe)
            {
                TileMode = TileMode.Tile, Viewbox = new Rect(0, 0, 10, 10), ViewboxUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 10, 10), ViewportUnits = BrushMappingMode.Absolute
            };
        }

        var text = Ui.Text(label, 12, "Brush.Ink2", FontWeights.Medium);
        text.TextAlignment = TextAlignment.Center;
        var pill = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 3, 10, 4), Margin = new Thickness(6, 10, 6, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center, IsHitTestVisible = false, Child = text };
        pill.SetResourceReference(Border.BackgroundProperty, "Brush.S1");
        pill.SetResourceReference(Border.BorderBrushProperty, "Brush.LineStrong");
        pill.BorderThickness = new Thickness(1);

        return new Grid { IsHitTestVisible = false, Children = { shade, lines, pill } };
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
        card.MouseLeftButtonDown += (_, e) => BeginPress(card, placed, day, e);
        card.MouseMove += (_, e) => OnCardMove(card, e);
        card.MouseLeftButtonUp += (_, e) => OnCardRelease(card, placed, e);
        card.LostMouseCapture += (_, _) => { if (drag?.Card == card) CancelDrag(); };
        card.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && drag is { Active: true }) { CancelDrag(); e.Handled = true; }
            else if (e.Key is Key.Enter or Key.Space) { SessionClicked?.Invoke(placed, card); e.Handled = true; }
        };
        return card;
    }

    // ---- dragging a card to move it, or its top or bottom edge to resize it

    private static bool NearEdge(FrameworkElement card, MouseEventArgs e, out DragMode mode)
    {
        var y = e.GetPosition(card).Y;
        var zone = Math.Min(EdgeZone, card.ActualHeight / 3.5);
        mode = y <= zone ? DragMode.ResizeTop : y >= card.ActualHeight - zone ? DragMode.ResizeBottom : DragMode.Move;
        return mode != DragMode.Move;
    }

    private double ColumnWidth => body is null || dates.Count == 0 ? 1 : Math.Max(1, (body.ActualWidth - GutterWidth) / dates.Count);

    private double MinutesAt(double y) => originMinutes + y * 60 / HourHeight;

    private static double Snap(double minutes) => Math.Round(minutes / SnapMinutes) * SnapMinutes;

    private void BeginPress(FrameworkElement card, PlacedSession placed, DayOfWeek day, MouseButtonEventArgs e)
    {
        var column = dates.ToList().FindIndex(d => d.DayOfWeek == day);
        if (body is null || column < 0 || e.ClickCount > 1) return;

        NearEdge(card, e, out var mode);
        var pointer = e.GetPosition(body);
        drag = new Drag
        {
            Placed = placed, Card = card, Day = day, Column = column, Mode = mode, Press = pointer,
            GrabMinutes = MinutesAt(pointer.Y) - placed.Session.Start.TotalMinutes
        };
        card.Focus();
        card.CaptureMouse();
    }

    private void OnCardMove(FrameworkElement card, MouseEventArgs e)
    {
        if (drag is null || drag.Card != card)
        {
            card.Cursor = NearEdge(card, e, out _) ? Cursors.SizeNS : Cursors.Hand;
            return;
        }

        if (!drag.Active)
        {
            var at = e.GetPosition(body);
            if (Math.Abs(at.X - drag.Press.X) < DragThreshold && Math.Abs(at.Y - drag.Press.Y) < DragThreshold) return;
            StartDrag(drag);
        }
        UpdateDrag(drag);
    }

    private void OnCardRelease(FrameworkElement card, PlacedSession placed, MouseButtonEventArgs e)
    {
        var finished = drag is { } d && d.Card == card ? d : null;
        drag = null;
        if (finished is not null) card.ReleaseMouseCapture();
        e.Handled = true;

        if (finished is not { Active: true })
        {
            SessionClicked?.Invoke(placed, card);
            return;
        }

        EndDrag(finished);
        var session = finished.Placed.Session;
        var to = dates[finished.Column].DayOfWeek;
        var start = TimeSpan.FromMinutes(finished.Start);
        var end = TimeSpan.FromMinutes(finished.End);
        if (to == finished.Day && start == session.Start && end == session.End) return;
        SessionMoved?.Invoke(new SessionMove(finished.Placed, finished.Day, to, start, end, card));
    }

    private void StartDrag(Drag d)
    {
        d.Active = true;
        d.Card.Opacity = 0.35;
        d.Card.Cursor = d.Mode == DragMode.Move ? Cursors.SizeAll : Cursors.SizeNS;

        var key = d.Placed.Course.Color;
        var title = Ui.Text(d.Placed.Course.Code, 12, "Brush.OnEvent", FontWeights.Medium);
        d.Detail = Ui.Text("", 11, "Brush.OnEvent");
        var stack = new StackPanel { Margin = new Thickness(8, 0, 6, 0) };
        stack.Children.Add(title);
        stack.Children.Add(d.Detail);

        d.Ghost = new Border
        {
            CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(2), Padding = new Thickness(0, 4, 0, 3), ClipToBounds = true,
            Child = stack, Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 5, Direction = 270, Opacity = 0.3, Color = Colors.Black }
        };
        d.Ghost.SetResourceReference(Border.BackgroundProperty, $"Course.{key}.Bg");
        d.Ghost.SetResourceReference(Border.BorderBrushProperty, "Brush.Ink");
        dragLayer?.Children.Add(d.Ghost);
        autoscroll.Start();
    }

    /// <summary>Works out where the card would land from the pointer, snapped to five minutes and kept inside the grid, and moves the ghost there.</summary>
    private void UpdateDrag(Drag d)
    {
        if (body is null || d.Ghost is null) return;
        var session = d.Placed.Session;
        var pointer = Mouse.GetPosition(body);
        var floor = originMinutes;
        var ceiling = originMinutes + totalMinutes;

        switch (d.Mode)
        {
            case DragMode.Move:
                var length = (session.End - session.Start).TotalMinutes;
                d.Start = Math.Clamp(Snap(MinutesAt(pointer.Y) - d.GrabMinutes), floor, ceiling - length);
                d.End = d.Start + length;
                d.Column = Math.Clamp((int)Math.Floor((pointer.X - GutterWidth) / ColumnWidth), 0, dates.Count - 1);
                break;
            case DragMode.ResizeBottom:
                d.Start = session.Start.TotalMinutes;
                d.End = Math.Clamp(Snap(MinutesAt(pointer.Y)), d.Start + SessionEdits.MinMinutes, ceiling);
                break;
            default:
                d.End = session.End.TotalMinutes;
                d.Start = Math.Clamp(Snap(MinutesAt(pointer.Y)), floor, d.End - SessionEdits.MinMinutes);
                break;
        }

        var width = ColumnWidth;
        d.Ghost.Width = Math.Max(24, width - 10);
        d.Ghost.Height = Math.Max(20, (d.End - d.Start) * HourHeight / 60 - 2);
        Canvas.SetLeft(d.Ghost, d.Column * width + 2);
        Canvas.SetTop(d.Ghost, (d.Start - originMinutes) * HourHeight / 60 + 1);

        var range = TimeText.Range(TimeSpan.FromMinutes(d.Start), TimeSpan.FromMinutes(d.End));
        var moved = dates[d.Column].DayOfWeek != d.Day;
        if (d.Detail is not null) d.Detail.Text = moved ? $"{dates[d.Column]:ddd} {range}" : range;
    }

    private void AutoScroll()
    {
        if (drag is not { Active: true } d || scroller is null) return;
        var y = Mouse.GetPosition(scroller).Y;
        const double edge = 36;
        var step = y < edge ? -(edge - y) / 2.5 - 1 : y > scroller.ViewportHeight - edge ? (y - (scroller.ViewportHeight - edge)) / 2.5 + 1 : 0;
        if (step == 0) return;
        scroller.ScrollToVerticalOffset(scroller.VerticalOffset + Math.Clamp(step, -24, 24));
        UpdateDrag(d);
    }

    private void EndDrag(Drag d)
    {
        autoscroll.Stop();
        if (d.Ghost?.Parent is Panel parent) parent.Children.Remove(d.Ghost);
        d.Card.Opacity = 1;
        d.Card.Cursor = Cursors.Hand;
    }

    private void CancelDrag()
    {
        if (drag is not { } d) return;
        drag = null;
        if (d.Active) EndDrag(d);
        d.Card.ReleaseMouseCapture();
    }
}
