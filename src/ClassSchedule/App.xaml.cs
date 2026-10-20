using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ClassSchedule.Core;
using ClassSchedule.Views;

namespace ClassSchedule;

public partial class App : Application
{
    private const string ShowSignal = @"Local\ClassSchedule.ShowWindow";
    private Mutex? instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        instance = new Mutex(true, @"Local\ClassSchedule.SingleInstance", out var first);
        if (!first)
        {
            try { EventWaitHandle.OpenExisting(ShowSignal).Set(); }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException) { }
            Shutdown();
            return;
        }

        var config = AppConfig.Load();
        var store = new ScheduleStore(config.ResolveDataPath(out var locationWarning));
        var data = store.Load(DateTime.Now);
        if (data.Settings.BackupsEnabled) TryBackUp(store.Path, data.Settings.BackupCount);

        var window = new MainWindow(store, data);
        MainWindow = window;
        if (data.Settings.StartHidden && data.Settings.WidgetEnabled) window.StartHidden();
        else window.Show();
        ListenForSecondLaunch(window);
        if ((store.LoadWarning ?? locationWarning) is { } warning) window.ShowToast("Schedule file", warning);
    }

    private static void TryBackUp(string path, int keep)
    {
        try { ScheduleBackups.CreateAndPrune(path, keep, DateTime.Now); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
    }

    private void ListenForSecondLaunch(MainWindow window)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignal);
        var listener = new Thread(() =>
        {
            while (signal.WaitOne()) Dispatcher.BeginInvoke(window.ShowMain);
        }) { IsBackground = true };
        listener.Start();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Class Schedule", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
