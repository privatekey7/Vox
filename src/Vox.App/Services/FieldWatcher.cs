using System.Collections.Concurrent;
using System.Windows.Automation;
using Vox.Core.Text;

namespace Vox.App.Services;

/// <summary>
/// Self-learning dictionary: after a dictation is pasted, remembers the focused field's text
/// (read-only: UI Automation, or WM_GETTEXT for apps without it such as Notepad++ — the caret and
/// clipboard are never touched). When the field later
/// shows a single-word fix inside the dictation, <see cref="CorrectionDetector"/> turns it into a
/// dictionary entry.
/// </summary>
/// <remarks>
/// Password fields are never read. Field text stays in memory and is never logged. All UI Automation
/// calls run on one background thread, so a slow or hung target app cannot block dictation.
/// A field is compared only once its text has been stable for <see cref="StableFor"/> (or when the
/// next dictation starts), so a half-typed fix ("Kubern…") is never learned.
/// </remarks>
internal sealed class FieldWatcher : IDisposable
{
    private const int MaxChars = 20_000;
    private const uint WindowTextTimeoutMs = 150;
    private static readonly TimeSpan StableFor = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ConfirmWithin = TimeSpan.FromMinutes(2); // Ctrl+S after a rejected fix
    private static readonly TimeSpan SampleAfter = TimeSpan.FromMilliseconds(100); // the hook sees a key before the app

    private readonly BlockingCollection<Action> _work = new(boundedCapacity: 16);
    private readonly Thread _thread;
    private readonly Timer _poll;
    private Pending? _pending;
    private (Pending Field, DateTime At)? _rejected; // the last edit that was not learned
    private int _sampling; // one field sample at a time: the queue also carries pastes and Ctrl+S

    public FieldWatcher()
    {
        _thread = new Thread(() =>
        {
            foreach (var action in _work.GetConsumingEnumerable())
            {
                try
                {
                    action();
                }
                catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
                {
                    Log.Info($"[LEARN] Field is gone or unreadable ({e.GetType().Name})");
                    _pending = null; // the field went away — nothing to learn from it
                }
                catch (Exception e)
                {
                    Log.Error("[LEARN] Field read failed", e);
                    _pending = null;
                }
            }
        })
        { IsBackground = true, Name = "VoxFieldWatcher" };
        _thread.Start();
        _poll = new Timer(_ => Enqueue(() => Check(requireStable: true)), null, StableFor, StableFor);
    }

    /// <summary>Is this word a dictionary target (a fix of it is a typo in the target)? See <see cref="CorrectionDetector.Detect"/>.</summary>
    public Func<string, bool>? IsTarget { get; set; }

    /// <summary>The user fixed a dictated word: (wrong, correct).</summary>
    /// <remarks>The third value: the user typed in that window since the paste (not autocorrect).</remarks>
    public event Action<string, string, bool>? Learned;

    public bool Enabled { get; set; } = true;

    /// <summary>Call right after a dictation was pasted.</summary>
    public void AfterPaste(string inserted) => Enqueue(() =>
    {
        Thread.Sleep(150); // let the target app apply the paste (more retries below)
        _pending = null;
        _rejected = null;
        var element = AutomationElement.FocusedElement; // null: no UIA element, WM_GETTEXT may still work
        var app = element is not null ? AppName(element) : Native.ForegroundApp().Name;
        var uiaText = element is not null ? ReadText(element) : null;
        var hwnd = uiaText is null ? Native.FocusedControl() : 0;
        if (element is null && hwnd == 0)
        {
            Log.Info("[LEARN] No focused field: nothing to watch");
            return;
        }

        var windowClass = hwnd != 0 ? Native.ClassName(hwnd) : "?";
        var isPassword = (element is not null && (bool)element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty))
            || (hwnd != 0 && windowClass.Contains("Edit", StringComparison.OrdinalIgnoreCase)
                && (Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE) & Native.ES_PASSWORD) != 0);
        var length = hwnd != 0 ? Native.WindowTextLength(hwnd, WindowTextTimeoutMs) : -1;
        var source = FieldReadPolicy.Choose(uiaText is not null, isPassword, length, hwnd != 0 && IsTextControl(windowClass));

        Func<string?> read = source == FieldSource.Uia
            ? () => ReadText(element!)
            : () => ReadWindowText(hwnd);

        // Only the reason is logged, never the field's text. The target app applies the paste
        // asynchronously (Notepad++ took ~0.8 s in the owner's log): retry for about a second.
        var text = source switch
        {
            FieldSource.Uia => uiaText,
            FieldSource.WindowText => read(),
            _ => null,
        };
        for (var attempt = 0;
             attempt < 5 && source != FieldSource.None && (text is null || !text.Contains(inserted.Trim(), StringComparison.Ordinal));
             attempt++)
        {
            Thread.Sleep(200);
            text = read();
        }
        if (isPassword)
        {
            Log.Info($"[LEARN] Password field in {app}: not read");
        }
        else if (text is null && source == FieldSource.WindowText)
        {
            Log.Info($"[LEARN] {app} (window class {windowClass}) did not answer in time: not watched");
        }
        else if (text is null)
        {
            Log.Info($"[LEARN] {app} (window class {windowClass}) does not expose the field's text: cannot learn here");
        }
        else if (!text.Contains(inserted.Trim(), StringComparison.Ordinal))
        {
            Log.Info($"[LEARN] The field in {app} does not show the dictation: not watched");
        }
        else
        {
            Log.Info($"[LEARN] Watching the field in {app} via {(source == FieldSource.Uia ? "uia" : "wm_gettext")}");
            _pending = new Pending(read, text, inserted, DateTime.UtcNow, text, DateTime.UtcNow, Native.GetForegroundWindow(), Environment.TickCount64,
                new TypingTrace(text, inserted));
        }
    });

    /// <summary>Call when the next dictation starts: the user is done editing the previous one.</summary>
    public void BeforeNextDictation() => Enqueue(() => Check(requireStable: false));

    /// <summary>
    /// Call on Ctrl+S: the user confirms the fix they just made in the dictation (owner, 23.10). It is
    /// learned even when the words do not look alike; the other guards stay. A fix rejected within
    /// <see cref="ConfirmWithin"/> counts too, but only in the same window.
    /// </summary>
    public void ConfirmFix()
    {
        var window = Native.GetForegroundWindow();
        Enqueue(() => Confirm(window));
    }

    /// <summary>
    /// Call when the user pressed an editing key in <paramref name="window"/>: a look at the field shortly after shows
    /// whether a fix is typed by hand (<see cref="TypingTrace"/>).
    /// </summary>
    public void Edited(nint window)
    {
        if (_pending?.Window != window || Interlocked.Exchange(ref _sampling, 1) == 1)
        {
            return;
        }

        _ = Task.Delay(SampleAfter).ContinueWith(_ =>
        {
            if (!Enqueue(Sample))
            {
                Volatile.Write(ref _sampling, 0);
            }
        }, TaskScheduler.Default);
    }

    public void Dispose()
    {
        _poll.Dispose();
        _work.CompleteAdding();
    }

    private bool Enqueue(Action action) =>
        Enabled && !_work.IsAddingCompleted
        && _work.TryAdd(action); // a full queue means the watcher is stuck on a hung app — skip

    private void Sample()
    {
        Volatile.Write(ref _sampling, 0);
        if (_pending is { } p && p.Read() is { } now)
        {
            p.Typing.Add(now);
        }
    }

    private void Check(bool requireStable)
    {
        if (_pending is not { } p)
        {
            return;
        }

        if (DateTime.UtcNow - p.PastedAt > GiveUpAfter)
        {
            _pending = null;
            return;
        }

        var now = p.Read();
        if (now is null)
        {
            _pending = null;
            return;
        }

        if (now == p.Before)
        {
            return; // nothing edited yet
        }

        if (requireStable && (now != p.LastSeen || DateTime.UtcNow - p.LastChange < StableFor))
        {
            _pending = p with { LastSeen = now, LastChange = now != p.LastSeen ? DateTime.UtcNow : p.LastChange };
            return; // still being edited
        }

        _pending = null;
        if (!Compare(p, now, confirmed: false))
        {
            _rejected = (p, DateTime.UtcNow); // Ctrl+S may still confirm it
        }
    }

    // Ctrl+S: compare at once (no waiting for StableFor) and let words that do not look alike through.
    private void Confirm(nint window)
    {
        var p = _pending ?? (_rejected is var (r, at) && DateTime.UtcNow - at < ConfirmWithin ? r : null);
        if (p is null || p.Window != window)
        {
            return; // Ctrl+S elsewhere, or no dictation edited lately: just a save
        }

        var now = p.Read();
        if (now == p.Before)
        {
            return; // nothing edited yet
        }

        _pending = null;
        _rejected = null;
        if (now is not null)
        {
            Compare(p, now, confirmed: true);
        }
    }

    private bool Compare(Pending p, string now, bool confirmed)
    {
        var by = confirmed ? "Ctrl+S: " : "";
        if (!confirmed && CorrectionDetector.Detect(p.Before, now, p.Inserted, IsTarget) is var (_, fixedTo) && !p.Typing.ShowsTyping(now, fixedTo))
        {
            Log.Info("[LEARN] The word changed in one step, not typed by hand: nothing learned (Ctrl+S remembers it)");
            return false;
        }

        if (CorrectionDetector.Detect(p.Before, now, p.Inserted, IsTarget, confirmed) is var (wrong, correct))
        {
            Log.Info($"[LEARN] {by}Correction: \"{wrong}\" → \"{correct}\"");
            Learned?.Invoke(wrong, correct, KeyboardHook.UserTypedIn(p.Window, p.PastedTick));
            return true;
        }

        var diff = CorrectionDetector.Describe(p.Before, now, p.Inserted, IsTarget, confirmed);
        Log.Info(diff.StartsWith("no change", StringComparison.Ordinal)
            ? $"[LEARN] {by}The field changed outside the dictation: nothing learned"
            : $"[LEARN] {by}The field was edited, nothing learned: {diff}");
        return false;
    }

    private static string AppName(AutomationElement element)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(element.Current.ProcessId);
            return process.ProcessName;
        }
        catch (ArgumentException)
        {
            return "?";
        }
    }

    private static bool IsTextControl(string windowClass) =>
        windowClass.Equals("Scintilla", StringComparison.OrdinalIgnoreCase)
        || windowClass.Contains("Edit", StringComparison.OrdinalIgnoreCase); // Edit, RichEdit20W, RICHEDIT50W

    private static string? ReadWindowText(nint hwnd)
    {
        var length = Native.WindowTextLength(hwnd, WindowTextTimeoutMs);
        return FieldReadPolicy.Choose(false, false, length) == FieldSource.WindowText
            ? Native.WindowText(hwnd, length, WindowTextTimeoutMs)
            : null; // the control went away, hung or grew past the limit
    }

    private static string? ReadText(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var text))
        {
            return ((TextPattern)text).DocumentRange.GetText(MaxChars);
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
        {
            var s = ((ValuePattern)value).Current.Value;
            return s.Length > MaxChars ? s[..MaxChars] : s;
        }

        return null; // this app does not expose its text
    }

    private sealed record Pending(
        Func<string?> Read, string Before, string Inserted, DateTime PastedAt, string LastSeen, DateTime LastChange,
        nint Window, long PastedTick, TypingTrace Typing);
}
