using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Vox.App.Services;
using Vox.Core;
using Vox.Core.Models;
using Vox.Core.Storage;

namespace Vox.App.Views;

/// <summary>Settings: general options, dictation history, user dictionary, help.</summary>
public partial class SettingsWindow : Window
{
    private readonly App _app = App.Current;
    private readonly DispatcherTimer _searchDelay = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer _micMeter = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _loading = true;
    private float _micPeak;

    public SettingsWindow()
    {
        InitializeComponent();
        LoadGeneral();
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            LoadHistory();
        };

        Action<float> onLevel = peak => _micPeak = Math.Max(_micPeak, peak);
        _app.Audio.Level += onLevel;
        _micMeter.Tick += (_, _) =>
        {
            MicHint.Text = _micPeak > 0.02f ? "Сейчас слышу вас хорошо" : "Скажите что-нибудь, проверю, слышно ли вас";
            _micPeak = 0;
        };
        _micMeter.Start();
        Closed += (_, _) =>
        {
            _app.Audio.Level -= onLevel;
            _micMeter.Stop();
        };

        ShowHowTo();
        VersionText.Text = $"Vox {typeof(App).Assembly.GetName().Version?.ToString(3)} · работает без интернета";
        _loading = false;
    }

    public void ShowPage(string page)
    {
        var nav = page switch
        {
            "history" => NavHistory,
            "dictionary" => NavDictionary,
            "help" => NavHelp,
            _ => NavGeneral,
        };
        nav.IsChecked = true;
        OnNav(nav, new RoutedEventArgs());
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        PageGeneral.Visibility = sender == NavGeneral ? Visibility.Visible : Visibility.Collapsed;
        PageHistory.Visibility = sender == NavHistory ? Visibility.Visible : Visibility.Collapsed;
        PageDictionary.Visibility = sender == NavDictionary ? Visibility.Visible : Visibility.Collapsed;
        PageHelp.Visibility = sender == NavHelp ? Visibility.Visible : Visibility.Collapsed;
        if (sender == NavHistory)
        {
            LoadHistory();
        }
        else if (sender == NavDictionary)
        {
            LoadDictionary();
        }
    }

    // ---- General -------------------------------------------------------------------------

    private void LoadGeneral()
    {
        foreach (var (name, display, _) in Hotkeys.All)
        {
            HotkeyBox.Items.Add(new ComboBoxItem { Content = display, Tag = name });
        }

        HotkeyBox.SelectedItem = HotkeyBox.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == _app.Settings.Hotkey);

        foreach (var model in ModelCatalog.All)
        {
            LanguageBox.Items.Add(new ComboBoxItem { Content = model.DisplayName, Tag = model });
        }

        var active = _app.Dictation.Model ?? ModelCatalog.Default;
        LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>().First(i => Equals(i.Tag, active));

        MicBox.Items.Add(new ComboBoxItem { Content = "По умолчанию", Tag = null });
        foreach (var (id, name) in AudioCapture.Devices())
        {
            MicBox.Items.Add(new ComboBoxItem { Content = name, Tag = id });
        }

        MicBox.SelectedItem = MicBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == _app.Settings.Microphone)
            ?? MicBox.Items[0];

        SoundsToggle.IsChecked = _app.Settings.Sounds;
        AutostartToggle.IsChecked = Autostart.IsEnabled;
        ClipboardToggle.IsChecked = _app.Settings.RestoreClipboard;
        RecordingsToggle.IsChecked = _app.Settings.SaveRecordings;
        LearnToggle.IsChecked = _app.Settings.LearnCorrections;
        ScreenToggle.IsChecked = _app.Settings.ScreenHints;
        ScreenTextToggle.IsChecked = _app.Settings.ScreenText;
    }

    private void OnHotkeyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && HotkeyBox.SelectedItem is ComboBoxItem { Tag: string name })
        {
            _app.SetHotkey(name);
            ShowHowTo(); // the help page names the hotkey
        }
    }

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedItem is not ComboBoxItem { Tag: SpeechModel model } || model == _app.Dictation.Model)
        {
            return;
        }

        LanguageBox.IsEnabled = false;
        try
        {
            if (!ModelCatalog.IsInstalled(model, App.Paths.Models))
            {
                LanguageProgress.Visibility = Visibility.Visible;
                LanguageHint.Text = "Скачиваю модель…";
                var progress = new Progress<DownloadProgress>(p =>
                {
                    LanguageProgress.IsIndeterminate = p.Total <= 0 || p.Stage != DownloadStage.Downloading;
                    if (p.Total > 0)
                    {
                        LanguageProgress.Value = (double)p.Done / p.Total;
                        LanguageHint.Text = p.Stage == DownloadStage.Downloading
                            ? $"Скачиваю модель… {p.Done / 1048576} из {p.Total / 1048576} МБ"
                            : "Распаковываю…";
                    }
                });
                await new ModelDownloader(App.Http, App.Paths.Models).InstallAsync(model, progress, CancellationToken.None);
            }

            LanguageHint.Text = "Переключаю…";
            await _app.Dictation.LoadModelAsync(model);
            _app.Settings.ActiveModel = model.Id;
            _app.Settings.Language = model.Language;
            _app.Settings.Save();
            LanguageHint.Text = "Готово";
        }
        catch (Exception ex)
        {
            Log.Error("[SETTINGS] Model switch failed", ex);
            LanguageHint.Text = "Не получилось скачать. Проверьте интернет и попробуйте ещё раз.";
            _loading = true;
            var current = _app.Dictation.Model ?? ModelCatalog.Default;
            LanguageBox.SelectedItem = LanguageBox.Items.Cast<ComboBoxItem>().First(i => Equals(i.Tag, current));
            _loading = false;
        }
        finally
        {
            LanguageProgress.Visibility = Visibility.Collapsed;
            LanguageBox.IsEnabled = true;
        }
    }

    private void OnMicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || MicBox.SelectedItem is not ComboBoxItem item)
        {
            return;
        }

        var id = item.Tag as string;
        _app.Settings.Microphone = id;
        _app.Settings.Save();
        _app.Audio.UseDevice(id);
    }

    private void OnToggle(object sender, RoutedEventArgs e)
    {
        if (sender == SoundsToggle)
        {
            _app.Settings.Sounds = SoundsToggle.IsChecked == true;
            _app.SoundPlayer.Enabled = _app.Settings.Sounds;
        }
        else if (sender == ClipboardToggle)
        {
            _app.Settings.RestoreClipboard = ClipboardToggle.IsChecked == true;
        }
        else if (sender == AutostartToggle)
        {
            Autostart.Set(AutostartToggle.IsChecked == true);
        }
        else if (sender == LearnToggle)
        {
            _app.Settings.LearnCorrections = LearnToggle.IsChecked == true;
            _app.Dictation.LearnCorrections = _app.Settings.LearnCorrections;
        }
        else if (sender == ScreenToggle)
        {
            _app.Settings.ScreenHints = ScreenToggle.IsChecked == true;
        }
        else if (sender == ScreenTextToggle)
        {
            _app.Settings.ScreenText = ScreenTextToggle.IsChecked == true;
        }
        else if (sender == RecordingsToggle)
        {
            _app.Settings.SaveRecordings = RecordingsToggle.IsChecked == true;
        }

        _app.Settings.Save();
    }

    private void ShowHowTo() => HowTo.Text =
        "1. Поставьте курсор туда, где нужен текст, в любой программе.\n" +
        $"2. Зажмите {Hotkeys.Display(_app.Settings.Hotkey)} и говорите.\n" +
        "3. Отпустите, и текст появится у курсора.";

    // ---- History -------------------------------------------------------------------------

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _searchDelay.Stop();
        _searchDelay.Start();
    }

    private void LoadHistory()
    {
        HistoryList.Children.Clear();
        var query = SearchBox.Text.Trim();
        var items = query.Length == 0 ? _app.Database.History() : _app.Database.Search(query);
        if (items.Count == 0)
        {
            HistoryList.Children.Add(new TextBlock
            {
                Text = query.Length == 0 ? "Здесь появятся ваши диктовки." : "Ничего не нашлось.",
                Style = (Style)FindResource("Vox.Subtitle"),
                Margin = new Thickness(4, 8, 0, 0),
            });
            return;
        }

        foreach (var day in items.GroupBy(t => t.CreatedAt.ToLocalTime().Date))
        {
            HistoryList.Children.Add(new TextBlock
            {
                Text = DayLabel(day.Key),
                Style = (Style)FindResource("Vox.Question"),
                FontSize = 14,
                Margin = new Thickness(4, 12, 0, 8),
            });
            var card = new StackPanel();
            var first = true;
            foreach (var t in day)
            {
                if (!first)
                {
                    card.Children.Add(Divider());
                }

                first = false;
                card.Children.Add(HistoryRow(t));
            }

            HistoryList.Children.Add(new Border { Style = (Style)FindResource("Vox.Card"), Padding = new Thickness(0), Child = card });
        }
    }

    private Grid HistoryRow(Transcription t)
    {
        var row = new Grid { Margin = new Thickness(16, 12, 16, 12) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = t.CreatedAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
            Style = (Style)FindResource("Vox.Subtitle"),
        });
        var text = new TextBlock { Text = t.ProcessedText.Trim(), Style = (Style)FindResource("Vox.Body") };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var copy = new Button { Content = "Копировать", Style = (Style)FindResource("Vox.LinkButton"), Margin = new Thickness(16, 0, 0, 0) };
        copy.Click += (_, _) =>
        {
            new Paster(Dispatcher).Copy(t.ProcessedText.Trim());
            copy.Content = "Скопировано";
        };
        Grid.SetColumn(copy, 2);
        row.Children.Add(copy);
        return row;
    }

    private static string DayLabel(DateTime day)
    {
        var today = DateTime.Today;
        if (day == today)
        {
            return "Сегодня";
        }

        return day == today.AddDays(-1) ? "Вчера" : day.ToString("d MMMM yyyy", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private void OnClearHistory(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Удалить всю историю диктовок? Это нельзя отменить.", "Vox",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            _app.Database.ClearHistory();
            LoadHistory();
        }
    }

    private void OnOpenRecordings(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(App.Paths.Recordings);
        Process.Start(new ProcessStartInfo { FileName = App.Paths.Recordings, UseShellExecute = true });
    }

    // ---- Dictionary ----------------------------------------------------------------------

    private void OnClearScreenMemory(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Забыть все названия, которые Vox видел на экране?", "Vox",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _app.Dictation.ClearScreenMemory();
            LoadScreenMemory();
        }
    }

    private void OnShowScreenMemory(object sender, RoutedEventArgs e)
    {
        var show = ScreenMemoryList.Visibility != Visibility.Visible;
        ScreenMemoryList.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        ScreenMemoryButton.Content = show ? "Скрыть список" : "Что Vox запомнил с экрана";
        LoadScreenMemory();
    }

    // The names Vox remembered from the screen, the latest first; any of them can be forgotten.
    private void LoadScreenMemory()
    {
        ScreenMemoryList.Children.Clear();
        if (ScreenMemoryList.Visibility != Visibility.Visible)
        {
            return;
        }

        var entries = _app.Dictation.ScreenMemoryEntries;
        if (entries.Count == 0)
        {
            ScreenMemoryList.Children.Add(new TextBlock { Text = "Пока ничего не запомнено.", Style = (Style)FindResource("Vox.Caption") });
            return;
        }

        foreach (var entry in entries)
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock
            {
                Text = $"{entry.Name}   ·   {entry.Count} раз, последний {entry.Last.ToLocalTime():dd.MM.yyyy}",
                Style = (Style)FindResource("Vox.Caption"),
            });
            var forget = new Button { Content = "Забыть", Style = (Style)FindResource("Vox.LinkButton") };
            forget.Click += (_, _) =>
            {
                _app.Dictation.ForgetScreenName(entry.Name);
                LoadScreenMemory();
            };
            Grid.SetColumn(forget, 1);
            row.Children.Add(forget);
            ScreenMemoryList.Children.Add(row);
        }
    }

    private void LoadDictionary()
    {
        DictionaryList.Children.Clear();
        var entries = _app.Database.DictionaryEntries();
        if (entries.Count == 0)
        {
            DictionaryList.Children.Add(new TextBlock
            {
                Text = "Пока пусто. Например: «кубернетис» → «Kubernetes».",
                Style = (Style)FindResource("Vox.Subtitle"),
                Margin = new Thickness(4, 4, 0, 0),
            });
            return;
        }

        var card = new StackPanel();
        foreach (var entry in entries)
        {
            if (card.Children.Count > 0)
            {
                card.Children.Add(Divider());
            }

            var row = new Grid { Margin = new Thickness(16, 10, 16, 10) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = $"{entry.Wrong}  →  {entry.Correct}", Style = (Style)FindResource("Vox.Body") });
            var delete = new Button { Content = "Удалить", Style = (Style)FindResource("Vox.LinkButton") };
            delete.Click += (_, _) =>
            {
                _app.Database.DeleteDictionaryEntry(entry.Id);
                _app.Dictation.ReloadCorrections();
                LoadDictionary();
            };
            Grid.SetColumn(delete, 1);
            row.Children.Add(delete);
            card.Children.Add(row);
        }

        DictionaryList.Children.Add(new Border { Style = (Style)FindResource("Vox.Card"), Padding = new Thickness(0), Child = card });
    }

    private void OnAddWord(object sender, RoutedEventArgs e)
    {
        var wrong = WrongBox.Text.Trim().ToLowerInvariant();
        var correct = CorrectBox.Text.Trim();
        if (wrong.Length == 0 || correct.Length == 0 || wrong.Contains(' ', StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Впишите одно слово так, как его пишет Vox, и как оно должно выглядеть.", "Vox");
            return;
        }

        _app.Database.AddDictionaryEntry(wrong, correct, "", "manual");
        _app.Dictation.ReloadCorrections();
        WrongBox.Clear();
        CorrectBox.Clear();
        LoadDictionary();
    }

    // ---- Help ----------------------------------------------------------------------------

    private void OnOpenLogs(object sender, RoutedEventArgs e) => OpenFolder(App.Paths.Logs);

    private void OnOpenModels(object sender, RoutedEventArgs e) => OpenFolder(App.Paths.Models);

    private static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    // A dynamic reference, so the line follows a theme switch (FindResource froze the old brush).
    private static Border Divider()
    {
        var line = new Border { Height = 1 };
        line.SetResourceReference(Border.BackgroundProperty, "Vox.CardStroke");
        return line;
    }
}
