using System;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public partial class MainWindow
{
    private void AddBreak(DateTime first, DateTime last)
    {
        if (new BreakDialog(this, data, null, first, last).ShowDialog() == true) Commit();
    }

    private void EditBreak(TermBreak item)
    {
        var dialog = new BreakDialog(this, data, item);
        if (dialog.ShowDialog() != true) return;

        Commit();
        if (dialog.UndoDelete is { } undo) ShowUndo($"Deleted {item.Name}", undo);
    }

    private void RemoveBreak(TermBreak item)
    {
        var index = data.Settings.Breaks.IndexOf(item);
        data.Settings.Breaks.Remove(item);
        Commit();
        ShowUndo($"Removed {item.Name}", () => data.Settings.Breaks.Insert(Math.Min(index, data.Settings.Breaks.Count), item));
    }
}
