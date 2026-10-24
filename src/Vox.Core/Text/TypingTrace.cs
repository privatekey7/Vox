namespace Vox.Core.Text;

/// <summary>
/// How the dictation looked in the field after the user's editing keys (security audit L5, 25.10): was a fix typed
/// by hand? Typing goes through in-between states of the word ("кубернетис" → "кубер" → "" → "Kube" → "Kubernetes");
/// a page script that waits for any key and then swaps the word goes from the old word to the new one in one step.
/// </summary>
/// <remarks>
/// One-step fixes of the user's own (a whole word pasted over a selection, one letter typed over a selected one) are
/// not learned automatically either: Ctrl+S remembers them. Only the changed words inside the dictation are kept, in
/// memory, not the field's text.
/// </remarks>
public sealed class TypingTrace(string before, string inserted)
{
    public const int MaxEdits = 200;

    private readonly HashSet<(int OldStart, int OldEnd, string Text)> _edits = [];
    private string? _last;

    /// <summary>The field's text right after an editing key of the user in its window.</summary>
    public void Add(string sample)
    {
        if (sample == before || sample == _last || _edits.Count >= MaxEdits)
        {
            return; // nothing new (Backspace at the start), or a long session: the diff is not free
        }

        _last = sample;

        foreach (var edit in CorrectionDetector.DictationEdits(before, sample, inserted))
        {
            _edits.Add(edit);
        }
    }

    /// <summary>
    /// Was the fix <paramref name="after"/> shows (the word now <paramref name="correct"/>, as
    /// <see cref="CorrectionDetector.Detect"/> gave it) seen half-done: that word neither the old one nor the new one?
    /// </summary>
    public bool ShowsTyping(string after, string correct) =>
        CorrectionDetector.DictationEdits(before, after, inserted) is [var (start, end, _)]
        && _edits.Any(e => e.OldStart < Math.Max(end, start + 1) && Math.Max(e.OldEnd, e.OldStart + 1) > start
            && !e.Text.Contains(correct, StringComparison.Ordinal));
}
