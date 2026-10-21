using Microsoft.Win32;

namespace Vox.App.Services;

/// <summary>"Запускать вместе с Windows": the per-user Run key (also written by the installer).</summary>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Vox";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string;
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(Name, $"\"{Environment.ProcessPath}\"");
        }
        else
        {
            key.DeleteValue(Name, throwOnMissingValue: false);
        }
    }
}
