using System.Windows;
using Microsoft.Win32;

namespace Vox.App.Services;

/// <summary>Follows Windows' light/dark setting for windows and for the taskbar (tray icon).</summary>
internal static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static ResourceDictionary? _current;

    /// <summary>Taskbar theme changed: true = light taskbar.</summary>
    public static event Action<bool>? TaskbarThemeChanged;

    public static bool AppsUseLightTheme => ReadFlag("AppsUseLightTheme");

    public static bool TaskbarIsLight => ReadFlag("SystemUsesLightTheme");

    public static void Start()
    {
        Apply();
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                Application.Current.Dispatcher.BeginInvoke(Apply);
                TaskbarThemeChanged?.Invoke(TaskbarIsLight);
            }
        };
    }

    private static void Apply()
    {
        var name = AppsUseLightTheme ? "Light" : "Dark";
        var next = new ResourceDictionary { Source = new Uri($"pack://application:,,,/Theme/{name}.xaml") };
        var merged = Application.Current.Resources.MergedDictionaries;
        if (_current is not null)
        {
            merged.Remove(_current);
        }

        merged.Add(next);
        _current = next;
    }

    private static bool ReadFlag(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue(name) is not int value || value != 0; // missing → light (Windows default)
    }
}
