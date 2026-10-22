using System.Collections.Concurrent;
using System.Windows.Automation;
using Vox.Core.Text;

namespace Vox.App.Services;

/// <summary>
/// Self-learning dictionary: after a dictation is pasted, remembers the focused field's text
/// (read-only, via UI Automation — the caret and clipboard are never touched). When the field later
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
    private static readonly TimeSpan StableFor = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(10);

    private readonly BlockingCollection<Action> _work = new(boundedCapacity: 16);
    private readonly Thread _thread;
    private readonly Timer _poll;
    private Pending? _pending;

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

    /// <summary>The user fixed a dictated word: (wrong, correct).</summary>
    public event Action<string, string>? Learned;

    public bool Enabled { get; set; } = true;

    /// <summary>Call right after a dictation was pasted.</summary>
    public void AfterPaste(string inserted) => Enqueue(() =>
    {
        Thread.Sleep(300); // let the target app apply the paste
        _pending = null;
        var element = AutomationElement.FocusedElement;
        if (element is null)
        {
            Log.Info("[LEARN] No focused field — nothing to watch");
            return;
        }

        var app = AppName(element);
        if ((bool)element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty))
        {
            Log.Info($"[LEARN] Password field in {app} — not read");
            return;
        }

        // Only the reason is logged, never the field's text.
        var text = ReadText(element);
        if (text is null)
        {
            Log.Info($"[LEARN] {app} does not expose the field's text — cannot learn here");
        }
        else if (!text.Contains(inserted.Trim(), StringComparison.Ordinal))
        {
            Log.Info($"[LEARN] The field in {app} does not show the dictation — not watched");
        }
        else
        {
            _pending = new Pending(element, text, inserted, DateTime.UtcNow, text, DateTime.UtcNow);
        }
    });

    /// <summary>Call when the next dictation starts: the user is done editing the previous one.</summary>
    public void BeforeNextDictation() => Enqueue(() => Check(requireStable: false));

    public void Dispose()
    {
        _poll.Dispose();
        _work.CompleteAdding();
    }

    private void Enqueue(Action action)
    {
        if (Enabled && !_work.IsAddingCompleted)
        {
            _work.TryAdd(action); // a full queue means the watcher is stuck on a hung app — skip
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

        var now = ReadText(p.Element);
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
        if (CorrectionDetector.Detect(p.Before, now, p.Inserted) is var (wrong, correct))
        {
            Log.Info($"[LEARN] Correction: \"{wrong}\" → \"{correct}\"");
            Learned?.Invoke(wrong, correct);
        }
        else
        {
            Log.Info("[LEARN] The field was edited, but not as a one-word fix — nothing learned");
        }
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
        AutomationElement Element, string Before, string Inserted, DateTime PastedAt, string LastSeen, DateTime LastChange);
}
