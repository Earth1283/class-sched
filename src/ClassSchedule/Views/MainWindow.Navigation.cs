using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public partial class MainWindow
{
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

    private const double SidebarWidth = 256;
    private const double RailWidth = 300;
    private const double PageMargin = 16;
    private const double ListMinWidth = 520;

    /// <summary>
    /// Gives the side panels the room left over once the week grid has what it needs, so a narrow window drops the right panel first,
    /// then the sidebar, instead of clipping the day columns. The settings say what is wanted; this only decides what fits.
    /// </summary>
    private void FitPanels()
    {
        var room = Root.ActualWidth > 0 ? Root.ActualWidth - PageMargin - gridMinWidth : double.PositiveInfinity;
        var rail = wantRail && room >= (wantSidebar ? SidebarWidth : 0) + RailWidth;
        var sidebar = wantSidebar && room >= (rail ? RailWidth : 0) + SidebarWidth;

        SidebarScroll.Visibility = sidebar ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = new GridLength(sidebar ? SidebarWidth : 0);
        RailScroll.Visibility = rail ? Visibility.Visible : Visibility.Collapsed;
        RailColumn.Width = new GridLength(rail ? RailWidth : 0);
    }

    /// <summary>The note beside the date: why the shown days are empty (“Fall break”), or else the week count.</summary>
    private string PeriodNote(IReadOnlyList<DateTime> dates)
    {
        var off = TermCalendar.OffLabel(data.Settings, dates);
        if (off.Length > 0) return off;
        return data.Settings.ShowWeekNumber ? WeekCountText() : "";
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
        Tabs.Visibility = shown;
        MenuButton.Visibility = shown;
        var prefs = data.Settings;
        wantSidebar = empty || prefs.SidebarVisible;
        wantRail = !empty && prefs.RailVisible;
        if (empty)
        {
            gridMinWidth = 0;
            FitPanels();
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
        gridMinWidth = mode == ViewMode.List ? ListMinWidth : ScheduleGridView.MinWidthFor(dates.Count);
        FitPanels();
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
        WeekCount.Text = PeriodNote(dates);

        WeekButton.IsChecked = mode == ViewMode.Week;
        DayButton.IsChecked = mode == ViewMode.Day;
        ListButton.IsChecked = mode == ViewMode.List;
        SegMove.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation((int)mode * 68, TimeSpan.FromMilliseconds(450)) { EasingFunction = Ui.Smooth });

        SidePanel.Children.Clear();
        SidePanel.Children.Add(SidePanels.NowCard(data, now, animate: !keepScroll));
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
        if (s.MiniMonthVisible) SidebarPanel.Children.Add(MiniMonth.Build(miniMonth, DateTime.Today, from, count, data.Settings, GoTo, StepMiniMonth));
        if (!s.CoursesVisible) return;
        var courses = SidePanels.CoursesList(data, ToggleCourse, ShowCourseMenu, poppedCourse);
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

    private void OnToggleSidebar(object sender, RoutedEventArgs e)
    {
        data.Settings.SidebarVisible = !data.Settings.SidebarVisible;
        Commit();
    }
}
