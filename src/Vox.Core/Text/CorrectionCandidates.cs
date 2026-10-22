namespace Vox.Core.Text;

/// <summary>Where <see cref="CorrectionCandidates"/> keeps fixes between runs (vox_data.db).</summary>
public interface ICandidateStore
{
    IReadOnlyDictionary<string, string> LoadCandidates();

    void SaveCandidate(string wrong, string correct);

    void RemoveCandidate(string wrong);

    void ClearCandidates();
}

/// <summary>
/// Corrections waiting for a second sighting: a fix becomes a dictionary entry only when the user
/// makes the same fix again, so a one-off edit never rewrites later dictations. With a store the
/// candidates survive a restart; without one they live in memory only. A common Russian word is never
/// learned ("рак" → "RAG" would turn every "рак" into RAG): such a fix goes into the dictionary by hand.
/// </summary>
public sealed class CorrectionCandidates
{
    private const int MaxCandidates = 200;
    private readonly Dictionary<string, string> _seen; // wrong → correct seen once
    private readonly ICandidateStore? _store;
    private readonly Lock _lock = new();

    public CorrectionCandidates(ICandidateStore? store = null)
    {
        _store = store;
        _seen = store is null ? [] : new Dictionary<string, string>(store.LoadCandidates());
    }

    // The 48 000 most frequent Russian word forms (hermitdave/FrequencyWords ru_50k, CC BY-SA 4.0:
    // Data/common-words.LICENSE.md).
    private static readonly Lazy<HashSet<string>> CommonWords = new(LoadCommonWords);

    /// <summary>Is this an ordinary Russian word form ("рак", "блоге")?</summary>
    public static bool IsCommonWord(string word) => CommonWords.Value.Contains(word.ToLowerInvariant());

    /// <summary>True when this exact fix was seen before (learn it now); false on the first sighting.</summary>
    public bool Confirm(string wrong, string correct)
    {
        if (IsCommonWord(wrong))
        {
            return false;
        }

        lock (_lock)
        {
            if (_seen.TryGetValue(wrong, out var earlier) && earlier == correct)
            {
                _seen.Remove(wrong);
                _store?.RemoveCandidate(wrong);
                return true;
            }

            if (_seen.Count >= MaxCandidates)
            {
                _seen.Clear();
                _store?.ClearCandidates();
            }

            _seen[wrong] = correct;
            _store?.SaveCandidate(wrong, correct);
            return false;
        }
    }

    private static HashSet<string> LoadCommonWords()
    {
        using var stream = typeof(CorrectionCandidates).Assembly.GetManifestResourceStream("Vox.Core.common-words.txt.gz")
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
