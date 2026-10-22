namespace Vox.Core.Text;

/// <summary>
/// Corrections waiting for a second sighting: a fix becomes a dictionary entry only when the user
/// makes the same fix again, so a one-off edit never rewrites later dictations. Kept in memory —
/// candidates are forgotten when Vox restarts.
/// </summary>
public sealed class CorrectionCandidates
{
    private const int MaxCandidates = 200;
    private readonly Dictionary<string, string> _seen = []; // wrong → correct seen once
    private readonly Lock _lock = new();

    /// <summary>True when this exact fix was seen before (learn it now); false on the first sighting.</summary>
    public bool Confirm(string wrong, string correct)
    {
        lock (_lock)
        {
            if (_seen.TryGetValue(wrong, out var earlier) && earlier == correct)
            {
                _seen.Remove(wrong);
                return true;
            }

            if (_seen.Count >= MaxCandidates)
            {
                _seen.Clear();
            }

            _seen[wrong] = correct;
            return false;
        }
    }
}
