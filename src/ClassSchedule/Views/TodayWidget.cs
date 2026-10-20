using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

/// <summary>A borderless desktop card with today's classes that can stay beneath every other window.</summary>
public sealed class TodayWidget : Window
{
    private const int WmWindowPosChanging = 0x0046;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndBottom = new(1);

    private readonly ScheduleData data;
    private readonly Border background = new();
    private readonly Border body = new();
    private readonly Popup menu = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.MousePoint, PopupAnimation = PopupAnimation.Fade };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool behind = true;
    private bool compact;

    public event Action? OpenRequested;
    public event Action? HideRequested;
    public event Action? QuitRequested;
    public event Action? Moved;

    public TodayWidget(ScheduleData data)
    {
        this.data = data;

        Title = "Today's schedule";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        FontSize = 13;
        Cursor = Cursors.SizeAll;
        SetResourceReference(ForegroundProperty, "Brush.Ink");
        SetResourceReference(FontFamilyProperty, "Font.Body");

        background.CornerRadius = new CornerRadius(20);
        background.SetResourceReference(Border.BackgroundProperty, "Brush.S1");
        background.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.28, Color = Colors.Black };

        body.Padding = new Thickness(20, 16, 20, 16);

        var card = new Grid { Margin = new Thickness(14) };
        card.Children.Add(background);
        card.Children.Add(body);
        Content = card;

        menu.Child = BuildMenu();
        menu.PlacementTarget = card;

        MouseLeftButtonDown += OnPress;
        MouseRightButtonUp += (_, e) => { menu.IsOpen = true; e.Handled = true; };
        SourceInitialized += (_, _) => Hook();

        timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) { Refresh(); timer.Start(); } else timer.Stop(); };
    }

    private UIElement BuildMenu()
    {
        var stack = new StackPanel();
        Button Item(string text, string icon, Action act)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new Icon(icon, 18, "Brush.Ink2"));
            row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            var b = new Button { Content = row };
            b.SetResourceReference(StyleProperty, "MenuItem");
            b.Click += (_, _) => { menu.IsOpen = false; act(); };
            return b;
        }
        stack.Children.Add(Item("Open Class Schedule", "Icon.Calendar", () => OpenRequested?.Invoke()));
        stack.Children.Add(Item("Hide this widget", "Icon.Dash", () => HideRequested?.Invoke()));
        stack.Children.Add(Ui.Rule(6, 6));
        stack.Children.Add(Item("Quit Class Schedule", "Icon.Close", () => QuitRequested?.Invoke()));

        var pop = Ui.Popover(stack, new Thickness(8));
        pop.Width = 236;
        pop.Margin = new Thickness(8);
        return pop;
    }

    public void Apply(Settings s)
    {
        behind = s.WidgetBehind;
        Width = (s.WidgetCompact ? 262 : 296) + 28;
        background.Opacity = Math.Clamp(s.WidgetOpacity, 55, 100) / 100.0;
        compact = s.WidgetCompact;
        if (IsLoaded || new WindowInteropHelper(this).Handle != IntPtr.Zero) ApplyWindowStyles();
        Refresh();
    }

    public void PlaceFromSettings(Settings s)
    {
        var area = SystemParameters.WorkArea;
        if (s.WidgetLeft is { } left && s.WidgetTop is { } top && IsOnScreen(left, top))
        {
            Left = left;
            Top = top;
            return;
        }
        Left = area.Right - Width - 12;
        Top = area.Top + 12;
    }

    private bool IsOnScreen(double left, double top) =>
        left + 80 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
        left + Width > SystemParameters.VirtualScreenLeft + 80 &&
        top >= SystemParameters.VirtualScreenTop &&
        top + 60 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

    private void OnPress(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            OpenRequested?.Invoke();
            return;
        }
        try
        {
            DragMove();
            Moved?.Invoke();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void Hook()
    {
        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        ApplyWindowStyles();
    }

    private void ApplyWindowStyles()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;

        var style = GetWindowLong(handle, GwlExStyle) | WsExToolWindow;
        style = behind ? style | WsExNoActivate : style & ~WsExNoActivate;
        SetWindowLong(handle, GwlExStyle, style);
        if (behind) SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmWindowPosChanging && behind)
        {
            var pos = Marshal.PtrToStructure<WindowPos>(lParam);
            pos.InsertAfter = HwndBottom;
            pos.Flags &= ~SwpNoZOrder;
            Marshal.StructureToPtr(pos, lParam, false);
        }
        return IntPtr.Zero;
    }

    public void Refresh()
    {
        var now = DateTime.Now;
        var stack = new StackPanel();

        var prefs = data.Settings;
        var agenda = ScheduleQueries.Agenda(data, now).Where(i => prefs.WidgetShowDone || i.Status != AgendaStatus.Done).ToList();
        var todays = data.Tasks.Where(t => !t.Done && t.Due.Date == now.Date).OrderBy(t => t.Due).ToList();

        var head = new Grid();
        var titles = new StackPanel();
        titles.Children.Add(Ui.Caption("Today"));
        titles.Children.Add(Ui.Heading(now.ToString("ddd, MMM d"), compact ? 18 : 22));
        head.Children.Add(titles);
        var count = Ui.Text(agenda.Count == 0 ? "" : $"{agenda.Count} {(agenda.Count == 1 ? "class" : "classes")}", 12, "Brush.Ink3");
        count.HorizontalAlignment = HorizontalAlignment.Right;
        count.VerticalAlignment = VerticalAlignment.Top;
        head.Children.Add(count);
        stack.Children.Add(head);

        var list = new StackPanel { Margin = new Thickness(-8, 12, -8, 0) };
        foreach (var item in agenda) list.Children.Add(ClassRow(item, now, compact));
        foreach (var task in todays) list.Children.Add(TaskRow(task));
        if (prefs.WidgetShowTomorrow && agenda.All(i => i.Status == AgendaStatus.Done))
        {
            var tomorrow = now.Date.AddDays(1);
            var coming = ScheduleQueries.SessionsOn(data, tomorrow.DayOfWeek, onlyVisible: false);
            if (coming.Count > 0)
            {
                var label = Ui.Caption($"Tomorrow, {tomorrow:ddd MMM d}");
                label.Margin = new Thickness(10, list.Children.Count > 0 ? 10 : 0, 0, 2);
                list.Children.Add(label);
                foreach (var p in coming) list.Children.Add(ClassRow(new AgendaItem(p, AgendaStatus.Upcoming), now, compact));
            }
        }
        if (list.Children.Count == 0)
        {
            var info = ScheduleQueries.Now(data, now);
            var text = info.Next is { } next && info.NextStart is { } at
                ? $"Nothing today. Next: {next.Course.Code} {next.Session.Kind}, {at:ddd} {TimeText.Format(next.Session.Start)}."
                : "Nothing scheduled today.";
            var empty = Ui.Text(text, 13, "Brush.Ink3");
            empty.TextWrapping = TextWrapping.Wrap;
            empty.TextTrimming = TextTrimming.None;
            empty.Margin = new Thickness(8, 4, 8, 4);
            list.Children.Add(empty);
        }
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

    private static UIElement ClassRow(AgendaItem item, DateTime now, bool compact)
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
            var where = string.Join(" · ", new[] { p.Session.Location, isNow ? p.Course.Instructor : "" }.Where(s => s.Length > 0));
            if (where.Length > 0) text.Children.Add(Ui.Text(where, 12.5, "Brush.Ink3"));

            if (isNow)
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
        if (isNow) row.SetResourceReference(Border.BackgroundProperty, "Brush.S2");
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

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr InsertAfter;
        public int X, Y, Cx, Cy;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
