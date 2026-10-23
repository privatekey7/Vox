using System.Collections.Concurrent;
using System.Diagnostics;
using Vox.Core;
using Vox.Core.Audio;
using Vox.Core.Models;
using Vox.Core.Storage;
using Vox.Core.Text;
using Vox.Engine;

namespace Vox.App.Services;

public enum DictationState
{
    Idle,
    Listening,
    Recognizing,
    Inserted,
    NotHeard,
    ModelLoading,
    ModelNotReady,
}

/// <summary>
/// Push-to-talk: key down → record, key up → recognise, post-process, save and paste.
/// The hotkey thread only flips state; all heavy work runs on one "VoxProcessor" thread, in order.
/// </summary>
internal sealed class DictationService : IDisposable
{
    private const int MinRecordMs = 250;
    private const int ErrorSignalMinMs = 1000; // shorter presses are usually accidental
    private const float MinPeak = 100f / 32768f; // quieter than this is silence
    private static readonly TimeSpan Tail = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan TitlesTimeout = TimeSpan.FromMilliseconds(50);

    private readonly AudioCapture _audio;
    private readonly Paster _paster;
    private readonly Sounds _sounds;
    private readonly VoxDatabase _db;
    private readonly Settings _settings;
    private readonly string _modelsRoot;
    private readonly BlockingCollection<Job> _jobs = [];
    private readonly Thread _processor;
    private readonly Lock _engineLock = new();
    private SpeechRecognizer? _recognizer;
    private PostProcessor? _postProcessor;
    private IReadOnlyDictionary<string, string> _corrections;
    private long _startTimestamp;
    private volatile RecordingBuffer? _current;

    private readonly FieldWatcher _watcher = new();
    private readonly SelfLearning _learning;
    private readonly CancellationTokenSource _stopping = new(); // Vox is closing
    private readonly CursorContext _context = new();
    private readonly ScreenMemory _screenMemory;
    private readonly OwnWords _ownWords = new();
    private readonly RecordingStore _recordings;

    public DictationService(AudioCapture audio, Paster paster, Sounds sounds, VoxDatabase db, Settings settings, string modelsRoot, ScreenMemory screenMemory, RecordingStore recordings)
    {
        _screenMemory = screenMemory;
        _recordings = recordings;
        foreach (var t in db.History(OwnWords.HistoryLimit))
        {
            _ownWords.Add(t.RawText);
        }

        if (_screenMemory.Forget(_ownWords.Contains) is var forgotten and > 0)
        {
            Log.Info($"[SCREEN] Forgot {forgotten} remembered names the model itself wrote");
            _ = Task.Run(SaveScreenMemory);
        }

        _audio = audio;
        _paster = paster;
        _sounds = sounds;
        _db = db;
        _settings = settings;
        _modelsRoot = modelsRoot;
        _corrections = db.Corrections();
        _learning = new SelfLearning(db);
        _processor = new Thread(ProcessLoop) { IsBackground = true, Name = "VoxProcessor" };
        _processor.Start();
        _watcher.Enabled = settings.LearnCorrections;
        _watcher.IsTarget = _learning.IsTarget;
        _watcher.Learned += (shown, correct, userTyped) =>
        {
            var (result, wrong, target) = _learning.Learn(shown, correct, userTyped);
            var why = result switch
            {
                LearnResult.CommonWord => "the fixed word is an ordinary Russian word: not learned (add it in the dictionary by hand)",
                LearnResult.NotTyped => "the text changed without your typing (autocorrect?): nothing learned",
                LearnResult.TooShort => "too short to learn automatically",
                LearnResult.Reverted => "you reverted a dictionary replacement here: nothing changed",
                LearnResult.Pinned => "this entry went back and forth twice: it is pinned, change it in the dictionary by hand",
                _ => null,
            };
            if (why is not null)
            {
                Log.Info($"[LEARN] {char.ToUpperInvariant(why[0])}{why[1..]}");
                return;
            }

            Log.Info(result == LearnResult.TargetFixed ? "[LEARN] Fixed the target of a dictionary entry" : "[LEARN] Added to the dictionary");
            ReloadCorrections();
            CorrectionLearned?.Invoke(wrong, target, result == LearnResult.TargetFixed);
        };
    }

    public bool CanUndoLearned => _learning.CanUndo;

    /// <summary>Ctrl+S: remember the fix the user just made in the dictation (see <see cref="FieldWatcher.ConfirmFix"/>).</summary>
    public void ConfirmFix() => _watcher.ConfirmFix();

    /// <summary>Tray: forget the last learned pair; (wrong, correct) or null.</summary>
    public (string Wrong, string Correct)? UndoLastLearned()
    {
        var undone = _learning.UndoLast();
        if (undone is not null)
        {
            Log.Info("[LEARN] Last learned pair undone");
            ReloadCorrections();
        }

        return undone;
    }

    /// <summary>A dictionary entry was learned from the user's fix: (wrong, correct, an existing target was fixed).</summary>
    public event Action<string, string, bool>? CorrectionLearned;

    public bool LearnCorrections
    {
        get => _watcher.Enabled;
        set => _watcher.Enabled = value;
    }

    /// <summary>Raised from background threads; subscribers marshal to the UI themselves.</summary>
    public event Action<DictationState>? StateChanged;

    /// <summary>A dictation was pasted (text without the leading separator).</summary>
    public event Action<string>? Inserted;

    public SpeechModel? Model
    {
        get
        {
            lock (_engineLock)
            {
                return _recognizer?.Model;
            }
        }
    }

    public string LastText { get; private set; } = "";

    /// <summary>Load (or switch to) a model on a background thread. The old one keeps working until the swap.</summary>
    public Task LoadModelAsync(SpeechModel model) => Task.Run(() =>
    {
        Report(DictationState.ModelLoading);
        var sw = Stopwatch.StartNew();
        Log.Info($"[INIT] Loading model {model.Id}");
        _ = Task.Run(FuzzyTerms.Prepare); // alongside the model
        var recognizer = new SpeechRecognizer(model, _modelsRoot);
        SpeechRecognizer? old;
        lock (_engineLock)
        {
            old = _recognizer;
            _recognizer = recognizer;
            _postProcessor = new PostProcessor(model);
        }

        old?.Dispose();
        Log.Info($"[INIT] Model {model.Id} loaded in {sw.ElapsedMilliseconds} ms");
        Report(DictationState.Idle);
    });

    public void ReloadCorrections() => _corrections = _db.Corrections();

    /// <summary>Settings: forget every remembered screen name (and its file).</summary>
    public void ClearScreenMemory()
    {
        _screenMemory.Clear();
        Log.Info("[SCREEN] Remembered names cleared");
    }

    /// <summary>Hotkey down (hotkey thread).</summary>
    public void OnPressed()
    {
        if (_current is not null)
        {
            return;
        }

        _startTimestamp = Stopwatch.GetTimestamp();
        _current = _audio.BeginRecording();
        _watcher.BeforeNextDictation();
        _sounds.Start();
        Log.Info("[REC] Listening...");
        StateChanged?.Invoke(DictationState.Listening);
    }

    /// <summary>Hotkey up (hotkey thread).</summary>
    public void OnReleased(bool usedInShortcut)
    {
        if (_current is not { } buffer)
        {
            return;
        }

        _current = null;
        var elapsed = Stopwatch.GetElapsedTime(_startTimestamp);
        var releasedAt = Stopwatch.GetTimestamp();
        buffer.StopAt(releasedAt + (long)(Tail.TotalSeconds * Stopwatch.Frequency)); // even if processing lags
        _sounds.Stop();
        if (usedInShortcut)
        {
            _audio.EndRecording(buffer);
            Log.Info("[REC] Hotkey was part of a shortcut — not a dictation, ignored");
            StateChanged?.Invoke(DictationState.Idle);
            return;
        }

        // Window titles are read while the model recognises: ready by the time the text is.
        _jobs.Add(new Job(buffer, releasedAt, elapsed, _settings.ScreenHints ? Task.Run(ScreenNames) : null));
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _stopping.Cancel(); // queued dictations are dropped, nothing is pasted
        _jobs.CompleteAdding();
        _processor.Join(TimeSpan.FromSeconds(3));
        _stopping.Dispose();
        lock (_engineLock)
        {
            _recognizer?.Dispose();
            _recognizer = null;
        }
    }

    private void ProcessLoop()
    {
        try
        {
            foreach (var job in _jobs.GetConsumingEnumerable(_stopping.Token))
            {
                ProcessSafely(job);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ProcessSafely(Job job)
    {
        try
        {
            Process(job);
        }
        catch (Exception e)
        {
            // One bad dictation must not stop all later ones.
            Log.Error("Processing dictation failed", e);
            _sounds.Error();
            Report(DictationState.NotHeard);
        }
    }

    private void Process(Job job)
    {
        // The audio block holding the end of the last word is still in flight at key-up.
        var wait = Tail - Stopwatch.GetElapsedTime(job.ReleasedAt);
        if (wait > TimeSpan.Zero)
        {
            Thread.Sleep(wait);
        }

        var (samples, rate) = _audio.EndRecording(job.Buffer);
        var ms = (long)job.Elapsed.TotalMilliseconds;
        if (ms < MinRecordMs)
        {
            Log.Info($"[REC] Too short ({ms}ms), ignored");
            Report(DictationState.Idle);
            return;
        }

        if (Model is null)
        {
            // Pressed while the model is still downloading or loading: not the user's fault.
            Log.Info("[STT] No model loaded yet — dictation skipped");
            Report(DictationState.ModelNotReady);
            return;
        }

        Log.Info($"[REC] Stopped ({ms}ms), transcribing...");
        Report(DictationState.Recognizing);
        var (raw, why) = Recognize(samples, rate);
        if (raw.Length == 0)
        {
            Log.Info("[STT] No speech recognized");
            SaveRecording(samples, rate, Model, "", "", why);
            if (ms >= ErrorSignalMinMs)
            {
                _sounds.Error();
                Report(DictationState.NotHeard);
            }
            else
            {
                Report(DictationState.Idle);
            }

            return;
        }

        Log.Info($"[STT] Raw: \"{raw}\"");
        _ownWords.Add(raw);
        PostProcessor? pp;
        SpeechModel? model;
        lock (_engineLock)
        {
            pp = _postProcessor;
            model = _recognizer?.Model;
        }

        var fuzzy = new List<FuzzyTerms.Match>();
        var text = pp?.Process(raw, _corrections, fuzzy, job.Screen?.Result) ?? raw;
        foreach (var f in fuzzy)
        {
            var score = f.Score.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
            Log.Info(f.Screen is { } origin
                ? $"[SCREEN] \"{f.Spoken}\" → \"{f.Replacement}\" {score} (источник: {origin})"
                : $"{(f.English ? "[EN]" : "[FUZZY]")} \"{f.Spoken}\" → \"{f.Replacement}\" {score}");
        }

        Log.Info($"[STT] Final: \"{text}\"");
        SaveRecording(samples, rate, model, raw, text, text.Trim().Length == 0 ? "empty final" : "");
        if (text.Trim().Length == 0)
        {
            Report(DictationState.Idle);
            return;
        }

        try
        {
            _db.AddTranscription(raw, text, model?.Id ?? "", model?.Language ?? "", ms);
        }
        catch (Exception e)
        {
            Log.Error("[DB] Failed to save", e);
        }

        while (_current is not null && !_stopping.IsCancellationRequested)
        {
            Thread.Sleep(20); // the next dictation's key is held: Ctrl+V now would become Alt+Ctrl+V etc.
        }

        if (_stopping.IsCancellationRequested)
        {
            return; // closing: the UI thread waits in Dispose, a paste (ui.Invoke) would hang until the timeout
        }

        // Continue the sentence at the cursor instead of always starting a new one.
        var (before, after, source) = _context.Read();
        text = ContextJoiner.Apply(text, before, after);
        var shownBefore = before is null ? "?" : before.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        Log.Info($"[CTX] {source}: {ContextJoiner.Describe(before)} (before: \"{shownBefore}\"); pasted: \"{text}\""); // Final is before the join

        _paster.Paste(text, _settings.RestoreClipboard);
        _context.Remember(text);
        LastText = text.TrimStart();
        _watcher.AfterPaste(LastText);
        Report(DictationState.Inserted);
        Inserted?.Invoke(LastText);
    }

    /// <summary>
    /// Progress of earlier work (a previous dictation, a model switch). Not shown while the next
    /// dictation is being recorded: the pill and the tray keep showing "listening".
    /// </summary>
    private void Report(DictationState state)
    {
        if (_current is null)
        {
            StateChanged?.Invoke(state);
        }
    }

    // Names in window titles, plus remembered ones; never the text of a field, where Vox pastes its own dictations
    // (a misheard "Jamper" would come back as a name). Titles are never logged or saved, only names from them.
    private List<ScreenName> ScreenNames()
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var titles = ScreenTitles.Read(TitlesTimeout);
            // A chat's title may be Vox's own dictation too ("Jamper X Change - ChatGPT").
            var fromTitles = ScreenWords.Distinct(titles.SelectMany(t => ScreenWords.Extract(t, FuzzyTerms.IsEnglishName)))
                .Where(n => !_ownWords.Contains(n)).ToList();
            List<ScreenName> names =
            [
                .. fromTitles.Select(n => new ScreenName(n, OnScreen: true)),
                .. _screenMemory.Entries.Select(e => new ScreenName(e.Name, OnScreen: false)),
            ];
            if (_screenMemory.See(ScreenWords.Unknown(fromTitles), DateTime.UtcNow))
            {
                _ = Task.Run(SaveScreenMemory);
            }

            Log.Info($"[SCREEN] {titles.Count} windows, {names.Count(n => n.OnScreen)} names on screen, {_screenMemory.Count} remembered, {sw.ElapsedMilliseconds} ms");
            return names;
        }
        catch (Exception e)
        {
            Log.Error("[SCREEN] Reading names failed", e);
            return [];
        }
    }

    private void SaveScreenMemory()
    {
        try
        {
            _screenMemory.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("[SCREEN] Saving names failed", e);
        }
    }

    // Settings: each dictation (an empty one too, with why) for checking recognition; off the processing thread.
    private void SaveRecording(float[] samples, int rate, SpeechModel? model, string raw, string final, string note)
    {
        if (!_settings.SaveRecordings || samples.Length == 0)
        {
            return;
        }

        var time = DateTime.Now;
        _ = Task.Run(() =>
        {
            try
            {
                Log.Info($"[REC] Saved recording {_recordings.Save(samples, rate, time, model?.Id ?? "", raw, final, note)}");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Error("[REC] Saving the recording failed", e);
            }
        });
    }

    /// <summary>Recognise, logging *why* when no text comes out (also returned, for the saved recording).</summary>
    private (string Text, string Why) Recognize(float[] samples, int rate)
    {
        if (samples.Length == 0)
        {
            Log.Info("[STT] No audio from microphone — reopening it");
            _audio.Reopen();
            return ("", "no audio");
        }

        var peak = samples.Max(Math.Abs);
        if (peak < MinPeak)
        {
            Log.Info($"[STT] Too quiet (peak {peak * 32768:F0} < 100) — check the microphone");
            return ("", "too quiet");
        }

        SpeechRecognizer? recognizer;
        lock (_engineLock)
        {
            recognizer = _recognizer;
        }

        if (recognizer is null)
        {
            Log.Info("[STT] No model loaded");
            return ("", "no model");
        }

        var sw = Stopwatch.StartNew();
        var text = recognizer.Transcribe(samples, rate); // never under _engineLock: the UI reads Model

        Log.Info($"[STT] Recognised {samples.Length / (double)rate:F1}s audio (peak {peak * 32768:F0}) in {sw.Elapsed.TotalSeconds:F1}s");
        if (text.Length == 0)
        {
            Log.Info("[STT] Model returned no text");
        }

        return (text, text.Length == 0 ? "no speech" : "");
    }

    /// <param name="Screen">Names on screen at key-up (<see cref="ScreenNames"/>), null when screen hints are off.</param>
    private sealed record Job(RecordingBuffer Buffer, long ReleasedAt, TimeSpan Elapsed, Task<List<ScreenName>>? Screen);
}
