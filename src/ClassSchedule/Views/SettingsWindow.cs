using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public sealed record SettingsHost(
    Action Changed, Action ResetWidgetPosition, Action BackUpNow, Action ShowFile, Func<string> FilePath,
    Action OpenFile, Action SaveAs, Func<ScheduleData> Data);

/// <summary>Every preference in one place: sections, search, a live preview, and changes that apply as you make them.</summary>
public sealed class SettingsWindow : Window
{
    private sealed class Row
    {
        public required string Label { get; init; }
        public string Hint { get; init; } = "";
        public required Func<UIElement> Build { get; init; }
        public Action? Reset { get; init; }
        public Func<bool>? Enabled { get; init; }
        public bool Stacked { get; init; }
        public bool Note { get; init; }
    }

    private sealed record Group(string Name, List<Row> Rows);

    private sealed record Section(string Name, string Icon, string Sub, List<Group> Groups);

    private static readonly Settings Defaults = new();

    private readonly SettingsHost host;
    private readonly List<Section> sections = new();
    private readonly List<(Row Row, FrameworkElement Element)> built = new();
    private readonly List<(Section Section, RadioButton Button, TextBlock Count)> nav = new();
    private readonly StackPanel mainPanel = new();
    private readonly ScrollViewer mainScroll = new();
    private readonly TextBox search = new();
    private readonly TextBlock searchHint = new();
    private readonly ScheduleGridView previewGrid = new() { IsHitTestVisible = false };
    private readonly Border previewFrame = new() { Width = 540, Height = 560 };
    private readonly ScheduleData sampleData = SampleData.Create(DateTime.Today);
    private readonly Button resetAll;
    private readonly TextBlock footNote;
    private readonly DispatcherTimer disarm = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly DispatcherTimer redraw = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private int current;
    private string query = "";
    private bool armed;
    private bool navLock;

    public SettingsWindow(Window owner, SettingsHost host)
    {
        this.host = host;
        var area = SystemParameters.WorkArea;
        Owner = owner;
        Title = "Settings";
        Width = Math.Min(1280, area.Width * 0.96);
        Height = Math.Min(790, area.Height * 0.94);
        MinWidth = Math.Min(1040, area.Width);
        MinHeight = Math.Min(600, area.Height);
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "Brush.S1");
        SetResourceReference(ForegroundProperty, "Brush.Ink");
        SetResourceReference(FontFamilyProperty, "Font.Body");
        SourceInitialized += (_, _) => ThemeManager.StyleTitleBar(this);

        BuildSections();

        resetAll = Ui.Button("Reset everything…", null, "BtnSecondary", 38);
        footNote = Ui.Text("", 12, "Brush.Ink3");
        footNote.TextWrapping = TextWrapping.Wrap;
        footNote.TextTrimming = TextTrimming.None;
        footNote.Margin = new Thickness(6, 0, 6, 10);
        footNote.Visibility = Visibility.Collapsed;

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(252) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(470) });

        var navColumn = BuildNav();
        var mainColumn = BuildMain();
        var previewColumn = BuildPreview();
        Grid.SetColumn(mainColumn, 1);
        Grid.SetColumn(previewColumn, 2);
        root.Children.Add(navColumn);
        root.Children.Add(mainColumn);
        root.Children.Add(previewColumn);
        Content = root;

        resetAll.Click += (_, _) => OnResetAll();
        disarm.Tick += (_, _) => Disarm();
        redraw.Tick += (_, _) =>
        {
            redraw.Stop();
            Render(keepScroll: true);
            RefreshPreview();
        };
        ThemeManager.Changed += OnLookChanged;
        Closed += (_, _) =>
        {
            ThemeManager.Changed -= OnLookChanged;
            disarm.Stop();
            redraw.Stop();
        };

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && search.Text.Length > 0) { search.Clear(); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { search.Focus(); e.Handled = true; }
        };

        Render();
        RefreshPreview();
    }

    private Settings Settings => host.Data().Settings;

    private void OnLookChanged()
    {
        redraw.Stop();
        redraw.Start();
    }

    private void Changed()
    {
        host.Changed();
        RefreshPreview();
        UpdateEnabled();
    }

    private void RefreshPreview()
    {
        var previewData = host.Data().Sessions.Count > 0 ? host.Data() : sampleData;
        previewData.Settings = Settings;
        var now = DateTime.Now;
        var monday = ScheduleQueries.WeekStart(now);
        var dates = ScheduleQueries.VisibleDays(previewData).Select(d => monday.AddDays(DayParser.Order(d))).ToList();
        previewFrame.Width = Math.Max(540, 80 + 90 * dates.Count);
        previewGrid.Render(previewData, dates, now, null, keepScroll: true);
    }

    private static PropertyInfo Prop(string name) =>
        typeof(Settings).GetProperty(name) ?? throw new ArgumentException("No such setting: " + name);

    private T Get<T>(string name) => (T)Prop(name).GetValue(Settings)!;

    private void Set<T>(string name, T value)
    {
        Prop(name).SetValue(Settings, value);
        Changed();
    }

    private Action ResetOf(params string[] names) => () =>
    {
        foreach (var name in names)
        {
            var value = Prop(name).GetValue(Defaults);
            if (value is List<DayOfWeek> days) value = new List<DayOfWeek>(days);
            Prop(name).SetValue(Settings, value);
        }
    };

    // ---- row kinds

    private Row Toggle(string label, string hint, string prop, Func<bool>? enabled = null) => new()
    {
        Label = label, Hint = hint, Enabled = enabled, Reset = ResetOf(prop),
        Build = () =>
        {
            var toggle = new System.Windows.Controls.Primitives.ToggleButton { IsChecked = Get<bool>(prop) };
            toggle.SetResourceReference(StyleProperty, "Switch");
            AutomationProperties.SetName(toggle, label);
            toggle.Click += (_, _) => Set(prop, toggle.IsChecked == true);
            return toggle;
        }
    };

    private Row Choice<T>(string label, string hint, string prop, params (T Value, string Text)[] options) =>
        Choice(label, hint, () => Get<T>(prop), v => Set(prop, v), options, ResetOf(prop));

    private Row Choice<T>(string label, string hint, Func<T> get, Action<T> set, (T Value, string Text)[] options, Action reset, Func<bool>? enabled = null) => new()
    {
        Label = label, Hint = hint, Reset = reset, Enabled = enabled,
        Stacked = options.Sum(o => o.Text.Length + 4) > 34,
        Build = () =>
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var group = Guid.NewGuid().ToString();
            var chosen = get();
            foreach (var (value, text) in options)
            {
                var button = new RadioButton { GroupName = group, Content = text, IsChecked = EqualityComparer<T>.Default.Equals(chosen, value) };
                button.SetResourceReference(StyleProperty, "SegItem");
                button.Checked += (_, _) => set(value);
                panel.Children.Add(button);
            }
            var pill = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(1), Child = panel };
            pill.SetResourceReference(Border.BorderBrushProperty, "Brush.LineStrong");
            return pill;
        }
    };

    private Row Pick<T>(string label, string hint, string prop, (T Value, string Text)[] options, Func<bool>? enabled = null) => new()
    {
        Label = label, Hint = hint, Reset = ResetOf(prop), Enabled = enabled,
        Build = () => PickBuilder(prop, options, label)
    };

    private Row Count(string label, string hint, string prop, int[] values, string unit, Func<bool>? enabled = null) => new()
    {
        Label = label, Hint = hint, Reset = ResetOf(prop), Enabled = enabled,
        Build = () =>
        {
            var value = new TextBlock { MinWidth = 78, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Medium };
            void Show() => value.Text = Get<int>(prop) + unit;
            Show();

            Button Step(string icon, string tip, Func<int, int> next)
            {
                var button = Ui.IconButton(icon, tip);
                button.Width = button.Height = 32;
                button.Click += (_, _) =>
                {
                    Set(prop, next(Get<int>(prop)));
                    Show();
                };
                return button;
            }

            var lower = Step("Icon.Dash", "Less", now => values.Where(v => v < now).DefaultIfEmpty(now).Max());
            var higher = Step("Icon.Plus", "More", now => values.Where(v => v > now).DefaultIfEmpty(now).Min());
            var grid = new Grid { Margin = new Thickness(1) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(value, 1);
            Grid.SetColumn(higher, 2);
            grid.Children.Add(lower);
            grid.Children.Add(value);
            grid.Children.Add(higher);

            var pill = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Child = grid, HorizontalAlignment = HorizontalAlignment.Right };
            pill.SetResourceReference(Border.BorderBrushProperty, "Brush.LineStrong");
            return pill;
        }
    };

    private Row ButtonRow(string label, string hint, string text, Action run, Func<bool>? enabled = null) => new()
    {
        Label = label, Hint = hint, Enabled = enabled,
        Build = () =>
        {
            var button = Ui.Button(text, null, "BtnSecondary", 34);
            button.Click += (_, _) => run();
            return button;
        }
    };

    private Row TextRow(string label, string hint, Func<string> get, Func<string, string?> commit) => new()
    {
        Label = label, Hint = hint,
        Build = () =>
        {
            var box = new TextBox { Text = get(), Width = 220, HorizontalAlignment = HorizontalAlignment.Right };
            box.SetResourceReference(StyleProperty, "Field");
            var error = Ui.Text("", 12, "Brush.Warn");
            error.TextWrapping = TextWrapping.Wrap;
            error.TextTrimming = TextTrimming.None;
            error.HorizontalAlignment = HorizontalAlignment.Right;
            error.MaxWidth = 220;
            error.Visibility = Visibility.Collapsed;

            void Commit()
            {
                var problem = commit(box.Text);
                error.Text = problem ?? "";
                error.Visibility = problem is null ? Visibility.Collapsed : Visibility.Visible;
                if (problem is null) box.Text = get();
            }
            box.LostKeyboardFocus += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Enter) return;
                Commit();
                e.Handled = true;
            };
            var stack = new StackPanel();
            stack.Children.Add(box);
            stack.Children.Add(error);
            return stack;
        }
    };

    // ---- the settings themselves

    private void BuildSections()
    {
        var weekdays = new (DayOfWeek, string)[] { (DayOfWeek.Monday, "Monday"), (DayOfWeek.Sunday, "Sunday"), (DayOfWeek.Saturday, "Saturday") };
        var views = new[] { ("Week", "Week"), ("Day", "Day"), ("List", "List") };

        sections.Add(new Section("General", "Icon.Sliders", "How the app opens, closes and behaves.", new()
        {
            new Group("Startup", new()
            {
                Choice("Open in this view", "", "DefaultView", views),
                Choice("Open on", "Which week to show when the app starts.", "RememberPosition", (false, "This week"), (true, "Where I left off")),
                Toggle("Start with Windows", "Open Class Schedule when you sign in to Windows.", "LaunchAtLogin"),
                Toggle("Start with only the widget", "When the desktop widget is on, open without the main window.", "StartHidden", () => Settings.WidgetEnabled)
            }),
            new Group("Closing", new()
            {
                Pick("When I close the window", "Reminders and the widget only work while the app keeps running.", "CloseBehavior", new[]
                {
                    ("WidgetOnly", "Keep running if the widget is on"), ("Quit", "Always quit"), ("Background", "Always keep running in the background")
                })
            }),
            new Group("Editing", new()
            {
                Toggle("Ask before deleting a class", "Deleting can always be undone from the message at the bottom.", "ConfirmDeletes"),
                Count("Undo stays for", "How long the Undo message stays on screen.", "UndoSeconds", Enumerable.Range(3, 18).ToArray(), " s"),
                Toggle("Right-click menus", "Menus on classes, empty time slots and day headers.", "ContextMenus")
            })
        }));

        sections.Add(new Section("Calendar", "Icon.Calendar", "What the week, day and list views show.", new()
        {
            new Group("Time", new()
            {
                Choice("Clock", "", "Use24Hour", (false, "12-hour"), (true, "24-hour")),
                Choice("Week starts on", "", "WeekStartsOn", weekdays),
                Toggle("Show “Week 9 of 15”", "The week count beside the date, based on your term dates.", "ShowWeekNumber")
            }),
            new Group("Days and hours", new()
            {
                Choice("Weekends", "Auto shows Saturday and Sunday only when a class meets then.", "Weekends", ("Auto", "Auto"), ("Always", "Always"), ("Never", "Never")),
                HoursRow(),
                Choice("Hour height", "Taller hours leave room for more text on short classes.", "HourHeightMode", ("Compact", "Compact"), ("Comfortable", "Comfortable"), ("Spacious", "Spacious"))
            }),
            new Group("Class cards", new()
            {
                Choice("What a card shows", "A line only appears when it fits.", "CardDetail", ("Code", "Code only"), ("Room", "Code + room"), ("Fit", "Everything that fits")),
                Toggle("Current-time line", "The red line across today.", "ShowNowLine"),
                Toggle("Fade classes that already happened", "", "DimPast")
            })
        }));

        var starts = Enumerable.Range(12, 31).Select(i => TimeSpan.FromMinutes(i * 30)).ToArray();
        var kinds = new[] { "Lecture", "Lab", "Seminar", "Discussion", "Studio", "Recitation", "Tutorial", "Workshop" };
        sections.Add(new Section("New classes", "Icon.Rows", "What a class starts as before you type anything.", new()
        {
            new Group("Starting values", new()
            {
                Count("Length", "", "NewClassMinutes", Enumerable.Range(4, 33).Select(i => i * 5).ToArray(), " min"),
                Pick("Starts at", "", "NewClassStart", starts.Select(t => (t, TimeText.Format(t))).ToArray()),
                DaysRow(),
                Pick("Kind", "", "NewClassKind", kinds.Concat(new[] { Settings.NewClassKind }).Distinct().Select(k => (k, k)).ToArray()),
                Count("Credits", "", "NewClassCredits", Enumerable.Range(0, 13).ToArray(), "")
            }),
            new Group("Colors", new() { PaletteRow() })
        }));

        sections.Add(new Section("Reminders", "Icon.Bell", "Nudges before classes start or work is due.", new()
        {
            new Group("Classes", new()
            {
                Toggle("Remind me before class", "", "RemindersEnabled"),
                Count("How early", "", "ReminderMinutes", new[] { 1, 2, 3, 5, 10, 15, 20, 30, 45, 60, 90, 120, 180, 240 }, " min", () => Settings.RemindersEnabled)
            }),
            new Group("Assignments and exams", new()
            {
                Toggle("Remind me about due dates", "", "TaskReminders"),
                Pick("Remind me", "", "TaskReminderWhen", new[]
                {
                    ("HourBefore", "1 hour before"), ("DayBefore", "1 day before"), ("Morning", "The morning of (8 AM)"), ("DayAndHour", "1 day and 1 hour before")
                }, () => Settings.TaskReminders)
            }),
            new Group("Delivery", new()
            {
                Choice("Show reminders as", "In the app only applies while the window is open.", "ReminderViaWindows", (true, "Windows notification"), (false, "In the app")),
                Toggle("Play a sound", "", "ReminderSound"),
                NoteRow("Reminders appear only while Class Schedule or its widget is running.")
            })
        }));

        sections.Add(new Section("Appearance", "Icon.Palette", "Colors, size and motion.", new()
        {
            new Group("Theme", new()
            {
                Choice("Theme", "", "Theme", ("System", "System"), ("Light", "Light"), ("Dark", "Dark")),
                AccentRow()
            }),
            new Group("Size and density", new()
            {
                Choice("Interface size", "Scales the whole app.", "InterfaceSize", ("Small", "Small"), ("Normal", "Normal"), ("Large", "Large")),
                Choice("Density", "Tighter lists and side panels.", "Density", ("Comfortable", "Comfortable"), ("Compact", "Compact"))
            }),
            new Group("Layout", new()
            {
                Toggle("Left sidebar", "", "SidebarVisible"),
                Toggle("Mini month", "", "MiniMonthVisible", () => Settings.SidebarVisible),
                Toggle("My courses list", "", "CoursesVisible", () => Settings.SidebarVisible),
                Toggle("Right panel", "“In class now”, Today and Coming up.", "RailVisible")
            }),
            new Group("Motion", new()
            {
                Toggle("Animations", "Small movements when things open, close or change.", "AnimationsEnabled")
            })
        }));

        var widgetOn = (Func<bool>)(() => Settings.WidgetEnabled);
        sections.Add(new Section("Desktop widget", "Icon.Grid", "Today's schedule, kept behind your windows.", new()
        {
            new Group("Widget", new()
            {
                Toggle("Show on the desktop", "", "WidgetEnabled"),
                Toggle("Keep behind other windows", "", "WidgetBehind", widgetOn),
                Choice("Size", "", "WidgetCompact", (false, "Comfortable"), (true, "Compact")),
                Choice("Opacity", "", () => new[] { 100, 90, 75, 60 }.OrderBy(o => Math.Abs(o - Settings.WidgetOpacity)).First(),
                    v => Set("WidgetOpacity", v), new[] { (100, "100%"), (90, "90%"), (75, "75%"), (60, "60%") }, ResetOf("WidgetOpacity"), widgetOn)
            }),
            new Group("Content", new()
            {
                Toggle("Show classes that already happened", "", "WidgetShowDone", widgetOn),
                Toggle("Show “Next due”", "The next assignment or exam, along the bottom.", "WidgetShowDue", widgetOn),
                Toggle("Show tomorrow when today is done", "", "WidgetShowTomorrow", widgetOn),
                ButtonRow("Position", "Put the widget back in the top-right corner.", "Reset position", host.ResetWidgetPosition, widgetOn)
            })
        }));

        sections.Add(new Section("Data", "Icon.Database", "Where your schedule lives, backups and the term.", new()
        {
            new Group("Schedule file", new() { FileRow() }),
            new Group("Backups", new()
            {
                Toggle("Keep automatic backups", "A dated copy beside the schedule file each time the app starts.", "BackupsEnabled"),
                Count("How many to keep", "", "BackupCount", Enumerable.Range(1, 30).ToArray(), "", () => Settings.BackupsEnabled),
                ButtonRow("Back up now", "", "Back up now", host.BackUpNow)
            }),
            new Group("Term", new()
            {
                TextRow("Term name", "", () => Settings.TermName, CommitTermName),
                TextRow("Starts", "Used for the week count and for how far exported events repeat.", () => Settings.TermStart.ToString("d", CultureInfo.CurrentCulture), text => CommitTermDate(text, start: true)),
                TextRow("Ends", "", () => Settings.TermEnd.ToString("d", CultureInfo.CurrentCulture), text => CommitTermDate(text, start: false))
            })
        }));
    }

    private UIElement PickBuilder<T>(string prop, (T Value, string Text)[] options, string label)
    {
        var chosen = Get<T>(prop);
        var shown = options.Where(o => EqualityComparer<T>.Default.Equals(o.Value, chosen)).Select(o => o.Text).FirstOrDefault() ?? chosen?.ToString() ?? "";
        var button = new Button { Content = shown };
        button.SetResourceReference(StyleProperty, "SelectBox");
        AutomationProperties.SetName(button, label);
        button.Click += (_, _) =>
        {
            var menu = new MenuPopup();
            var now = Get<T>(prop);
            foreach (var (value, text) in options)
            {
                var v = value;
                var t = text;
                menu.AddOption(t, EqualityComparer<T>.Default.Equals(now, v), () =>
                {
                    button.Content = t;
                    Set(prop, v);
                });
            }
            menu.OpenBelow(button);
        };
        return button;
    }

    private Row HoursRow() => new()
    {
        Label = "Visible hours", Hint = "Auto fits your classes. A class outside the range still widens it.", Stacked = true,
        Reset = ResetOf("HoursAuto", "HourStart", "HourEnd"),
        Build = () =>
        {
            string Range(int a, int b) => $"{TimeText.HourLabel(a)} – {TimeText.HourLabel(b)}";
            var options = new[] { ("auto", "Auto"), ("day", Range(7, 19)), ("long", Range(8, 22)) };
            string Now()
            {
                var s = Settings;
                return s.HoursAuto ? "auto" : s is { HourStart: 7, HourEnd: 19 } ? "day" : s is { HourStart: 8, HourEnd: 22 } ? "long" : "";
            }

            void Apply(string key)
            {
                var s = Settings;
                s.HoursAuto = key == "auto";
                if (key == "day") { s.HourStart = 7; s.HourEnd = 19; }
                else if (key == "long") { s.HourStart = 8; s.HourEnd = 22; }
                Changed();
            }

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var group = Guid.NewGuid().ToString();
            var chosen = Now();
            foreach (var (key, text) in options)
            {
                var button = new RadioButton { GroupName = group, Content = text, IsChecked = chosen == key };
                button.SetResourceReference(StyleProperty, "SegItem");
                button.Checked += (_, _) => Apply(key);
                panel.Children.Add(button);
            }
            var pill = new Border { CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1), Padding = new Thickness(1), Child = panel };
            pill.SetResourceReference(Border.BorderBrushProperty, "Brush.LineStrong");
            return pill;
        }
    };

    private Row DaysRow() => new()
    {
        Label = "Days", Hint = "Which days are ticked when you add a class.", Stacked = true, Reset = ResetOf("NewClassDays"),
        Build = () =>
        {
            var panel = new WrapPanel();
            foreach (var day in Enum.GetValues<DayOfWeek>().OrderBy(DayParser.Order))
            {
                var chip = new System.Windows.Controls.Primitives.ToggleButton
                {
                    Content = DayParser.Short(day), Width = 52, Margin = new Thickness(0, 0, 6, 0),
                    IsChecked = Get<List<DayOfWeek>>("NewClassDays").Contains(day)
                };
                chip.SetResourceReference(StyleProperty, "Chip");
                AutomationProperties.SetName(chip, day.ToString());
                chip.Click += (_, _) =>
                {
                    var days = new List<DayOfWeek>(Get<List<DayOfWeek>>("NewClassDays"));
                    if (chip.IsChecked == true) days.Add(day); else days.Remove(day);
                    Set("NewClassDays", days.Distinct().OrderBy(DayParser.Order).ToList());
                };
                panel.Children.Add(chip);
            }
            return panel;
        }
    };

    private Row PaletteRow() => new()
    {
        Label = "Course colors", Hint = "The sixteen colors keep their names; this changes how bright they are.", Stacked = true, Reset = ResetOf("Palette"),
        Build = () =>
        {
            var panel = new WrapPanel();
            var group = Guid.NewGuid().ToString();
            foreach (var name in new[] { "Google", "Pastel", "Vivid" })
            {
                var dots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 8, 0) };
                foreach (var color in ThemeManager.PaletteSample(name))
                    dots.Children.Add(new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Margin = new Thickness(0, 0, 3, 0), Fill = new SolidColorBrush(color) });
                var content = new StackPanel { Orientation = Orientation.Horizontal };
                content.Children.Add(dots);
                content.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });

                var chip = new RadioButton { GroupName = group, Content = content, IsChecked = Get<string>("Palette") == name, Height = 36, Margin = new Thickness(0, 0, 6, 6) };
                chip.SetResourceReference(StyleProperty, "Chip");
                chip.Checked += (_, _) => Set("Palette", name);
                panel.Children.Add(chip);
            }
            return panel;
        }
    };

    private Row AccentRow() => new()
    {
        Label = "Accent color", Hint = "Buttons, today, and highlights.", Reset = ResetOf("Accent"),
        Build = () =>
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var group = Guid.NewGuid().ToString();
            foreach (var name in ThemeManager.AccentNames)
            {
                var swatch = new RadioButton { GroupName = group, ToolTip = name, IsChecked = Get<string>("Accent") == name, Background = new SolidColorBrush(ThemeManager.AccentColor(name)) };
                swatch.SetResourceReference(StyleProperty, "Swatch");
                AutomationProperties.SetName(swatch, name);
                swatch.Checked += (_, _) => Set("Accent", name);
                panel.Children.Add(swatch);
            }
            return panel;
        }
    };

    private static Row NoteRow(string text) => new() { Label = text, Note = true, Build = () => new Border() };

    private Row FileRow() => new()
    {
        Label = "Location", Hint = "Open a different schedule file, or save a copy somewhere else, such as D:\\.", Stacked = true,
        Build = () =>
        {
            var path = Ui.Mono(host.FilePath(), 12);
            path.TextWrapping = TextWrapping.Wrap;
            path.TextTrimming = TextTrimming.None;
            path.ToolTip = host.FilePath();

            var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            Button Make(string text, Action run)
            {
                var button = Ui.Button(text, null, "BtnSecondary", 34);
                button.Margin = new Thickness(0, 0, 8, 0);
                button.Click += (_, _) => run();
                return button;
            }
            buttons.Children.Add(Make("Open…", () => { Close(); Dispatcher.BeginInvoke(host.OpenFile); }));
            buttons.Children.Add(Make("Save as…", () => { Close(); Dispatcher.BeginInvoke(host.SaveAs); }));
            buttons.Children.Add(Make("Show in Explorer", host.ShowFile));

            var stack = new StackPanel();
            stack.Children.Add(path);
            stack.Children.Add(buttons);
            return stack;
        }
    };

    private string? CommitTermName(string text)
    {
        Settings.TermName = text.Trim().Length > 0 ? text.Trim() : "My term";
        Changed();
        return null;
    }

    private string? CommitTermDate(string text, bool start)
    {
        if (!DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var date))
            return start ? "Enter a date like 2026-08-31." : "Enter a date like 2026-12-11.";

        var s = Settings;
        if (start && date.Date > s.TermEnd.Date) return "The term must start before it ends.";
        if (!start && date.Date < s.TermStart.Date) return "The term must end after it starts.";
        if (start) s.TermStart = date.Date; else s.TermEnd = date.Date;
        Changed();
        return null;
    }

    // ---- window parts

    private UIElement BuildNav()
    {
        var column = new Grid { Margin = new Thickness(14, 18, 14, 18) };
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        column.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = Ui.Heading("Settings", 22);
        title.Margin = new Thickness(8, 0, 0, 14);
        column.Children.Add(title);

        search.SetResourceReference(StyleProperty, "Field");
        search.Padding = new Thickness(38, 0, 12, 0);
        search.TextChanged += (_, _) =>
        {
            query = search.Text.Trim();
            searchHint.Visibility = search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            Render();
        };
        AutomationProperties.SetName(search, "Search settings");
        searchHint.Text = "Search settings";
        searchHint.IsHitTestVisible = false;
        searchHint.Margin = new Thickness(40, 0, 0, 0);
        searchHint.VerticalAlignment = VerticalAlignment.Center;
        searchHint.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Ink3");
        var glass = new Icon("Icon.Search", 18, "Brush.Ink3") { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(12, 0, 0, 0) };
        var box = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        box.Children.Add(search);
        box.Children.Add(glass);
        box.Children.Add(searchHint);
        Grid.SetRow(box, 1);
        column.Children.Add(box);

        var list = new StackPanel();
        var group = "settings-nav";
        for (var i = 0; i < sections.Count; i++)
        {
            var section = sections[i];
            var index = i;
            var count = Ui.Text("", 12, "Brush.Ink3");
            count.Visibility = Visibility.Collapsed;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = new Icon(section.Icon, 18);
            var name = new TextBlock { Text = section.Name, VerticalAlignment = VerticalAlignment.Center };
            count.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            Grid.SetColumn(count, 2);
            row.Children.Add(icon);
            row.Children.Add(name);
            row.Children.Add(count);

            var button = new RadioButton { GroupName = group, Content = row, Margin = new Thickness(0, 0, 0, 2) };
            button.SetResourceReference(StyleProperty, "NavItem");
            button.Checked += (_, _) =>
            {
                if (navLock) return;
                current = index;
                if (search.Text.Length > 0) search.Clear();
                else Render();
            };
            nav.Add((section, button, count));
            list.Children.Add(button);
        }
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        Grid.SetRow(scroll, 2);
        column.Children.Add(scroll);

        var foot = new StackPanel();
        foot.Children.Add(footNote);
        foot.Children.Add(resetAll);
        var done = Ui.Button("Done", null, "BtnPrimary", 38);
        done.Margin = new Thickness(0, 8, 0, 0);
        done.IsDefault = true;
        done.Click += (_, _) => Close();
        foot.Children.Add(done);
        Grid.SetRow(foot, 3);
        column.Children.Add(foot);
        return column;
    }

    private UIElement BuildMain()
    {
        mainPanel.Margin = new Thickness(30, 22, 30, 28);
        mainScroll.Content = mainPanel;
        mainScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        mainScroll.Focusable = false;
        return mainScroll;
    }

    private UIElement BuildPreview()
    {
        var stack = new StackPanel { Margin = new Thickness(20, 22, 22, 22) };
        var caption = Ui.Caption("Live preview");
        caption.Margin = new Thickness(2, 0, 0, 10);
        stack.Children.Add(caption);

        previewFrame.Child = previewGrid;
        var scaled = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = previewFrame };
        stack.Children.Add(Ui.Panel(scaled, new Thickness(0, 0, 0, 0), 14));

        var hint = Ui.Text("This is your week, drawn the way these settings will draw it. Clock, week start, hours, class cards, theme, accent and colors update as you change them.", 12.5, "Brush.Ink3");
        hint.TextWrapping = TextWrapping.Wrap;
        hint.TextTrimming = TextTrimming.None;
        hint.Margin = new Thickness(2, 12, 2, 0);
        stack.Children.Add(hint);

        var panel = new Border { Child = stack, BorderThickness = new Thickness(1, 0, 0, 0) };
        panel.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");
        panel.SetResourceReference(Border.BackgroundProperty, "Brush.Canvas");
        return panel;
    }

    // ---- drawing the list

    private bool Matches(Row row, Group group, Section section)
    {
        if (query.Length == 0) return true;
        var text = $"{row.Label} {row.Hint} {group.Name} {section.Name}";
        return text.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private bool HasResettable(Group group) => group.Rows.Any(r => r.Reset is not null);

    private void Render(bool keepScroll = false)
    {
        var offset = mainScroll.VerticalOffset;
        mainPanel.Children.Clear();
        built.Clear();
        var searching = query.Length > 0;
        var shown = 0;
        var step = 0;

        foreach (var (section, button, count) in nav)
        {
            var matching = section.Groups.Sum(g => g.Rows.Count(r => Matches(r, g, section)));
            count.Text = matching.ToString();
            count.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
            button.Visibility = searching && matching == 0 ? Visibility.Collapsed : Visibility.Visible;
        }
        navLock = true;
        for (var i = 0; i < nav.Count; i++) nav[i].Button.IsChecked = !searching && i == current;
        navLock = false;

        foreach (var section in searching ? sections : new List<Section> { sections[current] })
        {
            var headed = false;
            foreach (var group in section.Groups)
            {
                var rows = group.Rows.Where(r => Matches(r, group, section)).ToList();
                if (rows.Count == 0) continue;

                if (!headed)
                {
                    headed = true;
                    var heading = Ui.Heading(section.Name, 22);
                    mainPanel.Children.Add(heading);
                    var sub = Ui.Text(searching ? "" : section.Sub, 13, "Brush.Ink3");
                    sub.Margin = new Thickness(0, 2, 0, 14);
                    sub.TextWrapping = TextWrapping.Wrap;
                    sub.TextTrimming = TextTrimming.None;
                    mainPanel.Children.Add(sub);
                }

                var block = new StackPanel { Margin = new Thickness(0, 0, 0, 24) };
                var name = Ui.Caption(group.Name.ToUpperInvariant());
                name.Margin = new Thickness(0, 0, 0, 2);
                block.Children.Add(name);
                for (var i = 0; i < rows.Count; i++)
                {
                    var element = Layout(rows[i], i == 0);
                    block.Children.Add(element);
                    built.Add((rows[i], element));
                    shown++;
                    if (!keepScroll) Motion.Rise(element, 6, 260, step++ * 18);
                }

                if (!searching && HasResettable(group))
                {
                    var reset = Ui.Button($"Reset {group.Name.ToLowerInvariant()} to defaults", null, "BtnQuiet", 32);
                    reset.HorizontalAlignment = HorizontalAlignment.Left;
                    reset.Margin = new Thickness(-14, 6, 0, 0);
                    reset.Click += (_, _) =>
                    {
                        foreach (var row in group.Rows) row.Reset?.Invoke();
                        Changed();
                        Render(keepScroll: true);
                        Note($"{group.Name} is back to the defaults.");
                    };
                    block.Children.Add(reset);
                }
                mainPanel.Children.Add(block);
            }
        }

        if (shown == 0)
        {
            var none = Ui.Text($"No settings match “{query}”.", 14, "Brush.Ink3");
            none.Margin = new Thickness(0, 40, 0, 0);
            none.HorizontalAlignment = HorizontalAlignment.Center;
            mainPanel.Children.Add(none);
        }

        UpdateEnabled();
        if (keepScroll)
        {
            mainScroll.UpdateLayout();
            mainScroll.ScrollToVerticalOffset(offset);
        }
        else mainScroll.ScrollToTop();
    }

    private FrameworkElement Layout(Row row, bool first)
    {
        if (row.Note)
        {
            var note = Ui.Text(row.Label, 12.5, "Brush.Ink3");
            note.TextWrapping = TextWrapping.Wrap;
            note.TextTrimming = TextTrimming.None;
            note.Margin = new Thickness(0, 10, 0, 0);
            return note;
        }

        var label = Ui.Text(row.Label, 14, "Brush.Ink", FontWeights.Medium);
        label.TextWrapping = TextWrapping.Wrap;
        label.TextTrimming = TextTrimming.None;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(label);
        if (row.Hint.Length > 0)
        {
            var hint = Ui.Text(row.Hint, 12.5, "Brush.Ink3");
            hint.TextWrapping = TextWrapping.Wrap;
            hint.TextTrimming = TextTrimming.None;
            hint.MaxWidth = 380;
            hint.HorizontalAlignment = HorizontalAlignment.Left;
            hint.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(hint);
        }

        var control = row.Build();
        var grid = new Grid();
        if (row.Stacked)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            if (control is FrameworkElement stacked)
            {
                stacked.HorizontalAlignment = HorizontalAlignment.Left;
                stacked.Margin = new Thickness(stacked.Margin.Left, 10, 0, 0);
            }
            Grid.SetRow(control, 1);
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (control is FrameworkElement side)
            {
                side.VerticalAlignment = VerticalAlignment.Center;
                side.Margin = new Thickness(20, 0, 0, 0);
            }
            Grid.SetColumn(control, 1);
        }
        grid.Children.Add(text);
        grid.Children.Add(control);

        var wrapper = new Border { Child = grid, Padding = new Thickness(0, 12, 0, 12), BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0) };
        wrapper.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");
        return wrapper;
    }

    private void UpdateEnabled()
    {
        foreach (var (row, element) in built)
        {
            var on = row.Enabled?.Invoke() ?? true;
            element.IsEnabled = on;
            element.Opacity = on ? 1 : 0.45;
        }
    }

    // ---- reset everything, with a second click to confirm

    private void OnResetAll()
    {
        if (!armed)
        {
            armed = true;
            var text = Ui.Text("Click again to confirm", 13.5, "Brush.Warn", FontWeights.SemiBold);
            text.TextTrimming = TextTrimming.None;
            resetAll.Content = text;
            Note("This puts every preference back to its default. Your classes and term stay as they are.");
            disarm.Stop();
            disarm.Start();
            return;
        }

        Disarm();
        var s = Settings;
        s.ResetPreferences();
        Changed();
        host.ResetWidgetPosition();
        Render();
        Note("Everything is back to the defaults.");
    }

    private void Note(string text)
    {
        footNote.Text = text;
        footNote.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Disarm()
    {
        disarm.Stop();
        if (!armed) return;
        armed = false;
        resetAll.Content = "Reset everything…";
        Note("");
    }
}
