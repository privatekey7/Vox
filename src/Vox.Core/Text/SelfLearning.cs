using Vox.Core.Storage;

namespace Vox.Core.Text;

public enum LearnResult
{
    Learned,

    /// <summary>The fixed word was the target of an entry ("GitHbu" from "гитхап → GitHbu"): its target was fixed.</summary>
    TargetFixed,

    /// <summary>"рак" → "RAG": an ordinary Russian word is never learned (add it by hand).</summary>
    CommonWord,
}

/// <summary>
/// Self-learning dictionary (owner, 22.10): a fix that passed <see cref="CorrectionDetector"/> goes into
/// the dictionary at once; the last one can be undone (tray → "Отменить последнее запоминание").
/// An ordinary Russian word is never learned.
/// </summary>
public sealed class SelfLearning(VoxDatabase db)
{
    // The 48 000 most frequent Russian word forms (hermitdave/FrequencyWords ru_50k, CC BY-SA 4.0:
    // Data/common-words.LICENSE.md).
    private static readonly Lazy<HashSet<string>> CommonWords = new(LoadCommonWords);

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
    public (LearnResult Result, string Wrong, string Correct) Learn(string wrong, string correct)
    {
        var key = wrong.ToLowerInvariant();
        lock (_lock)
        {
            var entries = db.DictionaryEntries();

            // The user fixed a word Vox itself wrote from their dictionary (a typo when it was learned):
            // fix that entry's target instead of chaining "GitHbu → GitHub" onto "гитхап → GitHbu".
            var producedBy = entries.Where(e => string.Equals(e.Correct, wrong, StringComparison.OrdinalIgnoreCase)).ToList();
            if (producedBy.Count > 0)
            {
                foreach (var e in producedBy)
                {
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
