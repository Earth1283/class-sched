using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ClassSchedule.Views;

internal static class Ui
{
    public static readonly IEasingFunction Smooth = new QuinticEase { EasingMode = EasingMode.EaseOut };

    /// <summary>Tighter rows in lists and panels (the Density setting).</summary>
    public static bool Compact { get; set; }

    /// <summary>How much the whole interface is scaled (the Interface size setting).</summary>
    public static double Scale { get; set; } = 1;

    public static double Row(double comfortable, double compact) => Compact ? compact : comfortable;

    public static TextBlock Text(string text, double size = 13, string brush = "Brush.Ink",
        FontWeight? weight = null, string font = "Font.Body")
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        t.SetResourceReference(TextBlock.FontFamilyProperty, font);
        return t;
    }

    public static TextBlock Caption(string text)
    {
        var t = Text(text, 12, "Brush.Ink3", FontWeights.Medium);
        t.SetResourceReference(FrameworkElement.StyleProperty, "Caption");
        return t;
    }

    public static TextBlock Mono(string text, double size = 11.5, string brush = "Brush.Ink3") =>
        Text(text, size, brush, FontWeights.Normal, "Font.Mono");

    public static TextBlock Heading(string text, double size = 15, string brush = "Brush.Ink") =>
        Text(text, size, brush, FontWeights.Medium, "Font.Display");

    public static Border Panel(UIElement child, Thickness padding, double radius = 16)
    {
        var edge = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(radius, 0, radius, 0), IsHitTestVisible = false };
        edge.SetResourceReference(Border.BackgroundProperty, "Brush.Edge");

        var layers = new Grid();
        layers.Children.Add(new Border { Child = child, Padding = padding });
        layers.Children.Add(edge);

        var panel = new Border { Child = layers, CornerRadius = new CornerRadius(radius) };
        panel.SetResourceReference(FrameworkElement.StyleProperty, "Panel");
        return panel;
    }

    public static Border Popover(UIElement child, Thickness padding)
    {
        var popover = new Border { Child = child, Padding = padding };
        popover.SetResourceReference(FrameworkElement.StyleProperty, "Popover");
        return popover;
    }

    public static Border Rule(double top = 0, double bottom = 0)
    {
        var rule = new Border { Height = 1, Margin = new Thickness(0, top, 0, bottom) };
        rule.SetResourceReference(Border.BackgroundProperty, "Brush.Line");
        return rule;
    }

    public static Border Badge(string text, string foreground = "Brush.Ink2")
    {
        var badge = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 1, 9, 2), Child = Text(text, 12, foreground, FontWeights.Medium) };
        badge.SetResourceReference(Border.BackgroundProperty, "Brush.S3");
        return badge;
    }

    public static Button Button(string text, string? iconKey = null, string kind = "BtnPrimary", double height = 40)
    {
        var button = new Button { Height = height };
        button.SetResourceReference(FrameworkElement.StyleProperty, kind);
        if (iconKey is null) { button.Content = text; return button; }

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon(iconKey, 15));
        row.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        button.Content = row;
        return button;
    }

    public static Button IconButton(string iconKey, string tip)
    {
        var button = new Button { Content = new Icon(iconKey, 16), ToolTip = tip };
        button.SetResourceReference(FrameworkElement.StyleProperty, "IconBtn");
        System.Windows.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }
}
