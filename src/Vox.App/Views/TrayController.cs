using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Vox.App.Services;
using Vox.Core;

namespace Vox.App.Views;

/// <summary>Tray icon (light/dark taskbar aware, red dot while recording) and its menu.</summary>
internal sealed class TrayController : IDisposable
{
    private readonly App _app;
    private readonly TaskbarIcon _icon;
    private readonly MenuItem _status;
    private bool _recording;

    public TrayController(App app)
    {
        _app = app;
        _status = new MenuItem { IsEnabled = false };
        var menu = new ContextMenu();
        menu.Items.Add(_status);
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Скопировать последнюю диктовку", "", CopyLast));
        menu.Items.Add(Item("История", "", () => _app.ShowSettings("history")));
        menu.Items.Add(Item("Настройки", "", () => _app.ShowSettings()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Выход", "", () => Application.Current.Shutdown()));

        _icon = new TaskbarIcon
        {
            ToolTipText = "Vox",
            ContextMenu = menu,
            NoLeftClickDelay = true,
        };
        _icon.TrayLeftMouseUp += (_, _) => _app.ShowSettings();
        Refresh();
        UpdateIcon();
        _icon.ForceCreate(false); // false: no Efficiency Mode — the hotkey must react instantly
        ThemeManager.TaskbarThemeChanged += _ => _app.Dispatcher.BeginInvoke(UpdateIcon);
    }

    /// <summary>Re-read texts that depend on settings (the hotkey name).</summary>
    public void Refresh()
    {
        var hotkey = Hotkeys.Display(_app.Settings.Hotkey);
        _status.Header = new StackPanel
        {
            Children =
            {
                new TextBlock { Text = "Vox работает", FontWeight = FontWeights.SemiBold },
                new TextBlock { Text = $"Зажмите {hotkey} и говорите", Opacity = 0.7, FontSize = 12 },
            },
        };
        _icon.ToolTipText = $"Vox — зажмите {hotkey} и говорите";
    }

    public void SetRecording(bool recording)
    {
        if (_recording != recording)
        {
            _recording = recording;
            UpdateIcon();
        }
    }

    public void Dispose() => _icon.Dispose();

    private void UpdateIcon()
    {
        var theme = ThemeManager.TaskbarIsLight ? "light" : "dark";
        var name = _recording ? $"tray-recording-{theme}" : $"tray-{theme}";
        _icon.IconSource = new BitmapImage(new Uri($"pack://application:,,,/Assets/{name}.ico"));
    }

    private void CopyLast()
    {
        var text = _app.Dictation.LastText;
        if (text.Length > 0)
        {
            new Paster(_app.Dispatcher).Copy(text);
        }
    }

    private static MenuItem Item(string text, string glyph, Action action)
    {
        var item = new MenuItem
        {
            Header = text,
            Icon = new TextBlock { Text = glyph, FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("Vox.FontIcons") },
        };
        item.Click += (_, _) => action();
        return item;
    }
}
