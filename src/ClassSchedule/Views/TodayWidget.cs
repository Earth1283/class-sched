using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

/// <summary>A borderless desktop window with today's classes that can stay beneath every other window.</summary>
public sealed class TodayWidget : Window
{
    private const int WmWindowPosChanging = 0x0046;
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010;
    private static readonly IntPtr HwndBottom = new(1);

    private readonly ScheduleData data;
    private readonly WidgetCard card = new();
    private readonly Popup menu = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.MousePoint, PopupAnimation = PopupAnimation.Fade };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(30) };
    private bool behind = true;
    private bool locked;
    private bool refreshing;

    public event Action? OpenRequested;
    public event Action? HideRequested;
    public event Action? QuitRequested;
    public event Action? Moved;

    /// <summary>Raised after the widget's own menu changed a preference, so it can be saved.</summary>
    public event Action? PreferencesEdited;

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

        Content = card;
        menu.PlacementTarget = card;

        MouseLeftButtonDown += OnPress;
        MouseRightButtonUp += (_, e) =>
        {
            menu.Child = BuildMenu();
            menu.IsOpen = true;
            e.Handled = true;
        };
        SourceInitialized += (_, _) => Hook();

        timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => { if (IsVisible) Refresh(); };
        Closed += (_, _) => timer.Stop();
    }

    /// <summary>True while the widget is hidden because nothing is left today; it comes back by itself.</summary>
    public bool AutoHidden { get; private set; }

    /// <summary>Whether the widget is doing its job: on screen, or waiting out the rest of the day.</summary>
    public bool IsRunning => IsVisible || AutoHidden;

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

        var s = data.Settings;
        stack.Children.Add(Item("Open Class Schedule", "Icon.Calendar", () => OpenRequested?.Invoke()));
        stack.Children.Add(Item(s.WidgetLocked ? "Unlock position" : "Lock position", "Icon.Pin", () =>
        {
            s.WidgetLocked = !s.WidgetLocked;
            PreferencesEdited?.Invoke();
        }));
        stack.Children.Add(Item(s.WidgetCompact ? "Comfortable layout" : "Compact layout", "Icon.Rows", () =>
        {
            s.WidgetCompact = !s.WidgetCompact;
            PreferencesEdited?.Invoke();
        }));

        var corners = new UniformGrid { Columns = 2 };
        foreach (var (label, right, bottom) in new[] { ("Top left", false, false), ("Top right", true, false), ("Bottom left", false, true), ("Bottom right", true, true) })
        {
            var corner = new Button { Content = label, FontSize = 12.5 };
            corner.SetResourceReference(StyleProperty, "MenuItem");
            corner.Click += (_, _) => { menu.IsOpen = false; MoveToCorner(right, bottom); };
            corners.Children.Add(corner);
        }
        stack.Children.Add(Ui.Rule(6, 2));
        var caption = Ui.Caption("Move to a corner");
        caption.Margin = new Thickness(12, 4, 12, 2);
        stack.Children.Add(caption);
        stack.Children.Add(corners);
        stack.Children.Add(Ui.Rule(6, 6));
        stack.Children.Add(Item("Hide this widget", "Icon.Dash", () => HideRequested?.Invoke()));
        stack.Children.Add(Item("Quit Class Schedule", "Icon.Close", () => QuitRequested?.Invoke()));

        var pop = Ui.Popover(stack, new Thickness(8));
        pop.Width = 236;
        pop.Margin = new Thickness(8);
        return pop;
    }

    public void Apply(Settings s)
    {
        behind = s.WidgetBehind;
        locked = s.WidgetLocked;
        Cursor = locked ? Cursors.Arrow : Cursors.SizeAll;
        Width = WidgetCard.WidthFor(s);
        card.Apply(s);
        if (IsLoaded || new WindowInteropHelper(this).Handle != IntPtr.Zero) ApplyWindowStyles();
        if (!timer.IsEnabled) timer.Start();
        Refresh();
    }

    /// <summary>Stops updating and hides the widget, for when it is switched off.</summary>
    public void Stop()
    {
        timer.Stop();
        AutoHidden = false;
        Hide();
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

    public void MoveToCorner(bool right, bool bottom)
    {
        var area = SystemParameters.WorkArea;
        UpdateLayout();
        const double inset = 12;
        var gutter = WidgetCard.GutterFor(data.Settings);
        Left = right ? area.Right - ActualWidth + gutter - inset : area.Left + inset - gutter;
        Top = bottom ? area.Bottom - ActualHeight + gutter - inset : area.Top + inset - gutter;
        Moved?.Invoke();
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
        if (locked) return;
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
        if (refreshing) return;
        refreshing = true;
        try
        {
            card.Refresh(data, DateTime.Now);

            var done = data.Settings.WidgetHideWhenDone && !card.HasContent;
            if (done == AutoHidden) return;
            AutoHidden = done;
            if (done) Hide();
            else Show();
        }
        finally
        {
            refreshing = false;
        }
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
