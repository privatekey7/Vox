using System.Net.Http;
using System.Windows;
using System.Windows.Threading;
using Vox.App.Services;
using Vox.App.Views;
using Vox.Core;
using Vox.Core.Models;
using Vox.Core.Storage;

namespace Vox.App;

/// <summary>
/// Startup: single instance → log → settings/history → microphone + hotkey + dictation → first-run
/// wizard if no model is installed → tray. Every window lives on this one UI thread.
/// </summary>
public partial class App : Application
{
    private Mutex? _singleInstance;
    private AudioCapture? _audio;
    private KeyboardHook? _hook;
    private DictationService? _dictation;
    private Sounds? _sounds;
    private VoxDatabase? _db;
    private TrayController? _tray;
    private RecordingPill? _pill;
    private SettingsWindow? _settingsWindow;

    internal static AppPaths Paths { get; } = AppPaths.Default;

    internal static HttpClient Http { get; } = CreateHttp();

    internal Settings Settings { get; private set; } = null!;

    internal DictationService Dictation => _dictation!;

    internal AudioCapture Audio => _audio!;

    internal VoxDatabase Database => _db!;

    internal Sounds SoundPlayer => _sounds!;

    internal static new App Current => (App)Application.Current;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, @"Local\Vox.Dictation", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        Log.Init(Paths.LogFile);
        DispatcherUnhandledException += OnUiException;
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error("[FATAL] Unhandled", (Exception)a.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, a) =>
        {
            Log.Error("[ERROR] Unobserved task", a.Exception);
            a.SetObserved();
        };

        try
        {
            ThemeManager.Start();
            Settings = Settings.Load(Paths.Config);
            _db = new VoxDatabase(Paths.Database);
            _sounds = new Sounds { Enabled = Settings.Sounds };
            _audio = new AudioCapture(Settings.Microphone);
            _dictation = new DictationService(_audio, new Paster(Dispatcher), _sounds, _db, Settings, Paths.Models);
            _hook = new KeyboardHook(Hotkeys.VirtualKey(Settings.Hotkey));
            _hook.Pressed += _dictation.OnPressed;
            _hook.Released += _dictation.OnReleased;
            _pill = new RecordingPill(_audio);
            _dictation.StateChanged += s => Dispatcher.BeginInvoke(() => OnDictationState(s));
            _dictation.CorrectionLearned += (w, c) => Dispatcher.BeginInvoke(() => _pill?.ShowLearned(w, c));
            Log.Info($"[INIT] Hotkey: {Hotkeys.Display(Settings.Hotkey)}; models: {Paths.Models}");

            var model = InstalledModel();
            if (model is null)
            {
                Log.Info("[INIT] No model installed — showing first-run wizard");
                var wizard = new WizardWindow();
                wizard.ShowDialog();
                if (!wizard.ModelReady)
                {
                    Log.Info("[INIT] First run cancelled, exiting");
                    Shutdown();
                    return;
                }
            }
            else
            {
                await _dictation.LoadModelAsync(model);
            }

            _tray = new TrayController(this);
        }
        catch (Exception ex)
        {
            Log.Error("[FATAL] Startup failed", ex);
            MessageBox.Show($"Vox не смог запуститься:\n\n{ex.Message}\n\nПодробности в логе:\n{Paths.LogFile}",
                "Vox", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hook?.Dispose();
        _dictation?.Dispose();
        _audio?.Dispose();
        _sounds?.Dispose();
        _db?.Dispose();
        _singleInstance?.Dispose();
        Log.Info("[EXIT] Bye");
        base.OnExit(e);
    }

    /// <summary>The saved model if it is installed, otherwise any installed one (GigaAM first).</summary>
    internal SpeechModel? InstalledModel()
    {
        var saved = ModelCatalog.Find(Settings.ActiveModel);
        if (saved is not null && ModelCatalog.IsInstalled(saved, Paths.Models))
        {
            return saved;
        }

        return ModelCatalog.All.FirstOrDefault(m => ModelCatalog.IsInstalled(m, Paths.Models));
    }

    internal void SetHotkey(string name)
    {
        Settings.Hotkey = name;
        Settings.Save();
        _hook?.SetHotkey(Hotkeys.VirtualKey(name));
        _tray?.Refresh();
        Log.Info($"[SETTINGS] Hotkey changed to {Hotkeys.Display(name)}");
    }

    internal void ShowSettings(string page = "general")
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.ShowPage(page);
        _settingsWindow.Show();
        if (_settingsWindow.WindowState == WindowState.Minimized)
        {
            _settingsWindow.WindowState = WindowState.Normal;
        }

        _settingsWindow.Activate();
    }

    private void OnDictationState(DictationState state)
    {
        _pill?.Show(state);
        _tray?.SetRecording(state == DictationState.Listening);
    }

    private void OnUiException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("[ERROR] UI", e.Exception);
        MessageBox.Show($"Что-то пошло не так:\n\n{e.Exception.Message}\n\nVox продолжит работать. Подробности в логе.",
            "Vox", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Vox/2.0");
        return http;
    }
}
