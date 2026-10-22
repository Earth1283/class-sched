using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ClassSchedule.Core;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.Win32;

namespace ClassSchedule.Views;

public partial class MainWindow
{
    private bool WidgetRunning => widget is { IsRunning: true };

    private void MoveWidgetToCorner(bool right, bool bottom)
    {
        if (widget is not { IsVisible: true }) return;
        widget.MoveToCorner(right, bottom);
    }

    private void ResetWidgetPosition()
    {
        data.Settings.WidgetLeft = null;
        data.Settings.WidgetTop = null;
        widget?.PlaceFromSettings(data.Settings);
        Commit();
    }

    private void ApplyWidget()
    {
        var s = data.Settings;
        if (!s.WidgetEnabled)
        {
            widget?.Stop();
            return;
        }

        if (widget is null)
        {
            widget = new TodayWidget(data);
            widget.OpenRequested += ShowMain;
            widget.QuitRequested += Quit;
            widget.HideRequested += () =>
            {
                data.Settings.WidgetEnabled = false;
                Commit();
                if (!IsVisible) ShowMain();
            };
            widget.PreferencesEdited += Commit;
            widget.Moved += () =>
            {
                data.Settings.WidgetLeft = widget.Left;
                data.Settings.WidgetTop = widget.Top;
                try { store.Save(data); }
                catch (Exception) { }
            };
        }

        widget.Apply(s);
        if (!widget.IsVisible && !widget.AutoHidden)
        {
            widget.PlaceFromSettings(s);
            widget.Show();
        }
    }
}
