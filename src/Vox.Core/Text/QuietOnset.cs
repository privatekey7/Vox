using System.Text;
using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// Words whose first sound is said so softly that the model drops it: "вставь" with a soft "ф" ("фставь") comes out
/// as "Ставь" (owner, 01.11). No rule on the text can tell it from a real "ставь", so the recording is heard again
/// with the full words as a hint, and only the lost sound is taken from that second pass.
/// </summary>
/// <remarks>
/// On the owner's 37 takes 01.11 a hint of 1.5–3.0 brought back 4 of 5 lost "в" and kept all 15 real "ставь";
/// 4.0 turned two real ones into "вставить".
/// </remarks>
public static partial class QuietOnset
{
    /// <summary>What the model wrote → what was said.</summary>
    private static readonly Dictionary<string, string> Restored = new()
    {
        ["ставь"] = "вставь",
        ["ставьте"] = "вставьте",
        ["ставить"] = "вставить",
        ["ставлю"] = "вставлю",
        ["ставим"] = "вставим",
    };

    /// <summary>The hint for the second pass: every restored word, in lower case and with a capital.</summary>
    public static IReadOnlyList<string> Hotwords { get; } =
        [.. Restored.Values.SelectMany(w => new[] { w, char.ToUpperInvariant(w[0]) + w[1..] })];

    /// <summary>How strongly the second pass leans to the hint (sherpa-onnx <c>hotwords_score</c>).</summary>
    public const float HintScore = 2.0f;

    [GeneratedRegex(@"\p{L}+(?:-\p{L}+)*")]
    private static partial Regex Word();

    public static bool NeedsSecondPass(string text) =>
        Word().Matches(text).Any(m => Restored.ContainsKey(m.Value.ToLowerInvariant()));

    public static IReadOnlyList<HeardWord> Words(IReadOnlyList<string> tokens, IReadOnlyList<float> starts) => throw new NotImplementedException();

    public static IReadOnlyList<Suspect> Suspects(IReadOnlyList<HeardWord> words) => throw new NotImplementedException();

    public static (int From, int To) Window(float start, int sampleRate, int length) => throw new NotImplementedException();

    public static string? HeardFull(string suspect, IReadOnlyList<HeardWord> window, float at) => throw new NotImplementedException();

    public static string Restore(string text, int occurrence, string full) => throw new NotImplementedException();
}

public sealed record HeardWord(string Text, float Start);

public sealed record Suspect(string Word, int Occurrence, float Start);
