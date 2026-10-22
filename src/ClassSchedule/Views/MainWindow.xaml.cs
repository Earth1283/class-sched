using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ClassSchedule.Core;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ClassSchedule.Views;

public partial class MainWindow : Window
{
    private enum ViewMode { Week, Day, List }

    private ScheduleStore store;
    private readonly ScheduleData data;
    private readonly ScheduleGridView grid = new();
    private readonly HashSet<string> remindersSent = new();
    private readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer toastTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    private ViewMode mode = ViewMode.Week;
    private int weekOffset;
    private DateTime dayDate = DateTime.Today;
    private Border? highlighted;
    private string highlightKey = "";
    private UIElement? emptyView;
    private DateTime miniMonth = new(DateTime.Today.Year, DateTime.Today.Month, 1);
    private TodayWidget? widget;
    private bool quitting;
    private bool hintShown;
    private bool uiReleased;
    private int slideDir;
    private bool animateNext;
    private Guid? poppedCourse;
    private Action? toastAction;
    private string appliedLook = "";
    private bool wantSidebar, wantRail;
    private double gridMinWidth;

    public MainWindow(ScheduleStore store, ScheduleData data)
    {
        this.store = store;
        this.data = data;
        InitializeComponent();
        ApplyPreferences();
        ApplyBounds();
        StartOnPreferredView();

        SourceInitialized += (_, _) => ThemeManager.StyleTitleBar(this);
        Application.Current.SessionEnding += (_, _) => quitting = true;
        grid.SessionClicked += ShowDetails;
        grid.SessionContext += ShowEventMenu;
        grid.SlotContext += ShowSlotMenu;
        grid.DayContext += ShowDayMenu;
        grid.SessionMoved += OnSessionMoved;

        clock.Tick += (_, _) => OnClockTick();
        clock.Start();
        toastTimer.Tick += (_, _) =>
        {
            toastTimer.Stop();
            Motion.FadeOut(ToastHost, 200, () =>
            {
                ToastHost.Visibility = Visibility.Collapsed;
                Motion.Reset(ToastHost);
            });
        };

        PreviewKeyDown += OnShortcut;
        Root.SizeChanged += (_, _) => FitPanels();
        Refresh(keepScroll: false);
        OnClockTick();
        ApplyWidget();
    }

    /// <summary>Pushes every preference that is not read directly while drawing: theme, clock, week start, scale, density, startup.</summary>
    private void ApplyPreferences()
    {
        var s = data.Settings;
        SettingsRuntime.Apply(s);
        var look = $"{s.Theme}|{s.Accent}|{s.Palette}";
        if (look != appliedLook)
        {
            appliedLook = look;
            ThemeManager.Apply(s);
        }

        Motion.Enabled = s.AnimationsEnabled;
        Ui.Compact = s.Density == "Compact";
        Ui.Scale = s.InterfaceSize switch { "Small" => 0.92, "Large" => 1.12, _ => 1.0 };
        Root.LayoutTransform = Ui.Scale == 1 ? Transform.Identity : new ScaleTransform(Ui.Scale, Ui.Scale);
        MinWidth = 980 * Ui.Scale;
        MinHeight = 620 * Ui.Scale;
        StartupRegistration.Apply(s.LaunchAtLogin);
    }

    /// <summary>Starts with only the desktop widget showing.</summary>
    public void StartHidden() => ReleaseUi();

    public void ShowMain()
    {
        var wasHidden = !IsVisible;
        if (wasHidden) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        if (wasHidden)
        {
            uiReleased = false;
            Refresh(keepScroll: false);
        }
        Activate();
    }

    private void Quit()
    {
        quitting = true;
        Close();
    }

    private void OnQuit(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        Quit();
    }

    private void ApplyBounds()
    {
        var saved = data.Settings;
        var area = SystemParameters.WorkArea;
        var hasSaved = saved.WindowWidth >= MinWidth && saved.WindowHeight >= MinHeight;

        var width = hasSaved ? saved.WindowWidth : Math.Min(1320, area.Width * 0.92);
        var height = hasSaved ? saved.WindowHeight : Math.Min(820, area.Height * 0.92);
        Width = Math.Clamp(width, Math.Min(MinWidth, area.Width), area.Width);
        Height = Math.Clamp(height, Math.Min(MinHeight, area.Height), area.Height);

        if (hasSaved && IsOnScreen(saved.WindowLeft, saved.WindowTop))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = saved.WindowLeft;
            Top = saved.WindowTop;
        }
        if (hasSaved && saved.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private bool IsOnScreen(double left, double top) =>
        left + 120 < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth &&
        left + Width > SystemParameters.VirtualScreenLeft + 120 &&
        top >= SystemParameters.VirtualScreenTop &&
        top + 80 < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        SaveWindowBounds();

        var keepRunning = data.Settings.CloseBehavior switch
        {
            "Quit" => false,
            "Background" => true,
            _ => WidgetRunning
        };
        if (quitting || !keepRunning) return;

        e.Cancel = true;
        Hide();
        ReleaseUi();
        if (hintShown) return;
        hintShown = true;
        try
        {
            new ToastContentBuilder().AddText("Class Schedule is still running")
                .AddText(WidgetRunning
                    ? "It stays as the desktop widget. Right-click the widget to open or quit it."
                    : "Reminders keep working. Start Class Schedule again to bring this window back.").Show();
        }
        catch (Exception)
        {
        }
    }

    private void ReleaseUi()
    {
        uiReleased = true;
        MainHost.Child = null;
        grid.Release();
        SidePanel.Children.Clear();
        SidebarPanel.Children.Clear();
        emptyView = null;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, MemoryTrim.Run);
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized) Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, MemoryTrim.Run);
    }

    private void SaveWindowBounds()
    {
        var bounds = RestoreBounds;
        if (bounds.IsEmpty) return;

        var saved = data.Settings;
        saved.WindowLeft = bounds.Left;
        saved.WindowTop = bounds.Top;
        saved.WindowWidth = bounds.Width;
        saved.WindowHeight = bounds.Height;
        saved.WindowMaximized = WindowState == WindowState.Maximized;
        if (saved.RememberPosition) saved.LastDate = mode == ViewMode.Day ? dayDate : WeekStart;
        try { store.Save(data); }
        catch (Exception) { }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        widget?.Close();
        Application.Current.Shutdown();
    }

    private void Commit()
    {
        try { store.Save(data); }
        catch (Exception ex) { ShowToast("The schedule could not be saved", ex.Message); }
        ApplyPreferences();
        Refresh();
        ApplyWidget();
    }
}
