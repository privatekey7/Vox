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
/// candidates survive a restart; without one they live in memory only.
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

    /// <summary>True when this exact fix was seen before (learn it now); false on the first sighting.</summary>
    public bool Confirm(string wrong, string correct)
    {
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
}
