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
    private EventWaitHandle? _quitRequest;
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

    /// <summary>Named event the installer sets to close Vox gracefully (keep in sync with vox.iss).</summary>
    private const string QuitEventName = @"Local\Vox.Quit";

    /// <summary>
    /// vox.iss runs <c>Vox.exe --enable-autostart</c> as the user who started setup (keep in sync): an
    /// admin-mode installer may run as another account, whose Run key is not this user's (docs/adr/034).
    /// </summary>
    private const string EnableAutostartArg = "--enable-autostart";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains(EnableAutostartArg))
        {
            // Before the single-instance mutex and the log: this run only writes the Run value and exits.
            Shutdown(Autostart.TrySet(true) ? 0 : 1);
            return;
        }

        _singleInstance = new Mutex(true, @"Local\Vox.Dictation", out var first);
        if (!first)
        {
            Shutdown();
            return;
        }

        Log.Init(Paths.LogFile);

        // The installer asks a running Vox to quit (vox.iss), so an update is not logged as a crash.
        _quitRequest = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
        ThreadPool.RegisterWaitForSingleObject(_quitRequest, (_, _) => Dispatcher.BeginInvoke(() =>
        {
            Log.Info("[EXIT] Asked to quit (installer)");
            Shutdown();
        }), null, Timeout.Infinite, executeOnlyOnce: true);
        ModelDownloader.RemoveLeftovers(Paths.Models); // an install interrupted by closing Vox
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
            Log.Texts = Settings.LogTexts;
            _db = new VoxDatabase(Paths.Database);
            _sounds = new Sounds { Enabled = Settings.Sounds };
            _audio = new AudioCapture(Settings.Microphone);
            _dictation = new DictationService(_audio, new Paster(Dispatcher), _sounds, _db, Settings, Paths.Models, Vox.Core.Text.ScreenMemory.Load(Paths.ScreenNames), new Vox.Core.Audio.RecordingStore(Paths.Recordings));
            _hook = new KeyboardHook(Hotkeys.VirtualKey(Settings.Hotkey));
            _hook.Pressed += _dictation.OnPressed;
            _hook.Released += _dictation.OnReleased;
            _hook.SavePressed += _dictation.ConfirmFix;
            _hook.Edited += _dictation.Edited;
            _pill = new RecordingPill(_audio);
            _dictation.StateChanged += s => Dispatcher.BeginInvoke(() => OnDictationState(s));
            _dictation.CorrectionLearned += (w, c, fixedTarget) => Dispatcher.BeginInvoke(() => _pill?.ShowLearned(w, c, fixedTarget));
            Log.Info($"[INIT] Hotkey: {Hotkeys.Display(Settings.Hotkey)}; models: {Paths.Models}");

            var model = InstalledModel();
            if (model is not null && !await TryLoadModelAsync(model))
            {
                model = null;
            }

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

            _tray = new TrayController(this);
            _tray.ShowStarted(); // the model is loaded: dictation works from now on
        }
        catch (Exception ex)
        {
            Log.Error("[FATAL] Startup failed", ex);
            MessageBox.Show($"Vox не смог запуститься:\n\n{ex.Message}\n\nПодробности в логе:\n{Paths.LogFile}{Log.TextsWarning}",
                "Vox", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        StopServices();
        Log.Info("[EXIT] Bye");
        _quitRequest?.Dispose();
        _singleInstance?.Dispose(); // last: the installer waits for this mutex to disappear
        base.OnExit(e);
    }

    /// <summary>Stops everything that holds or writes Vox's files (the model, the database). Safe to call twice.</summary>
    private void StopServices()
    {
        _tray?.Dispose();
        _tray = null;
        _hook?.Dispose();
        _hook = null;
        _dictation?.Dispose();
        _dictation = null;
        _audio?.Dispose();
        _audio = null;
        _sounds?.Dispose();
        _sounds = null;
        _db?.Dispose();
        _db = null;
    }

    /// <summary>
    /// Settings → «Удалить мои данные»: this Windows user's history, dictionary, settings, logs, recordings,
    /// crash dumps, own models and autostart, then Vox quits. The uninstaller cannot do it reliably (docs/adr/034).
    /// </summary>
    internal void DeletePersonalDataAndQuit()
    {
        Log.Info("[DATA] Deleting the user's data on request");
        _settingsWindow?.Close();
        StopServices();
        Log.Stop(); // nothing may write into the folders being deleted
        var autostartOff = Autostart.TrySet(false);
        var left = PersonalData.Delete(Paths);
        if (left.Count == 0 && autostartOff)
        {
            MessageBox.Show("Ваши данные Vox удалены. Vox закроется.", "Vox", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            var lines = left.Select(f => "• " + f).ToList();
            if (!autostartOff)
            {
                lines.Add("• автозапуск Vox (выключите его в «Параметры» → «Приложения» → «Автозагрузка»)");
            }

            MessageBox.Show($"Удалено не всё. Осталось:\n\n{string.Join("\n", lines)}\n\nЗакройте программы, которые могут держать эти файлы, и удалите папки вручную.",
                "Vox", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        Shutdown();
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

    /// <summary>
    /// Load the installed model. A model that fails to load (damaged files) is removed, so the
    /// first-run wizard can download it again instead of Vox failing on every start.
    /// </summary>
    private async Task<bool> TryLoadModelAsync(SpeechModel model)
    {
        try
        {
            await _dictation!.LoadModelAsync(model);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[INIT] Model {model.Id} failed to load — removing it", ex);
            RemoveModel(model);
            return false;
        }
    }

    /// <summary>Delete a model that failed to load or failed its check (D034), so the next install downloads it again.</summary>
    internal static void RemoveModel(SpeechModel model)
    {
        try
        {
            new ModelDownloader(Http, Paths.Models).Uninstall(model);
        }
        catch (IOException e)
        {
            Log.Error("[MODEL] Could not remove the model", e);
        }
        catch (UnauthorizedAccessException e)
        {
            Log.Error("[MODEL] Could not remove the model", e); // another user's files: InstallAsync explains it
        }
    }

    internal void SetHotkey(string name)
    {
        Settings.Hotkey = name;
        Settings.Save();
        _hook?.SetHotkey(Hotkeys.VirtualKey(name));
        _tray?.Refresh();
        Log.Info($"[SETTINGS] Hotkey changed to {Hotkeys.Display(name)}");
    }

    internal bool CanUndoLearned => _dictation?.CanUndoLearned == true;

    /// <summary>Tray: forget the last pair self-learning added.</summary>
    internal void UndoLastLearned()
    {
        if (_dictation?.UndoLastLearned() is var (wrong, correct))
        {
            _pill?.ShowUndone(wrong, correct);
        }
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
        MessageBox.Show($"Что-то пошло не так:\n\n{e.Exception.Message}\n\nVox продолжит работать. Подробности в логе.{Log.TextsWarning}",
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
