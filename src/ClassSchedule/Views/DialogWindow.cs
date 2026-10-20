using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ClassSchedule.Views;

public abstract class DialogWindow : Window
{
    private readonly StackPanel form = new();
    private readonly TextBlock error;
    private readonly StackPanel leftActions = new() { Orientation = Orientation.Horizontal };
    private readonly StackPanel right = new() { Orientation = Orientation.Horizontal };

    protected DialogWindow(Window owner, string eyebrow, string title, double width = 460)
    {
        Owner = owner;
        Width = width * Ui.Scale;
        Title = title;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        FontSize = 13;
        SetResourceReference(BackgroundProperty, "Brush.S1");
        SetResourceReference(ForegroundProperty, "Brush.Ink");
        SetResourceReference(FontFamilyProperty, "Font.Body");
        SourceInitialized += (_, _) => ThemeManager.StyleTitleBar(this);

        var root = new StackPanel { Margin = new Thickness(24, 22, 24, 22) };
        root.Children.Add(Ui.Caption(eyebrow));
        var heading = Ui.Heading(title, 20);
        heading.Margin = new Thickness(0, 4, 0, 20);
        root.Children.Add(heading);
        root.Children.Add(form);

        error = Ui.Text("", 12.5, "Brush.Warn");
        error.TextWrapping = TextWrapping.Wrap;
        error.Margin = new Thickness(0, 0, 0, 10);
        error.MinHeight = 18;
        root.Children.Add(error);

        var cancel = Ui.Button("Cancel", null, "BtnQuiet");
        cancel.IsCancel = true;
        cancel.Margin = new Thickness(0, 0, 6, 0);
        var save = Ui.Button("Save");
        save.IsDefault = true;
        save.Click += (_, _) =>
        {
            if (TrySave(out var message)) DialogResult = true;
            else error.Text = message;
        };

        var footer = new Grid();
        footer.Children.Add(leftActions);
        right.HorizontalAlignment = HorizontalAlignment.Right;
        right.Children.Add(cancel);
        right.Children.Add(save);
        footer.Children.Add(right);
        root.Children.Add(footer);
        if (Ui.Scale != 1) root.LayoutTransform = new ScaleTransform(Ui.Scale, Ui.Scale);
        Content = root;
    }

    protected abstract bool TrySave(out string message);

    protected void ShowError(string message) => error.Text = message;

    protected void AddRightAction(Button button)
    {
        button.Margin = new Thickness(0, 0, 6, 0);
        right.Children.Insert(right.Children.Count - 2, button);
    }

    protected void AddLeftAction(Button button)
    {
        button.Margin = new Thickness(0, 0, 6, 0);
        leftActions.Children.Add(button);
    }

    protected int Mark() => form.Children.Count;

    /// <summary>The rows added since <paramref name="mark"/>, so a group of them can be shown or hidden together.</summary>
    protected List<UIElement> Since(int mark) => form.Children.Cast<UIElement>().Skip(mark).ToList();

    protected void AddElement(UIElement element) => form.Children.Add(element);

    protected void Add(string label, UIElement control, double bottom = 14)
    {
        var field = new StackPanel { Margin = new Thickness(0, 0, 0, bottom) };
        var caption = Ui.Caption(label);
        caption.Margin = new Thickness(0, 0, 0, 5);
        field.Children.Add(caption);
        field.Children.Add(control);
        form.Children.Add(field);
    }

    protected void AddRow(params (string Label, UIElement Control, double Width)[] cells)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        for (var i = 0; i < cells.Length; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = cells[i].Width > 0 ? new GridLength(cells[i].Width) : new GridLength(1, GridUnitType.Star) });
            var cell = new StackPanel { Margin = new Thickness(i == 0 ? 0 : 12, 0, 0, 0) };
            var caption = Ui.Caption(cells[i].Label);
            caption.Margin = new Thickness(0, 0, 0, 5);
            cell.Children.Add(caption);
            cell.Children.Add(cells[i].Control);
            Grid.SetColumn(cell, i);
            grid.Children.Add(cell);
        }
        form.Children.Add(grid);
    }

    protected static TextBox Box(string text = "")
    {
        var box = new TextBox { Text = text };
        box.SetResourceReference(FrameworkElement.StyleProperty, "Field");
        return box;
    }

    protected static ToggleButton Chip(string text, bool isChecked = false, string? group = null)
    {
        ToggleButton chip = group is null ? new ToggleButton() : new RadioButton { GroupName = group };
        chip.Content = text;
        chip.IsChecked = isChecked;
        chip.Margin = new Thickness(0, 0, 6, 6);
        chip.SetResourceReference(FrameworkElement.StyleProperty, "Chip");
        return chip;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (form.Children.Count > 0) MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }
}
