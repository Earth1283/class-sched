using System;
using System.Windows;
using ClassSchedule.Core;

namespace ClassSchedule.Views;

public partial class MainWindow
{
    private bool Confirm(string text)
    {
        if (!data.Settings.ConfirmDeletes) return true;
        return MessageBox.Show(this, text, "Class Schedule", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
    }

    private void OnAddClass(object sender, RoutedEventArgs e) => AddClass();

    private void AddClass() => AddClass(null);

    private void AddClass(ClassPrefill? prefill)
    {
        bool again;
        do
        {
            var dialog = new ClassDialog(this, data, null, prefill);
            var saved = dialog.ShowDialog() == true;
            if (saved) Commit();
            again = saved && dialog.AddAnother;
            prefill = null;
        }
        while (again);
    }

    private void OnCreate(object sender, RoutedEventArgs e) => CreatePopup.IsOpen = !CreatePopup.IsOpen;

    private void OnCreateClass(object sender, RoutedEventArgs e)
    {
        CreatePopup.IsOpen = false;
        AddClass();
    }

    private void OnBulkAdd(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        CreatePopup.IsOpen = false;
        OpenBulkAdd();
    }

    private void OpenBulkAdd()
    {
        var dialog = new BulkAddDialog(this, data);
        if (dialog.ShowDialog() != true) return;

        var result = BulkAdd.Apply(dialog.Rows, data);
        if (result.AddedAnything) Commit();
        ShowToast(result.Summary(), "");
    }

    private void LoadSample()
    {
        var sample = SampleData.Create(DateTime.Today);
        sample.Settings.KeepPreferencesFrom(data.Settings);
        data.Settings = sample.Settings;
        data.Courses.AddRange(sample.Courses);
        data.Sessions.AddRange(sample.Sessions);
        data.Tasks.AddRange(sample.Tasks);
        Commit();
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        if (data.Sessions.Count + data.Tasks.Count == 0) return;

        var answer = MessageBox.Show(this,
            $"Remove all {data.Sessions.Count} classes and {data.Tasks.Count} assignments and exams?\nYou can undo this right afterwards.",
            "Clear schedule", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;

        var undo = data.ClearWithUndo();
        Commit();
        ShowUndo("Cleared the schedule", undo);
    }

    private void AddTask() => AddTask(null);

    private void AddTask(DateTime? due, Guid? courseId = null)
    {
        if (new TaskDialog(this, data, null, due, courseId).ShowDialog() == true) Commit();
    }

    private void EditTask(TaskItem task)
    {
        var dialog = new TaskDialog(this, data, task);
        if (dialog.ShowDialog() != true) return;

        Commit();
        if (dialog.UndoDelete is { } undo) ShowUndo($"Deleted {task.Title}", undo);
    }

    private void ToggleDone(TaskItem task)
    {
        task.Done = !task.Done;
        Commit();
    }

    private void ToggleCourse(Course course)
    {
        course.Visible = !course.Visible;
        poppedCourse = course.Id;
        animateNext = true;
        Commit();
    }

    private void OnAddTask(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        CreatePopup.IsOpen = false;
        AddTask();
    }

    private void OnMore(object sender, RoutedEventArgs e) => MorePopup.IsOpen = !MorePopup.IsOpen;

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        var host = new SettingsHost(Commit, ResetWidgetPosition, MoveWidgetToCorner, () => BackUp(true), ShowFileInExplorer, () => store.Path,
            () => OnOpenFile(this, new RoutedEventArgs()), () => OnSaveAs(this, new RoutedEventArgs()), () => data);
        new SettingsWindow(this, host).ShowDialog();
        Commit();
    }
}
