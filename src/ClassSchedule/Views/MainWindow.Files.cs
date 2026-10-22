using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using ClassSchedule.Core;
using Microsoft.Win32;

namespace ClassSchedule.Views;

public partial class MainWindow
{
    public void BackUp(bool announce)
    {
        try
        {
            var copy = ScheduleBackups.CreateAndPrune(store.Path, data.Settings.BackupCount, DateTime.Now);
            if (announce) ShowToast(copy is null ? "Nothing to back up yet" : "Backed up", copy ?? "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (announce) ShowToast("The backup could not be made", ex.Message);
        }
    }

    private void ShowFileInExplorer()
    {
        try
        {
            if (File.Exists(store.Path)) System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{store.Path}\"");
            else if (Path.GetDirectoryName(store.Path) is { } dir && Directory.Exists(dir)) System.Diagnostics.Process.Start("explorer.exe", $"\"{dir}\"");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowToast("Explorer could not be opened", ex.Message);
        }
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        ImportFile();
    }

    private void ImportFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import schedule",
            Filter = "Schedule files (*.csv;*.ics)|*.csv;*.ics|CSV (*.csv)|*.csv|Calendar (*.ics)|*.ics"
        };
        if (dialog.ShowDialog(this) != true) return;

        ImportResult result;
        try
        {
            var text = File.ReadAllText(dialog.FileName);
            result = Path.GetExtension(dialog.FileName).Equals(".ics", StringComparison.OrdinalIgnoreCase)
                ? IcsService.Import(text, data)
                : CsvImporter.Import(text, data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("The file could not be read", ex.Message);
            return;
        }

        if (result.AddedAnything) Commit();
        ShowToast(result.Summary(), result.Errors.Count > 0 ? $"{result.Errors.Count} problem{(result.Errors.Count == 1 ? "" : "s")} found." : "");

        if (result.Errors.Count > 0)
        {
            var lines = string.Join(Environment.NewLine, result.Errors.Take(8));
            var more = result.Errors.Count > 8 ? $"{Environment.NewLine}…and {result.Errors.Count - 8} more." : "";
            MessageBox.Show(this, lines + more, "Import problems", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private string? StartFolder() =>
        Path.GetDirectoryName(store.Path) is { } dir && Directory.Exists(dir) ? dir : null;

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        var dialog = new OpenFileDialog
        {
            Title = "Open schedule file",
            Filter = "Schedule files (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = StartFolder(),
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true) SwitchTo(dialog.FileName, openExisting: true);
    }

    private void OnSaveAs(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        var dialog = new SaveFileDialog
        {
            Title = "Save schedule as",
            Filter = "Schedule files (*.json)|*.json",
            FileName = Path.GetFileName(store.Path),
            InitialDirectory = StartFolder(),
            AddExtension = true,
            DefaultExt = "json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) == true) SwitchTo(dialog.FileName, openExisting: false);
    }

    private void SwitchTo(string path, bool openExisting)
    {
        var next = new ScheduleStore(path);
        try
        {
            if (openExisting)
            {
                var other = ScheduleStore.TryRead(path, out var problem);
                if (other is null)
                {
                    ShowToast("That file could not be opened", problem ?? "");
                    return;
                }
                other.Settings.KeepPreferencesFrom(data.Settings);
                data.ReplaceContentsWith(other);
            }
            else next.Save(data);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast(openExisting ? "That file could not be opened" : "The schedule could not be saved there", ex.Message);
            return;
        }

        store = next;
        string note;
        try
        {
            var config = AppConfig.Load();
            config.Use(path);
            config.Save();
            note = "New changes are saved here from now on.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            note = "This only lasts until you close the app, because the choice could not be remembered.";
        }

        weekOffset = 0;
        dayDate = DateTime.Today;
        SyncMiniMonth();
        ApplyPreferences();
        ApplyWidget();
        Refresh(keepScroll: false);
        ShowToast(openExisting ? "Opened " + Path.GetFileName(path) : "Saved to " + path, note);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        MorePopup.IsOpen = false;
        var dialog = new SaveFileDialog
        {
            Title = "Export to calendar",
            Filter = "Calendar (*.ics)|*.ics",
            FileName = $"{data.Settings.TermName}.ics"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            File.WriteAllText(dialog.FileName, IcsService.Export(data, DateTime.UtcNow), new UTF8Encoding(false));
            ShowToast("Calendar exported", dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowToast("The calendar could not be saved", ex.Message);
        }
    }
}
