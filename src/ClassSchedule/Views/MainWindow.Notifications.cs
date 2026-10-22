using System;
using System.Windows;
using ClassSchedule.Core;
using Microsoft.Toolkit.Uwp.Notifications;

namespace ClassSchedule.Views;

public partial class MainWindow
{
    private void OnClockTick()
    {
        foreach (var reminder in ReminderPlanner.Due(data, DateTime.Now, remindersSent)) Notify(reminder.Title, reminder.Body);
        if (IsVisible && !DetailPopup.IsOpen) Refresh(keepScroll: true);
    }

    private void Notify(string title, string body)
    {
        if (data.Settings.ReminderSound)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); }
            catch (Exception) { }
        }
        if (IsActive || (IsVisible && !data.Settings.ReminderViaWindows)) { ShowToast(title, body); return; }
        try
        {
            new ToastContentBuilder().AddText(title).AddText(body).Show();
        }
        catch (Exception)
        {
            ShowToast(title, body);
        }
    }

    public void ShowToast(string title, string body, string? actionLabel = null, Action? action = null)
    {
        ToastTitle.Text = title;
        ToastBody.Text = body;
        ToastBody.Visibility = body.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        toastAction = action;
        ToastAction.Content = actionLabel ?? "";
        ToastAction.Visibility = action is null ? Visibility.Collapsed : Visibility.Visible;
        toastTimer.Interval = TimeSpan.FromSeconds(action is null ? 5 : Math.Clamp(data.Settings.UndoSeconds, 3, 30));

        Motion.Reset(ToastHost);
        ToastHost.Visibility = Visibility.Visible;
        toastTimer.Stop();
        toastTimer.Start();
        Motion.Rise(ToastHost, 16, 350, 0, Motion.Spring, 0.96);
    }

    /// <summary>Says what just happened and offers to take it back.</summary>
    private void ShowUndo(string message, Action undo) =>
        ShowToast(message, "", "Undo", () =>
        {
            undo();
            animateNext = true;
            Commit();
        });

    private void OnToastAction(object sender, RoutedEventArgs e)
    {
        var run = toastAction;
        toastAction = null;
        toastTimer.Stop();
        ToastHost.Visibility = Visibility.Collapsed;
        Motion.Reset(ToastHost);
        run?.Invoke();
    }
}
