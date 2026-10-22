using Vox.Core.Storage;

namespace Vox.Core.Text;

public enum LearnResult
{
    Learned,

    /// <summary>The fixed word was the target of an entry ("GitHbu" from "гитхап → GitHbu"): its target was fixed.</summary>
    TargetFixed,

    /// <summary>"рак" → "RAG": an ordinary Russian word is never learned (add it by hand).</summary>
    CommonWord,

    /// <summary>The text changed without the user typing in that window (autocorrect, Grammarly).</summary>
    NotTyped,

    /// <summary>Under 3 letters: too short to learn automatically (the dictionary takes it by hand).</summary>
    TooShort,

    /// <summary>The entry went back and forth twice ("GitHub" ↔ "Github"): it is no longer changed automatically.</summary>
    Pinned,
}

/// <summary>
/// Self-learning dictionary (owner, 22.10): a fix that passed <see cref="CorrectionDetector"/> goes into
/// the dictionary at once; the last one can be undone (tray → "Отменить последнее запоминание").
/// Guards: only the user's own typing, not an ordinary Russian word, at least 3 letters; an entry that
/// flips back and forth is pinned after two returns.
/// </summary>
public sealed class SelfLearning(VoxDatabase db)
{
    // The 48 000 most frequent Russian word forms (hermitdave/FrequencyWords ru_50k, CC BY-SA 4.0:
    // Data/common-words.LICENSE.md).
    private static readonly Lazy<HashSet<string>> CommonWords = new(LoadCommonWords);

    private const int MinLetters = 3;
    private const int MaxFlips = 2;

    private readonly Lock _lock = new();
    private (string Wrong, string Correct, IReadOnlyList<DictionaryEntry> Replaced)? _last;

    /// <summary>Is this an ordinary Russian word form ("рак", "блоге")?</summary>
    public static bool IsCommonWord(string word) => CommonWords.Value.Contains(word.ToLowerInvariant());

    public bool CanUndo
    {
        get
        {
            lock (_lock)
            {
                return _last is not null;
            }
        }
    }

    /// <param name="wrong">As the user fixed it ("Лебовски"); stored lower-cased.</param>
    /// <returns>What happened, and the pair to show ("гитхап → GitHub" when a target was fixed).</returns>
    /// <param name="userTyped">The user pressed keys in that window after the paste.</param>
    public (LearnResult Result, string Wrong, string Correct) Learn(string wrong, string correct, bool userTyped = true)
    {
        var key = wrong.ToLowerInvariant();
        if (!userTyped)
        {
            return (LearnResult.NotTyped, wrong, correct);
        }

        if (key.Count(char.IsLetter) < MinLetters)
        {
            return (LearnResult.TooShort, wrong, correct);
        }

        lock (_lock)
        {
            var entries = db.DictionaryEntries();

            // The user fixed a word Vox itself wrote from their dictionary (a typo when it was learned):
            // fix that entry's target instead of chaining "GitHbu → GitHub" onto "гитхап → GitHbu".
            var producedBy = entries.Where(e => string.Equals(e.Correct, wrong, StringComparison.OrdinalIgnoreCase)).ToList();
            if (producedBy.Count > 0)
            {
                if (producedBy.Any(IsPinned))
                {
                    return (LearnResult.Pinned, producedBy[0].Wrong, correct);
                }

                foreach (var e in producedBy)
                {
                    RecordTarget(e, correct);
                    db.AddDictionaryEntry(e.Wrong, correct, e.Lang, e.Source);
                }

                _last = (producedBy[0].Wrong, correct, producedBy);
                return (LearnResult.TargetFixed, producedBy[0].Wrong, correct);
            }

            if (IsCommonWord(key))
            {
                return (LearnResult.CommonWord, wrong, correct);
            }

            var replaced = entries.Where(e => e.Wrong == key && e.Lang == "").ToList();
            if (replaced.Any(IsPinned))
            {
                return (LearnResult.Pinned, wrong, correct);
            }

            foreach (var e in replaced)
            {
                RecordTarget(e, correct);
            }

            db.AddDictionaryEntry(key, correct, "", "auto");
            _last = (key, correct, replaced);
            return (LearnResult.Learned, wrong, correct);
        }
    }

    /// <summary>Removes the last learned pair (restoring an entry it overwrote); null if there is none.</summary>
    public (string Wrong, string Correct)? UndoLast()
    {
        lock (_lock)
        {
            if (_last is not var (wrong, correct, replaced))
            {
                return null;
            }

            _last = null;
            if (replaced.Count > 0)
            {
                foreach (var e in replaced)
                {
                    db.AddDictionaryEntry(e.Wrong, e.Correct, e.Lang, e.Source);
                }
            }
            else if (db.DictionaryEntries().FirstOrDefault(e => e.Wrong == wrong && e.Lang == "" && e.Source == "auto") is { } entry)
            {
                db.DeleteDictionaryEntry(entry.Id);
            }

            return (wrong, correct);
        }
    }

    private bool IsPinned(DictionaryEntry entry) => db.TargetHistory(entry.Wrong, entry.Lang).Flips >= MaxFlips;

    // A change of an entry's target; going back to an earlier target counts as a flip.
    private void RecordTarget(DictionaryEntry entry, string target)
    {
        var (targets, flips) = db.TargetHistory(entry.Wrong, entry.Lang);
        var history = targets.Count > 0 ? targets.ToList() : [entry.Correct];
        if (history.Contains(target, StringComparer.Ordinal))
        {
            flips++;
        }

        history.Add(target);
        db.SaveTargetHistory(entry.Wrong, entry.Lang, history.TakeLast(10), flips);
    }

    private static HashSet<string> LoadCommonWords()
    {
        using var stream = typeof(SelfLearning).Assembly.GetManifestResourceStream("Vox.Core.common-words.txt.gz")
            ?? throw new InvalidOperationException("Embedded common-words.txt.gz is missing");
        using var gzip = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var words = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            words.Add(line);
        }

        return words;
    }
}
