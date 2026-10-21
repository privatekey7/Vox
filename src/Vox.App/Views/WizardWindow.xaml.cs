using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Vox.App.Services;
using Vox.Core;
using Vox.Core.Models;

namespace Vox.App.Views;

/// <summary>First run: choose the language (= model), download it, try the hotkey once.</summary>
public partial class WizardWindow : Window
{
    private readonly App _app = App.Current;
    private CancellationTokenSource? _download;
    private int _step = 1;

    public WizardWindow()
    {
        InitializeComponent();
        foreach (var (name, display, _) in Hotkeys.All)
        {
            KeyPicker.Items.Add(new ComboBoxItem { Content = display, Tag = name });
        }

        ShowStep(1);
        Closing += (_, _) => _download?.Cancel();
        Action<string> onInserted = _ => Dispatcher.BeginInvoke(() =>
        {
            if (_step == 3)
            {
                TrySuccess.Visibility = Visibility.Visible; // a real dictation arrived
            }
        });
        _app.Dictation.Inserted += onInserted;
        Closed += (_, _) =>
        {
            _app.Dictation.Inserted -= onInserted;
            _download?.Dispose();
        };
    }

    /// <summary>A model is installed and loaded — closing the wizard after this is not "cancel".</summary>
    public bool ModelReady { get; private set; }

    private SpeechModel ChosenModel => ChoiceRussian.IsChecked == true ? ModelCatalog.GigaAm : ModelCatalog.Parakeet;

    private void ShowStep(int step)
    {
        _step = step;
        StepLanguage.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        StepDownload.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        StepTry.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        CancelButton.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        NextButton.Visibility = step == 2 ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = step == 3 ? "Готово" : "Продолжить";

        Steps.Children.Clear();
        for (var i = 1; i <= 3; i++)
        {
            Steps.Children.Add(new Rectangle
            {
                Width = 22,
                Height = 4,
                RadiusX = 2,
                RadiusY = 2,
                Margin = new Thickness(0, 0, 6, 0),
                Fill = (System.Windows.Media.Brush)FindResource(i <= step ? "Vox.Accent" : "Vox.ProgressTrack"),
            });
        }

        if (step == 3)
        {
            KeyCap.Text = Hotkeys.Display(_app.Settings.Hotkey);
            TryBox.Focus();
        }
    }

    private async void OnNext(object sender, RoutedEventArgs e)
    {
        if (_step == 1)
        {
            await DownloadAndLoad(ChosenModel);
        }
        else if (_step == 3)
        {
            Close();
        }
    }

    private async Task DownloadAndLoad(SpeechModel model)
    {
        ShowStep(2);
        DownloadTitle.Text = model == ModelCatalog.GigaAm ? "Скачиваю русскую модель" : "Скачиваю модель";
        _download = new CancellationTokenSource();
        var started = Stopwatch.StartNew();
        var progress = new Progress<DownloadProgress>(p => ShowProgress(p, started.Elapsed));
        try
        {
            Log.Info($"[DL] {model.Id}: {model.Url}");
            await new ModelDownloader(App.Http, App.Paths.Models).InstallAsync(model, progress, _download.Token);
            DownloadEta.Text = "Запускаю распознавание…";
            await _app.Dictation.LoadModelAsync(model);
            _app.Settings.ActiveModel = model.Id;
            _app.Settings.Language = model.Language;
            _app.Settings.Save();
            ModelReady = true;
            Log.Info($"[DL] {model.Id} installed");
            ShowStep(3);
        }
        catch (OperationCanceledException)
        {
            ShowStep(1);
        }
        catch (Exception ex)
        {
            Log.Error("[DL] Failed", ex);
            MessageBox.Show(this,
                "Не получилось скачать модель. Проверьте интернет и попробуйте ещё раз.\n\n" +
                "Если загрузка с GitHub не открывается, может помочь VPN.\n\n" + ex.Message,
                "Vox", MessageBoxButton.OK, MessageBoxImage.Warning);
            ShowStep(1);
        }
    }

    private void ShowProgress(DownloadProgress p, TimeSpan elapsed)
    {
        if (p.Stage == DownloadStage.Extracting)
        {
            DownloadBar.IsIndeterminate = true;
            DownloadEta.Text = "Распаковываю…";
            return;
        }

        DownloadBar.IsIndeterminate = p.Total <= 0;
        if (p.Total <= 0)
        {
            DownloadBytes.Text = $"{p.Done / 1048576} МБ";
            return;
        }

        DownloadBar.Value = (double)p.Done / p.Total;
        DownloadBytes.Text = $"{p.Done / 1048576} из {p.Total / 1048576} МБ";
        if (p.Done > 0 && elapsed.TotalSeconds > 2)
        {
            var left = TimeSpan.FromSeconds(elapsed.TotalSeconds * (p.Total - p.Done) / p.Done);
            DownloadEta.Text = left.TotalMinutes >= 1
                ? $"Осталось около {Math.Ceiling(left.TotalMinutes)} мин"
                : $"Осталось около {Math.Max(5, (int)(Math.Ceiling(left.TotalSeconds / 5) * 5))} секунд";
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => _download?.Cancel();

    private void OnChangeKey(object sender, RoutedEventArgs e)
    {
        KeyPicker.Visibility = Visibility.Visible;
        KeyPicker.SelectedItem = KeyPicker.Items.Cast<ComboBoxItem>().First(i => (string)i.Tag == _app.Settings.Hotkey);
        KeyPicker.IsDropDownOpen = true;
    }

    private void OnKeyPicked(object sender, SelectionChangedEventArgs e)
    {
        if (KeyPicker.SelectedItem is ComboBoxItem { Tag: string name } && name != _app.Settings.Hotkey)
        {
            _app.SetHotkey(name);
            KeyCap.Text = Hotkeys.Display(name);
            TryBox.Focus();
        }
    }
}
