using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

/// <summary>Adds or edits a stretch of days with no classes, such as a holiday or reading week.</summary>
public sealed class BreakDialog : DialogWindow
{
    private readonly ScheduleData data;
    private readonly TermBreak? existing;
    private readonly TextBox name, from, to;

    public BreakDialog(Window owner, ScheduleData data, TermBreak? existing, DateTime? first = null, DateTime? last = null)
        : base(owner, existing is null ? "New break" : "Edit break", existing is null ? "Mark days off" : "Edit break")
    {
        this.data = data;
        this.existing = existing;
        var start = existing?.Start ?? first ?? DateTime.Today;
        var end = existing?.End ?? last ?? start;

        name = Box(existing?.Name ?? "");
        from = Box(start.ToString("d", CultureInfo.CurrentCulture));
        to = Box(end.ToString("d", CultureInfo.CurrentCulture));

        Add("Name", name);
        AddRow(("First day", from, 0), ("Last day", to, 0));
        Add("", Ui.Text("No classes meet on these days. They are shaded in the schedule, and left out of reminders and calendar exports.", 12, "Brush.Ink3"), 4);

        if (existing is not null)
        {
            var delete = Ui.Button("Delete", null, "BtnQuiet");
            delete.Click += (_, _) =>
            {
                var index = data.Settings.Breaks.IndexOf(existing);
                data.Settings.Breaks.Remove(existing);
                UndoDelete = () => data.Settings.Breaks.Insert(Math.Min(index, data.Settings.Breaks.Count), existing);
                DialogResult = true;
            };
            AddLeftAction(delete);
        }
    }

    /// <summary>Set when the break was deleted from the dialog; calling it puts the break back.</summary>
    public Action? UndoDelete { get; private set; }

    protected override bool TrySave(out string message)
    {
        message = "";
        if (!DateTime.TryParse(from.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var first)) { message = "Enter the first day like 2026-11-25."; return false; }
        if (!DateTime.TryParse(to.Text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var last)) { message = "Enter the last day like 2026-11-29."; return false; }
        if (last.Date < first.Date) { message = "The last day can't be before the first."; return false; }

        var label = name.Text.Trim().Length > 0 ? name.Text.Trim() : "Break";
        var item = existing ?? new TermBreak();
        item.Name = label;
        item.Start = first.Date;
        item.End = last.Date;
        if (existing is null) data.Settings.Breaks.Add(item);
        data.Settings.Breaks = data.Settings.Breaks.OrderBy(b => b.Start).ToList();
        return true;
    }
}
