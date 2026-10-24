using System.Security;
using Microsoft.Win32;

namespace Vox.App.Services;

/// <summary>
/// "Запускать вместе с Windows": the per-user Run key. Only Vox itself writes it, as the signed-in user:
/// the installer starts <c>Vox.exe --enable-autostart</c> for that (docs/adr/034).
/// </summary>
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

    /// <summary><see cref="Set"/> that reports a refused registry write instead of throwing.</summary>
    public static bool TrySet(bool enabled)
    {
        try
        {
            Set(enabled);
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or IOException)
        {
            return false;
        }
    }
}
