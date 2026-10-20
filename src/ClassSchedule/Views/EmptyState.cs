using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public static class EmptyState
{
    public static UIElement Build(Action onPaste, Action onImport, Action onAddOne, Action onSample)
    {
        var column = new StackPanel { MaxWidth = 560, Margin = new Thickness(32, 40, 32, 40), VerticalAlignment = VerticalAlignment.Center };

        var mark = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(28), HorizontalAlignment = HorizontalAlignment.Left, Child = new Icon("Icon.Calendar", 26, "Brush.OnContainer") };
        mark.SetResourceReference(Border.BackgroundProperty, "Brush.Container");
        column.Children.Add(mark);

        var title = Ui.Heading("Build your schedule", 30);
        title.Margin = new Thickness(0, 20, 0, 6);
        column.Children.Add(title);

        var lead = Ui.Text("Paste your classes, one per line. Days, times, rooms and instructors are worked out for you.", 14, "Brush.Ink3");
        lead.TextWrapping = TextWrapping.Wrap;
        lead.TextTrimming = TextTrimming.None;
        column.Children.Add(lead);

        var sample = new StackPanel();
        foreach (var line in BulkAdd.Example.Split('\n').Take(3)) sample.Children.Add(Ui.Mono(line, 12, "Brush.Ink2"));
        var code = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(18, 14, 18, 14), Margin = new Thickness(0, 24, 0, 0), Child = sample };
        code.SetResourceReference(Border.BackgroundProperty, "Brush.S2");
        column.Children.Add(code);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 20, 0, 0) };
        var paste = Ui.Button("Paste classes", "Icon.Clipboard");
        paste.Click += (_, _) => onPaste();
        var import = Ui.Button("Import file", "Icon.Import", "BtnSecondary");
        import.Margin = new Thickness(8, 0, 0, 0);
        import.Click += (_, _) => onImport();
        var one = Ui.Button("Add one class", "Icon.Plus", "BtnSecondary");
        one.Margin = new Thickness(8, 0, 0, 0);
        one.Click += (_, _) => onAddOne();
        actions.Children.Add(paste);
        actions.Children.Add(import);
        actions.Children.Add(one);
        column.Children.Add(actions);

        column.Children.Add(Ui.Rule(28, 14));
        var trial = Ui.Button("Try a sample schedule", null, "BtnQuiet");
        trial.HorizontalAlignment = HorizontalAlignment.Left;
        trial.Margin = new Thickness(-16, 0, 0, 0);
        trial.Click += (_, _) => onSample();
        column.Children.Add(trial);

        return new ScrollViewer { Content = column, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
    }
}
