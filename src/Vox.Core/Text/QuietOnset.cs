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

    /// <summary>
    /// The first pass with each word that lost its sound put back where the second pass heard the full word. When the
    /// passes do not line up word for word, the first stays as it is.
    /// </summary>
    public static string Merge(string first, string second)
    {
        var words = Word().Matches(first);
        var heard = Word().Matches(second);
        if (words.Count != heard.Count)
        {
            return first;
        }

        var text = new StringBuilder(first);
        for (var i = words.Count - 1; i >= 0; i--)
        {
            var word = words[i].Value;
            if (Restored.TryGetValue(word.ToLowerInvariant(), out var full)
                && heard[i].Value.Equals(full, StringComparison.OrdinalIgnoreCase))
            {
                var cased = char.IsUpper(word[0]) ? char.ToUpperInvariant(full[0]) + full[1..] : full;
                text.Remove(words[i].Index, word.Length).Insert(words[i].Index, cased);
            }
        }

        return text.ToString();
    }
}
