using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ClassSchedule.Views;

/// <summary>A right-click menu in the app's own style, opening at the pointer.</summary>
public sealed class MenuPopup
{
    private readonly Popup popup = new()
    {
        StaysOpen = false,
        AllowsTransparency = true,
        Placement = PlacementMode.MousePoint,
        PopupAnimation = PopupAnimation.None
    };
    private readonly StackPanel stack = new();
    private readonly Border shell;

    public MenuPopup(string? caption = null)
    {
        if (!string.IsNullOrEmpty(caption))
        {
            var title = Ui.Text(caption, 12, "Brush.Ink3", FontWeights.Medium);
            title.Margin = new Thickness(12, 6, 12, 6);
            stack.Children.Add(title);
        }

        shell = Ui.Popover(stack, new Thickness(6));
        shell.Width = 276;
        shell.Margin = new Thickness(10);
        popup.Child = shell;
        popup.Opened += (_, _) => Animate();
        popup.Closed += (_, _) => Closed?.Invoke();
    }

    public event Action? Closed;

    public void Add(string icon, string text, string shortcut, Action action, bool danger = false)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var glyph = new Icon(icon, 18, danger ? "Brush.Warn" : "Brush.Ink2");
        glyph.HorizontalAlignment = HorizontalAlignment.Left;
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        if (danger) label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warn");
        Grid.SetColumn(label, 1);
        grid.Children.Add(glyph);
        grid.Children.Add(label);

        if (shortcut.Length > 0)
        {
            var hint = Ui.Text(shortcut, 12, "Brush.Ink4");
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(hint, 2);
            grid.Children.Add(hint);
        }

        var button = new Button { Content = grid, Height = 36, Padding = new Thickness(12, 0, 12, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        button.SetResourceReference(FrameworkElement.StyleProperty, "MenuItem");
        button.Click += (_, _) =>
        {
            popup.IsOpen = false;
            action();
        };
        stack.Children.Add(button);
    }

    public void Separator()
    {
        var rule = Ui.Rule(5, 5);
        rule.Margin = new Thickness(6, 5, 6, 5);
        stack.Children.Add(rule);
    }

    public void AddOption(string text, bool selected, Action action)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        if (selected)
        {
            var mark = new Icon("Icon.Check", 16, "Brush.Primary") { HorizontalAlignment = HorizontalAlignment.Left };
            grid.Children.Add(mark);
        }
        var label = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);

        var button = new Button { Content = grid, Height = 36, Padding = new Thickness(12, 0, 12, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch };
        button.SetResourceReference(FrameworkElement.StyleProperty, "MenuItem");
        button.Click += (_, _) =>
        {
            popup.IsOpen = false;
            action();
        };
        stack.Children.Add(button);
    }

    /// <summary>A row of the course colors; picking one closes the menu.</summary>
    public void AddSwatches(IEnumerable<string> keys, string selected, Action<string> pick)
    {
        var wrap = new WrapPanel { Margin = new Thickness(10, 4, 10, 6) };
        var group = Guid.NewGuid().ToString();
        foreach (var key in keys)
        {
            var swatch = new RadioButton { GroupName = group, IsChecked = key == selected, ToolTip = key };
            swatch.SetResourceReference(FrameworkElement.StyleProperty, "Swatch");
            swatch.SetResourceReference(Control.BackgroundProperty, $"Course.{key}.Dot");
            System.Windows.Automation.AutomationProperties.SetName(swatch, key);
            var chosen = key;
            swatch.Click += (_, _) =>
            {
                popup.IsOpen = false;
                if (chosen != selected) pick(chosen);
            };
            wrap.Children.Add(swatch);
        }
        stack.Children.Add(wrap);
    }

    /// <summary>Opens the menu under <paramref name="target"/>, as wide as it, like a drop-down list.</summary>
    public void OpenBelow(FrameworkElement target)
    {
        popup.Placement = PlacementMode.Bottom;
        popup.VerticalOffset = -4;
        shell.Width = Math.Max(target.ActualWidth, 220) + 20;
        shell.MaxHeight = 360;
        Open(target);
    }

    public void Open(UIElement target)
    {
        popup.PlacementTarget = target;
        popup.IsOpen = true;
    }

    private void Animate()
    {
        Motion.PopIn(shell);
        var index = 0;
        foreach (var item in stack.Children.OfType<Button>()) Motion.Rise(item, 3, 200, 30 + 18 * index++);
    }
}
