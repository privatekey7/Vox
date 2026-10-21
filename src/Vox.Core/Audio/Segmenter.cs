namespace Vox.Core.Audio;

/// <summary>
/// Splits long recordings at pauses before recognition.
/// </summary>
/// <remarks>
/// Both models degrade on long input: in vox.log 21% of Parakeet dictations longer than 15 s
/// came back empty (2% for 5–15 s), and GigaAM drops text after ~100 s and fails beyond. Cuts go
/// to the quietest moment (lowest short-term energy) inside a search window, so pieces break
/// between words; concatenating the pieces gives back the original audio exactly.
/// </remarks>
public static class Segmenter
{
    private const double MinPieceFraction = 0.5; // pieces are cut no earlier than this
    private const double FrameSec = 0.02;
    private const double SmoothSec = 0.3; // a cut needs a real pause, not a plosive

    public static IReadOnlyList<ArraySegment<float>> SplitOnPauses(float[] audio, int sampleRate, double maxSeconds)
    {
        if (audio.Length == 0)
        {
            return [];
        }

        var maxLen = (int)(maxSeconds * sampleRate);
        if (audio.Length <= maxLen)
        {
            return [new ArraySegment<float>(audio)];
        }

        var frame = Math.Max(1, (int)(FrameSec * sampleRate));
        var frames = audio.Length / frame;
        var energy = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            double sum = 0;
            for (var k = f * frame; k < (f + 1) * frame; k++)
            {
                sum += audio[k] * (double)audio[k];
            }

            energy[f] = Math.Sqrt(sum / frame);
        }

        var pieces = new List<ArraySegment<float>>();
        var start = 0;
        while (audio.Length - start > maxLen)
        {
            var lo = (start + (int)(maxLen * MinPieceFraction)) / frame;
            var hi = Math.Min(frames, (start + maxLen) / frame);
            var cut = Math.Min(QuietestFrame(energy, lo, hi) * frame + frame / 2, start + maxLen);
            pieces.Add(new ArraySegment<float>(audio, start, cut - start));
            start = cut;
        }

        pieces.Add(new ArraySegment<float>(audio, start, audio.Length - start));
        return pieces;
    }

    /// <summary>Index of the frame in [lo, hi) with the lowest smoothed energy (first on ties).</summary>
    private static int QuietestFrame(double[] energy, int lo, int hi)
    {
        var half = (int)Math.Round(SmoothSec / FrameSec) / 2;
        var best = lo;
        var bestValue = double.MaxValue;
        for (var f = lo; f < hi; f++)
        {
            double sum = 0;
            for (var k = Math.Max(lo, f - half); k <= Math.Min(hi - 1, f + half); k++)
            {
                sum += energy[k];
            }

            if (sum < bestValue)
            {
                bestValue = sum;
                best = f;
            }
        }

        return best;
    }
}
