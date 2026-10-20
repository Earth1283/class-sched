using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public sealed class BulkAddDialog : Window
{
    private readonly ScheduleData data;
    private readonly TextBox input;
    private readonly TextBlock placeholder;
    private readonly TextBlock note;
    private readonly StackPanel previewList = new();
    private readonly StackPanel counts = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly Button add;
    private readonly DispatcherTimer debounce = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private List<BulkRow> rows = new();

    public IReadOnlyList<BulkRow> Rows => rows;

    public BulkAddDialog(Window owner, ScheduleData data)
    {
        this.data = data;
        Owner = owner;
        Title = "Bulk add classes";
        Width = 980;
        Height = 660;
        MinWidth = 780;
        MinHeight = 480;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "Brush.S1");
        SetResourceReference(ForegroundProperty, "Brush.Ink");
        SetResourceReference(FontFamilyProperty, "Font.Body");
        SourceInitialized += (_, _) => ThemeManager.StyleTitleBar(this);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(24, 20, 24, 16) };
        header.Children.Add(Ui.Caption("Bulk add"));
        var title = Ui.Heading("Paste your classes", 20);
        title.Margin = new Thickness(0, 4, 0, 4);
        header.Children.Add(title);
        header.Children.Add(Ui.Text("One class per line. Type them the way you would say them, or paste rows straight from a spreadsheet.", 13, "Brush.Ink3"));
        root.Children.Add(header);

        input = new TextBox { AllowDrop = true, AcceptsTab = false };
        input.SetResourceReference(FrameworkElement.StyleProperty, "Editor");
        input.FontSize = 12.5;
        input.TextChanged += (_, _) =>
        {
            placeholder!.Visibility = input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            debounce.Stop();
            debounce.Start();
        };
        input.PreviewDragOver += (_, e) =>
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        };
        input.PreviewDrop += OnDrop;
        debounce.Tick += (_, _) => { debounce.Stop(); Refresh(); };

        placeholder = Ui.Text(BulkAdd.Example, 12.5, "Brush.Ink4", FontWeights.Normal, "Font.Mono");
        placeholder.TextTrimming = TextTrimming.None;
        placeholder.Margin = new Thickness(15, 13, 15, 0);
        placeholder.IsHitTestVisible = false;
        placeholder.VerticalAlignment = VerticalAlignment.Top;

        var editor = new Grid();
        editor.Children.Add(input);
        editor.Children.Add(placeholder);

        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var example = Ui.Button("Insert example", null, "BtnQuiet", 26);
        example.Padding = new Thickness(8, 0, 8, 0);
        example.Click += (_, _) => { input.Text = BulkAdd.Example; input.Focus(); input.CaretIndex = input.Text.Length; };
        var clear = Ui.Button("Clear", null, "BtnQuiet", 26);
        clear.Padding = new Thickness(8, 0, 8, 0);
        clear.Click += (_, _) => { input.Clear(); note!.Text = ""; input.Focus(); };
        tools.Children.Add(example);
        tools.Children.Add(clear);

        var leftHeader = new Grid { Margin = new Thickness(0, 0, 0, 8), Height = 26 };
        var yours = Ui.Caption("Your classes");
        yours.VerticalAlignment = VerticalAlignment.Center;
        leftHeader.Children.Add(yours);
        leftHeader.Children.Add(tools);

        note = Ui.Text("Days like MWF or Tue/Thu. Times like 9-9:50 or 1:00-2:15pm. Room, instructor and (Lab) are picked up too.", 12, "Brush.Ink3");
        note.TextWrapping = TextWrapping.Wrap;
        note.TextTrimming = TextTrimming.None;
        note.Margin = new Thickness(0, 10, 0, 0);

        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition());
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.Children.Add(leftHeader);
        Grid.SetRow(editor, 1);
        left.Children.Add(editor);
        Grid.SetRow(note, 2);
        left.Children.Add(note);

        var previewHeader = new Grid { Margin = new Thickness(0, 0, 0, 8), Height = 26 };
        var preview = Ui.Caption("Preview");
        preview.VerticalAlignment = VerticalAlignment.Center;
        previewHeader.Children.Add(preview);
        counts.VerticalAlignment = VerticalAlignment.Center;
        previewHeader.Children.Add(counts);

        var listBox = new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), ClipToBounds = true, Child = new ScrollViewer { Content = previewList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        listBox.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");

        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition());
        right.Children.Add(previewHeader);
        Grid.SetRow(listBox, 1);
        right.Children.Add(listBox);

        var body = new Grid { Margin = new Thickness(24, 0, 24, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(right, 2);
        body.Children.Add(left);
        body.Children.Add(right);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        var footer = new Grid { Margin = new Thickness(24, 16, 24, 20) };
        var drop = Ui.Text("Tip: drop a .csv file onto the box to load it. Ctrl+Enter adds.", 12, "Brush.Ink4");
        drop.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(drop);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = Ui.Button("Cancel", null, "BtnQuiet");
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(0, 0, 6, 0);
        add = Ui.Button("Add classes");
        add.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(add);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);

        Content = root;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control && add.IsEnabled) { Accept(); e.Handled = true; }
        };
        ContentRendered += (_, _) => input.Focus();
        Loaded += (_, _) => TryFillFromClipboard();
        Refresh();
    }

    private void Accept()
    {
        debounce.Stop();
        Refresh();
        if (!rows.Any(r => r.CanAdd)) return;
        DialogResult = true;
    }

    private void TryFillFromClipboard()
    {
        try
        {
            if (input.Text.Length > 0 || !Clipboard.ContainsText()) return;
            var text = Clipboard.GetText();
            if (text.Length is 0 or > 20000) return;
            var found = BulkAdd.Parse(text, data);
            if (!found.Any(r => r.Status == BulkStatus.Ok)) return;
            input.Text = text.TrimEnd();
            note.Text = "Filled in from your clipboard. Clear it if that was not meant for this.";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop) || e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        e.Handled = true;

        var path = files[0];
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".ics")
        {
            note.Text = "Calendar (.ics) files go through More > Import instead.";
            return;
        }
        try
        {
            input.Text = File.ReadAllText(path).TrimEnd();
            note.Text = $"Loaded {Path.GetFileName(path)}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            note.Text = "That file could not be read: " + ex.Message;
        }
    }

    private void Refresh()
    {
        rows = BulkAdd.Parse(input.Text, data);
        previewList.Children.Clear();
        counts.Children.Clear();

        var ready = rows.Count(r => r.CanAdd);
        var problems = rows.Count(r => r.Status == BulkStatus.Error);
        var skipped = rows.Count(r => r.Status == BulkStatus.Duplicate);

        if (rows.Count == 0)
        {
            var hint = new StackPanel { Margin = new Thickness(20, 28, 20, 28), HorizontalAlignment = HorizontalAlignment.Center };
            hint.Children.Add(Ui.Text("Nothing to preview yet", 13, "Brush.Ink2", FontWeights.Medium));
            var sub = Ui.Text("Classes show up here as you type, so you can check them before adding.", 12.5, "Brush.Ink4");
            sub.TextWrapping = TextWrapping.Wrap;
            sub.TextTrimming = TextTrimming.None;
            sub.TextAlignment = TextAlignment.Center;
            hint.Children.Add(sub);
            previewList.Children.Add(hint);
        }
        foreach (var row in rows) previewList.Children.Add(PreviewRow(row));

        void Count(string text, string brush)
        {
            var t = Ui.Mono(text, 11.5, brush);
            t.Margin = new Thickness(counts.Children.Count == 0 ? 0 : 12, 0, 0, 0);
            counts.Children.Add(t);
        }
        if (ready > 0) Count($"{ready} ready", "Brush.Ink2");
        if (problems > 0) Count($"{problems} need attention", "Brush.Warn");
        if (skipped > 0) Count($"{skipped} skipped", "Brush.Ink4");

        add.IsEnabled = ready > 0;
        add.Content = ready switch { 0 => "Add classes", 1 => "Add 1 class", _ => $"Add {ready} classes" };
    }

    private static UIElement PreviewRow(BulkRow row)
    {
        var (key, brush) = row.Status switch
        {
            BulkStatus.Ok => ("Icon.Check", "Brush.Ok"),
            BulkStatus.Assumed => ("Icon.Clock", "Brush.Ink3"),
            BulkStatus.Duplicate => ("Icon.Dash", "Brush.Ink4"),
            _ => ("Icon.Alert", "Brush.Warn")
        };

        var icon = new Icon(key, 15, brush) { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
        var text = new StackPanel();

        if (row.Status == BulkStatus.Error)
        {
            text.Children.Add(Ui.Mono(row.Source, 12, "Brush.Ink2"));
            var why = Ui.Text(row.Message, 12, "Brush.Warn");
            why.Margin = new Thickness(0, 2, 0, 0);
            text.Children.Add(why);
        }
        else
        {
            var title = new StackPanel { Orientation = Orientation.Horizontal };
            title.Children.Add(Ui.Heading(row.Code, 13, row.Status == BulkStatus.Duplicate ? "Brush.Ink3" : "Brush.Ink"));
            if (row.Name.Length > 0 && row.Name != row.Code)
            {
                var name = Ui.Text(row.Name, 13, "Brush.Ink2");
                name.Margin = new Thickness(8, 0, 0, 0);
                title.Children.Add(name);
            }
            if (!row.Kind.Equals("Lecture", StringComparison.OrdinalIgnoreCase))
            {
                var badge = Ui.Badge(row.Kind);
                badge.Margin = new Thickness(8, 0, 0, 0);
                title.Children.Add(badge);
            }
            text.Children.Add(title);

            var when = string.Join(" ", row.Days.Select(DayParser.Short)) + " · " + TimeText.Range(row.Start, row.End);
            var detail = string.Join(" · ", new[] { when, row.Location, row.Instructor }.Where(s => s.Length > 0));
            var meta = Ui.Mono(detail, 11.5, "Brush.Ink3");
            meta.Margin = new Thickness(0, 3, 0, 0);
            text.Children.Add(meta);

            if (row.Message.Length > 0)
            {
                var info = Ui.Text(row.Message, 12, "Brush.Ink4");
                info.Margin = new Thickness(0, 2, 0, 0);
                text.Children.Add(info);
            }
        }

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(text, 1);
        grid.Children.Add(icon);
        grid.Children.Add(text);

        var border = new Border { Child = grid, Padding = new Thickness(14, 10, 14, 10), BorderThickness = new Thickness(0, 0, 0, 1) };
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Line");
        if (row.Status == BulkStatus.Duplicate) border.Opacity = 0.7;
        return border;
    }
}
