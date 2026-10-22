using ClassSchedule.Core;

namespace ClassSchedule.Views;

public partial class MainWindow
{
    /// <summary>
    /// A class was dropped on a new time or day. A class that meets on several days and only changed time asks whether the
    /// change is for every day or just the dragged one; everything else applies at once, with Undo.
    /// </summary>
    private void OnSessionMoved(SessionMove move)
    {
        var session = move.Placed.Session;
        if (session.Days.Count <= 1 || move.ToDay != move.FromDay)
        {
            ApplyMove(move, EditScope.ThisDay);
            return;
        }

        var menu = new MenuPopup($"Change {move.Placed.Course.Code} {session.Kind} to {TimeText.Range(move.Start, move.End)}");
        menu.Add("Icon.Calendar", $"All {session.Days.Count} days ({DayParser.Abbreviations(session.Days)})", "", () => ApplyMove(move, EditScope.AllDays));
        menu.Add("Icon.Clock", $"{move.FromDay} only", "", () => ApplyMove(move, EditScope.ThisDay));
        menu.Open(move.Card);
    }

    private void ApplyMove(SessionMove move, EditScope scope)
    {
        var session = move.Placed.Session;
        var onlyOneDay = session.Days.Count > 1 && (scope == EditScope.ThisDay || move.ToDay != move.FromDay);
        var undo = SessionEdits.Reschedule(data, session.Id, move.FromDay, move.ToDay, move.Start, move.End, scope);
        if (undo is null) return;

        Commit();
        var day = onlyOneDay || move.ToDay != move.FromDay ? $"{move.ToDay} " : "";
        ShowUndo($"{move.Placed.Course.Code} {session.Kind} is now {day}{TimeText.Range(move.Start, move.End)}", undo);
    }
}
