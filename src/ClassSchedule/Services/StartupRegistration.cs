using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace ClassSchedule;

/// <summary>Adds or removes this app from the current user's "run when I sign in" list.</summary>
public static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClassSchedule";
    private static bool? applied;

    public static void Apply(bool enabled)
    {
        if (applied == enabled) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
            if (key is null) return;

            var current = key.GetValue(ValueName) as string;
            if (enabled)
            {
                if (Environment.ProcessPath is not { } path) return;
                var wanted = $"\"{path}\"";
                if (current != wanted) key.SetValue(ValueName, wanted);
            }
            else if (current is not null) key.DeleteValue(ValueName, throwOnMissingValue: false);
            applied = enabled;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException)
        {
        }
    }
}
