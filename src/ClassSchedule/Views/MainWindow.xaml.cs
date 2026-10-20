using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClassSchedule.Core;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;

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
        Refresh(keepScroll: false);
        OnClockTick();
        ApplyWidget();
    }

    private bool WidgetRunning => widget is { IsVisible: true };

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

    private void StartOnPreferredView()
    {
        var s = data.Settings;
        mode = s.DefaultView switch { "Day" => ViewMode.Day, "List" => ViewMode.List, _ => ViewMode.Week };
        if (!s.RememberPosition || s.LastDate is not { } last) return;

        var visible = ScheduleQueries.VisibleDays(data);
        var day = last.Date;
        for (var i = 0; i < 7 && !visible.Contains(day.DayOfWeek); i++) day = day.AddDays(1);
        dayDate = day;
        weekOffset = (int)((ScheduleQueries.WeekStart(day) - ScheduleQueries.WeekStart(DateTime.Today)).TotalDays / 7);
        miniMonth = new DateTime(day.Year, day.Month, 1);
    }

    /// <summary>Starts with only the desktop widget showing.</summary>
    public void StartHidden() => ReleaseUi();

    public void BackUp(bool announce)
    {
        try
        {
            var copy = ScheduleBackups.CreateAndPrune(store.Path, data.Settings.BackupCount, DateTime.Now);
            if (announce) ShowToast(copy is null ? "Nothing to back up yet" : "Backed up", copy ?? "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (announce) ShowToast("The backup could not be made", ex.Message);
        }
    }

    private void ResetWidgetPosition()
    {
        data.Settings.WidgetLeft = null;
        data.Settings.WidgetTop = null;
        widget?.PlaceFromSettings(data.Settings);
        Commit();
    }

    private void ShowFileInExplorer()
    {
        try
        {
            if (File.Exists(store.Path)) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{store.Path}\"");
            else if (Path.GetDirectoryName(store.Path) is { } dir && Directory.Exists(dir)) System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowToast("Explorer could not be opened", ex.Message);
        }
    }

    private bool Confirm(string text)
    {
        if (!data.Settings.ConfirmDeletes) return true;
        return MessageBox.Show(this, text, "Class Schedule", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private void ApplyWidget()
    {
        var s = data.Settings;
        if (!s.WidgetEnabled)
        {
            widget?.Hide();
            return;
        }

        if (widget is null)
        {
            widget = new TodayWidget(data);
            widget.OpenRequested += ShowMain;
            widget.QuitRequested += Quit;
            widget.HideRequested += () =>
            {
                data.Settings.WidgetEnabled = false;
                Commit();
                if (!IsVisible) ShowMain();
            };
            widget.Moved += () =>
            {
                data.Settings.WidgetLeft = widget.Left;
                data.Settings.WidgetTop = widget.Top;
                try { store.Save(data); }
                catch (Exception) { }
            };
        }

        widget.Apply(s);
        if (!widget.IsVisible)
        {
            widget.PlaceFromSettings(s);
            widget.Show();
        }
    }

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

    private DateTime WeekStart => ScheduleQueries.WeekStart(DateTime.Today).AddDays(7 * weekOffset);

    private IReadOnlyList<DateTime> WeekDates() =>
        ScheduleQueries.VisibleDays(data).Select(d => WeekStart.AddDays(DayParser.Order(d))).ToList();

    private void Refresh(bool keepScroll = true)
    {
        if (uiReleased) return;
        var now = DateTime.Now;
        Title = $"Class Schedule — {data.Settings.TermName}";
        TermLabel.Text = "/ " + data.Settings.TermName;

        var empty = data.Sessions.Count == 0;
        var shown = empty ? Visibility.Collapsed : Visibility.Visible;
        NavGroup.Visibility = shown;
        RailScroll.Visibility = shown;
        Tabs.Visibility = shown;
        MenuButton.Visibility = shown;
        var prefs = data.Settings;
        var sidebar = empty || prefs.SidebarVisible;
        SidebarScroll.Visibility = sidebar ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(sidebar ? 256 : 0);
        var rail = !empty && prefs.RailVisible;
        RailScroll.Visibility = rail ? Visibility.Visible : Visibility.Collapsed;
        RailColumn.Width = new GridLength(rail ? 300 : 0);
        if (empty)
        {
            if (emptyView is null || !ReferenceEquals(MainHost.Child, emptyView))
            {
                emptyView = EmptyState.Build(OpenBulkAdd, ImportFile, AddClass, LoadSample);
                MainHost.Child = emptyView;
            }
            SidePanel.Children.Clear();
            SidebarPanel.Children.Clear();
            return;
        }

        var dates = mode == ViewMode.Day ? new List<DateTime> { dayDate } : WeekDates();
        var animate = !keepScroll || animateNext;
        animateNext = false;
        if (mode == ViewMode.List)
        {
            MainHost.Child = ListViewBuilder.Build(data, dates, now, ShowDetails, ShowEventMenu);
        }
        else
        {
            grid.Render(data, dates, now, null, keepScroll && MainHost.Child == grid, animate);
            MainHost.Child = grid;
        }
        if (!keepScroll && MainHost.Child is { } view)
        {
            Motion.SlideX(view, slideDir * 28, 320);
            slideDir = 0;
        }

        DateLabel.Text = mode == ViewMode.Day
            ? dayDate.ToString("MMMM d, yyyy", CultureInfo.CurrentCulture)
            : WeekLabel(dates);
        WeekCount.Text = prefs.ShowWeekNumber ? WeekCountText() : "";

        WeekButton.IsChecked = mode == ViewMode.Week;
        DayButton.IsChecked = mode == ViewMode.Day;
        ListButton.IsChecked = mode == ViewMode.List;
        SegMove.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation((int)mode * 68, TimeSpan.FromMilliseconds(450)) { EasingFunction = Ui.Smooth });

        SidePanel.Children.Clear();
        SidePanel.Children.Add(SidePanels.NowCard(data, now));
        SidePanel.Children.Add(SidePanels.TodayCard(data, now));
        SidePanel.Children.Add(SidePanels.ComingUpCard(data, now, AddTask, EditTask, ToggleDone));

        BuildSidebar(dates);
    }

    private void BuildSidebar(IReadOnlyList<DateTime> dates)
    {
        var from = mode == ViewMode.Day ? dayDate : dates[0];
        var count = mode == ViewMode.Day ? 1 : dates.Count;

        SidebarPanel.Children.Clear();
        var s = data.Settings;
        if (s.MiniMonthVisible) SidebarPanel.Children.Add(MiniMonth.Build(miniMonth, DateTime.Today, from, count, GoTo, StepMiniMonth));
        if (!s.CoursesVisible) return;
        var courses = SidePanels.CoursesList(data, ToggleCourse, poppedCourse);
        poppedCourse = null;
        if (courses is FrameworkElement list) list.Margin = new Thickness(0, s.MiniMonthVisible ? 24 : 0, 0, 0);
        SidebarPanel.Children.Add(courses);
    }

    private void StepMiniMonth(int direction)
    {
        miniMonth = miniMonth.AddMonths(direction);
        Refresh();
    }

    private void SyncMiniMonth()
    {
        var anchor = mode == ViewMode.Day ? dayDate : WeekStart;
        miniMonth = new DateTime(anchor.Year, anchor.Month, 1);
    }

    private void GoTo(DateTime date)
    {
        var visible = ScheduleQueries.VisibleDays(data);
        var day = date.Date;
        for (var i = 0; i < 7 && !visible.Contains(day.DayOfWeek); i++) day = day.AddDays(1);
        slideDir = Math.Sign(day.CompareTo(mode == ViewMode.Day ? dayDate : WeekStart));
        dayDate = day;
        weekOffset = (int)((ScheduleQueries.WeekStart(date) - ScheduleQueries.WeekStart(DateTime.Today)).TotalDays / 7);
        miniMonth = new DateTime(date.Year, date.Month, 1);
        Refresh(keepScroll: false);
    }

    private void GoToDay(DateTime date)
    {
        mode = ViewMode.Day;
        GoTo(date);
    }

    private string WeekCountText()
    {
        var first = ScheduleQueries.WeekStart(data.Settings.TermStart);
        var index = (int)((WeekStart - first).TotalDays / 7) + 1;
        var total = (int)((ScheduleQueries.WeekStart(data.Settings.TermEnd) - first).TotalDays / 7) + 1;
        return index >= 1 && index <= total ? $"Week {index} of {total}" : "";
    }

    private static string WeekLabel(IReadOnlyList<DateTime> dates)
    {
        var first = dates[0];
        var last = dates[^1];
        if (first.Month == last.Month) return first.ToString("MMMM yyyy");
        return first.Year == last.Year
            ? $"{first:MMM} – {last:MMM yyyy}"
            : $"{first:MMM yyyy} – {last:MMM yyyy}";
    }

    private void OnClockTick()
    {
        foreach (var reminder in ReminderPlanner.Due(data, DateTime.Now, remindersSent)) Notify(reminder.Title, reminder.Body);
        if (IsVisible && !DetailPopup.IsOpen) Refresh(keepScroll: true);
    }

    private void Notify(string title, string body)
    {
        if (data.Settings.ReminderSound)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); }
            catch (Exception) { }
        }
        if (IsActive || (IsVisible && !data.Settings.ReminderViaWindows)) { ShowToast(title, body); return; }
        try
        {
            new ToastContentBuilder().AddText(title).AddText(body).Show();
        }
        catch (Exception)
        {
            ShowToast(title, body);
        }
    }

    public void ShowToast(string title, string body, string? actionLabel = null, Action? action = null)
    {
        ToastTitle.Text = title;
        ToastBody.Text = body;
        ToastBody.Visibility = body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        toastAction = action;
        ToastAction.Content = actionLabel ?? "";
        ToastAction.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        toastTimer.Interval = TimeSpan.FromSeconds(action is null ? 5 : Math.Clamp(data.Settings.UndoSeconds, 3, 30));

        Motion.Reset(ToastHost);
        ToastHost.Visibility = Visibility.Visible;
        toastTimer.Stop();
        toastTimer.Start();
        Motion.Rise(ToastHost, 16, 350, 0, Motion.Spring, 0.96);
    }

    private void OnToastAction(object sender, RoutedEventArgs e)
    {
        var run = toastAction;
        toastAction = null;
        toastTimer.Stop();
        ToastHost.Visibility = Visibility.Collapsed;
        Motion.Reset(ToastHost);
        run?.Invoke();
    }

    private void Commit()
    {
        try { store.Save(data); }
        catch (Exception ex) { ShowToast("The schedule could not be saved", ex.Message); }
        ApplyPreferences();
        Refresh();
        ApplyWidget();
    }

    private void OnViewClick(object sender, RoutedEventArgs e)
    {
        mode = sender == DayButton ? ViewMode.Day : sender == ListButton ? ViewMode.List : ViewMode.Week;
        if (mode == ViewMode.Day && (dayDate.Date < WeekStart || dayDate.Date > WeekStart.AddDays(6))) dayDate = weekOffset == 0 ? DateTime.Today : WeekStart;
        SyncMiniMonth();
        Refresh(keepScroll: false);
    }

    private void OnPrev(object sender, RoutedEventArgs e) => Step(-1);

    private void OnNext(object sender, RoutedEventArgs e) => Step(1);

    private void Step(int direction)
    {
        if (mode == ViewMode.Day)
        {
            var visible = ScheduleQueries.VisibleDays(data);
            do dayDate = dayDate.AddDays(direction);
            while (!visible.Contains(dayDate.DayOfWeek));
            weekOffset = (int)((ScheduleQueries.WeekStart(dayDate) - ScheduleQueries.WeekStart(DateTime.Today)).TotalDays / 7);
        }
        else weekOffset += direction;
        slideDir = direction;
        SyncMiniMonth();
        Refresh(keepScroll: false);
    }

    private void OnToday(object sender, RoutedEventArgs e)
    {
        slideDir = mode == ViewMode.Day ? Math.Sign(DateTime.Today.CompareTo(dayDate.Date)) : -Math.Sign(weekOffset);
        weekOffset = 0;
        dayDate = DateTime.Today;
        SyncMiniMonth();
        Refresh(keepScroll: false);
    }

    private void OnShortcut(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.N: AddClass(); break;
            case Key.B: OpenBulkAdd(); break;
            case Key.T: AddTask(); break;
            case Key.Q: Quit(); break;
            case Key.D1: SetMode(ViewMode.Week); break;
            case Key.D2: SetMode(ViewMode.Day); break;
            case Key.D3: SetMode(ViewMode.List); break;
            case Key.Left: Step(-1); break;
            case Key.Right: Step(1); break;
            default: return;
        }
        e.Handled = true;
    }

    private void SetMode(ViewMode next)
    {
        mode = next;
        SyncMiniMonth();
        Refresh(keepScroll: false);
    }

    private void OnAddClass(object sender, RoutedEventArgs e) => AddClass();

    private void AddClass() => AddClass(null);

    private void AddClass(ClassPrefill? prefill)
    {
        bool again;
        do
        {
            var dialog = new ClassDialog(this, data, null, prefill);
            var saved = dialog.ShowDialog() == true;
            if (saved) Commit();
            again = saved && dialog.AddAnother;
            prefill = null;
        }
        while (again);
    }

    private void OnToggleSidebar(object sender, RoutedEventArgs e)
    {
        data.Settings.SidebarVisible = !data.Settings.SidebarVisible;
        Commit();
    }

    private void OnCreate(object sender, RoutedEventArgs e) => CreatePopup.IsOpen = !CreatePopup.IsOpen;

    private void OnCreateClass(object sender, RoutedEventArgs e)
    {
        CreatePopup.IsOpen = false;
        AddClass();
    }

    private void OnBulkAdd(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        CreatePopup.IsOpen = false;
        OpenBulkAdd();
    }

    private void OpenBulkAdd()
    {
        var dialog = new BulkAddDialog(this, data);
        if (dialog.ShowDialog() != true) return;

        var result = BulkAdd.Apply(dialog.Rows, data);
        if (result.AddedAnything) Commit();
        ShowToast(result.Summary(), "");
    }

    private void LoadSample()
    {
        var sample = SampleData.Create(DateTime.Today);
        sample.Settings.KeepPreferencesFrom(data.Settings);
        data.Settings = sample.Settings;
        data.Courses.AddRange(sample.Courses);
        data.Sessions.AddRange(sample.Sessions);
        data.Tasks.AddRange(sample.Tasks);
        Commit();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        if (data.Sessions.Count + data.Tasks.Count == 0) return;

        var answer = MessageBox.Show(this,
            $"Remove all {data.Sessions.Count} classes and {data.Tasks.Count} assignments and exams?\nThis cannot be undone.",
            "Clear schedule", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        data.Clear();
        Commit();
    }

    private void AddTask() => AddTask(null);

    private void AddTask(DateTime? due)
    {
        if (new TaskDialog(this, data, null, due).ShowDialog() == true) Commit();
    }

    private void EditTask(TaskItem task)
    {
        if (new TaskDialog(this, data, task).ShowDialog() == true) Commit();
    }

    private void ToggleDone(TaskItem task)
    {
        task.Done = !task.Done;
        Commit();
    }

    private void ToggleCourse(Course course)
    {
        course.Visible = !course.Visible;
        poppedCourse = course.Id;
        animateNext = true;
        Commit();
    }

    private void OnAddTask(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        CreatePopup.IsOpen = false;
        AddTask();
    }

    private void OnMore(object sender, RoutedEventArgs e) => MorePopup.IsOpen = !MorePopup.IsOpen;

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        var host = new SettingsHost(Commit, ResetWidgetPosition, () => BackUp(true), ShowFileInExplorer, () => store.Path,
            () => OnOpenFile(this, new RoutedEventArgs()), () => OnSaveAs(this, new RoutedEventArgs()), () => data);
        new SettingsWindow(this, host).ShowDialog();
        Commit();
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        ImportFile();
    }

    private void ImportFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import schedule",
            Filter = "Schedule files (*.csv;*.ics)|*.csv;*.ics|CSV (*.csv)|*.csv|Calendar (*.ics)|*.ics"
        };
        if (dialog.ShowDialog(this) != true) return;

        ImportResult result;
        try
        {
            var text = File.ReadAllText(dialog.FileName);
            result = Path.GetExtension(dialog.FileName).Equals(".ics", StringComparison.OrdinalIgnoreCase)
                ? IcsService.Import(text, data)
                : CsvImporter.Import(text, data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("The file could not be read", ex.Message);
            return;
        }

        if (result.AddedAnything) Commit();
        ShowToast(result.Summary(), result.Errors.Count > 0 ? $"{result.Errors.Count} problem{(result.Errors.Count == 1 ? "" : "s")} found." : "");

        if (result.Errors.Count > 0)
        {
            var lines = string.Join(Environment.NewLine, result.Errors.Take(8));
            var more = result.Errors.Count > 8 ? $"{Environment.NewLine}…and {result.Errors.Count - 8} more." : "";
            MessageBox.Show(this, lines + more, "Import problems", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string? StartFolder() =>
        Path.GetDirectoryName(store.Path) is { } dir && Directory.Exists(dir) ? dir : null;

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        var dialog = new OpenFileDialog
        {
            Title = "Open schedule file",
            Filter = "Schedule files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = StartFolder(),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) SwitchTo(dialog.FileName, openExisting: true);
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        var dialog = new SaveFileDialog
        {
            Title = "Save schedule as",
            Filter = "Schedule files (*.json)|*.json",
            FileName = Path.GetFileName(store.Path),
            InitialDirectory = StartFolder(),
            AddExtension = true,
            DefaultExt = "json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true) SwitchTo(dialog.FileName, openExisting: false);
    }

    private void SwitchTo(string path, bool openExisting)
    {
        var next = new ScheduleStore(path);
        try
        {
            if (openExisting)
            {
                var other = ScheduleStore.TryRead(path, out var problem);
                if (other is null)
                {
                    ShowToast("That file could not be opened", problem ?? "");
                    return;
                }
                other.Settings.KeepPreferencesFrom(data.Settings);
                data.ReplaceContentsWith(other);
            }
            else next.Save(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast(openExisting ? "That file could not be opened" : "The schedule could not be saved there", ex.Message);
            return;
        }

        store = next;
        string note;
        try
        {
            var config = AppConfig.Load();
            config.Use(path);
            config.Save();
            note = "New changes are saved here from now on.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            note = "This only lasts until you close the app, because the choice could not be remembered.";
        }

        weekOffset = 0;
        dayDate = DateTime.Today;
        SyncMiniMonth();
        ApplyPreferences();
        ApplyWidget();
        Refresh(keepScroll: false);
        ShowToast(openExisting ? "Opened " + Path.GetFileName(path) : "Saved to " + path, note);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        var dialog = new SaveFileDialog
        {
            Title = "Export to calendar",
            Filter = "Calendar (*.ics)|*.ics",
            FileName = $"{data.Settings.TermName}.ics"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, IcsService.Export(data, DateTime.UtcNow), new UTF8Encoding(false));
            ShowToast("Calendar exported", dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("The calendar could not be saved", ex.Message);
        }
    }

    private void ShowDetails(PlacedSession placed, FrameworkElement target)
    {
        var key = placed.Course.Color;
        var session = placed.Session;

        var stack = new StackPanel { Width = 292 };
        var kind = new StackPanel { Orientation = Orientation.Horizontal };
        var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, $"Course.{key}.Dot");
        kind.Children.Add(dot);
        kind.Children.Add(Ui.Caption(session.Kind));
        stack.Children.Add(kind);

        var heading = placed.Course.Name.Length > 0 ? $"{placed.Course.Code} · {placed.Course.Name}" : placed.Course.Code;
        var title = Ui.Heading(heading, 20);
        title.TextWrapping = TextWrapping.Wrap;
        title.Margin = new Thickness(0, 8, 0, 14);
        stack.Children.Add(title);

        void Row(string icon, string text)
        {
            if (text.Length == 0) return;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            row.Children.Add(new Icon(icon, 16, "Brush.Ink3"));
            var label = Ui.Text(text, 13, "Brush.Ink2");
            label.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(label);
            stack.Children.Add(row);
        }
        Row("Icon.Clock", $"{DayParser.Abbreviations(session.Days)} · {TimeText.Range(session.Start, session.End)}");
        Row("Icon.Pin", session.Location);
        Row("Icon.User", placed.Course.Instructor);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var edit = Ui.Button("Edit", null, "BtnSecondary", 30);
        edit.Margin = new Thickness(0, 0, 6, 0);
        edit.Click += (_, _) =>
        {
            DetailPopup.IsOpen = false;
            EditSession(session);
        };
        var delete = Ui.Button("Delete", null, "BtnQuiet", 30);
        delete.Click += (_, _) =>
        {
            DetailPopup.IsOpen = false;
            DeleteSession(placed, target);
        };
        actions.Children.Add(edit);
        actions.Children.Add(delete);
        stack.Children.Add(actions);

        var back = Ui.Popover(stack, new Thickness(16));
        back.Margin = new Thickness(8);

        DetailPopup.Child = back;
        DetailPopup.PlacementTarget = target;
        DetailPopup.IsOpen = true;

        MarkOpen(target, key);
    }

    private void MarkOpen(FrameworkElement target, string key)
    {
        ClearHighlight();
        if (target is Border card && card.Tag is "event")
        {
            highlighted = card;
            highlightKey = key;
            card.Tag = "open";
            card.SetResourceReference(Border.BorderBrushProperty, "Brush.Ink");
        }
    }

    private void EditSession(ClassSession session)
    {
        if (new ClassDialog(this, data, session).ShowDialog() == true) Commit();
    }

    private void ShowEventMenu(PlacedSession placed, FrameworkElement target, DayOfWeek day)
    {
        if (!data.Settings.ContextMenus) return;
        DetailPopup.IsOpen = false;
        var session = placed.Session;
        var course = placed.Course;

        var menu = new MenuPopup($"{course.Code} · {session.Kind} · {TimeText.Range(session.Start, session.End)}");
        menu.Add("Icon.Edit", "Edit…", "", () => EditSession(session));
        menu.Add("Icon.Duplicate", "Duplicate to another day…", "", () => AddClass(new ClassPrefill(CopyOf: session)));
        menu.Add("Icon.Copy", "Copy details", "", () => CopyDetails(placed));
        menu.Separator();
        menu.Add("Icon.EyeOff", $"Hide {course.Code} from the schedule", "", () => HideCourse(course, target));
        if (session.Days.Count > 1)
        {
            menu.Add("Icon.Trash", $"Delete {day} only", "", () => DeleteDay(placed, target, day), danger: true);
            menu.Add("Icon.Trash", $"Delete all {session.Days.Count} days ({DayParser.Abbreviations(session.Days)})", "", () => DeleteSession(placed, target), danger: true);
        }
        else menu.Add("Icon.Trash", "Delete", "", () => DeleteSession(placed, target), danger: true);

        MarkOpen(target, course.Color);
        menu.Closed += ClearHighlight;
        menu.Open(target);
    }

    private void ShowSlotMenu(DateTime date, TimeSpan time, FrameworkElement anchor)
    {
        if (!data.Settings.ContextMenus) return;
        DetailPopup.IsOpen = false;
        grid.ShowSlotHint(date, time);

        var menu = new MenuPopup($"{date:dddd} · {TimeText.Format(time)}");
        menu.Add("Icon.Plus", "New class here…", "Ctrl+N", () => AddClass(new ClassPrefill(date.DayOfWeek, time)));
        menu.Add("Icon.Check", "New assignment or exam…", "Ctrl+T", () => AddTask(date.Date + time));
        menu.Add("Icon.Rows", "Paste classes…", "Ctrl+B", OpenBulkAdd);
        menu.Separator();
        menu.Add("Icon.Calendar", "Go to this day", "", () => GoToDay(date));
        menu.Closed += grid.ClearSlotHint;
        menu.Open(anchor);
    }

    private void ShowDayMenu(DateTime date, FrameworkElement anchor)
    {
        if (!data.Settings.ContextMenus) return;
        DetailPopup.IsOpen = false;
        var menu = new MenuPopup($"{date:dddd, MMM d}");
        menu.Add("Icon.Calendar", "Open day view", "", () => GoToDay(date));
        menu.Add("Icon.Plus", "New class on this day…", "", () => AddClass(new ClassPrefill(date.DayOfWeek)));
        menu.Open(anchor);
    }

    private void DeleteSession(PlacedSession placed, FrameworkElement target)
    {
        if (!Confirm($"Delete {placed.Course.Code} {placed.Session.Kind} ({DayParser.Abbreviations(placed.Session.Days)})?")) return;
        FadeDeleteSession(placed, target);
    }

    private void FadeDeleteSession(PlacedSession placed, FrameworkElement target) =>
        Motion.FadeOut(target, 200, () =>
        {
            var undo = data.RemoveSessionWithUndo(placed.Session.Id);
            if (undo is null) return;
            Commit();
            ShowToast($"Deleted {placed.Course.Code} {placed.Session.Kind}", "", "Undo", () =>
            {
                undo();
                animateNext = true;
                Commit();
            });
        });

    private void DeleteDay(PlacedSession placed, FrameworkElement target, DayOfWeek day)
    {
        if (!Confirm($"Delete {placed.Course.Code} {placed.Session.Kind} on {day}?")) return;
        FadeDeleteDay(placed, target, day);
    }

    private void FadeDeleteDay(PlacedSession placed, FrameworkElement target, DayOfWeek day) =>
        Motion.FadeOut(target, 200, () =>
        {
            var undo = data.RemoveDayWithUndo(placed.Session.Id, day);
            if (undo is null) return;
            Commit();
            ShowToast($"Deleted {placed.Course.Code} {placed.Session.Kind} on {day}", "", "Undo", () =>
            {
                undo();
                animateNext = true;
                Commit();
            });
        });

    private void HideCourse(Course course, FrameworkElement target) =>
        Motion.FadeOut(target, 200, () =>
        {
            course.Visible = false;
            Commit();
            ShowToast($"Hid {course.Code}", "", "Undo", () =>
            {
                course.Visible = true;
                animateNext = true;
                Commit();
            });
        });

    private void CopyDetails(PlacedSession placed)
    {
        var s = placed.Session;
        var c = placed.Course;
        var lines = new[]
        {
            c.Name.Length > 0 ? $"{c.Code} · {c.Name}" : c.Code,
            $"{s.Kind}, {DayParser.Abbreviations(s.Days)} {TimeText.Range(s.Start, s.End)}",
            s.Location,
            c.Instructor
        }.Where(line => line.Length > 0);

        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            ShowToast("Copied class details", "");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ShowToast("The clipboard is busy", "Try again in a moment.");
        }
    }

    private void OnDetailClosed(object? sender, EventArgs e) => ClearHighlight();

    private void ClearHighlight()
    {
        if (highlighted is null) return;
        highlighted.Tag = "event";
        highlighted.SetResourceReference(Border.BorderBrushProperty, $"Course.{highlightKey}.Line");
        highlighted = null;
    }
}
