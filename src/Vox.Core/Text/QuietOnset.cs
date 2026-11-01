using System.Text;
using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// Words whose first sound is said so softly that the model drops it: "вставь" with a soft "ф" ("фставь") comes out
/// as "Ставь" (owner, 01.11). No rule on the text can tell it from a real "ставь", so the recording is heard again
/// with the full words as a hint, and only the lost sound is taken from that second pass. Only a window around the
/// word is heard again: the whole piece took +1.7 s on a 34 s dictation, and the owner's "вставь. Ставь чайник" came
/// out as "Вставь чайник" (owner, 01.11).
/// </summary>
/// <remarks>
/// Tuned on the owner's takes 01.11 through Vox.Replay (37 test phrases, 15 more, a 34 s dictation): a window of 1 s
/// before the word and 1.5 s after it with a hint of 1.5 brought back 7 lost "в" of 10 and kept all real "ставь";
/// 1.75 did the same, 2.0 turned "Ставить пробел" into "Вставить", and so did a window of 0.6 s before and 0.9 s after.
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
    public const float HintScore = 1.5f;

    [GeneratedRegex(@"\p{L}+(?:-\p{L}+)*")]
    private static partial Regex Word();

    public static bool NeedsSecondPass(string text) => SuspectsIn(text).Any();

    /// <summary>How many words of the text may have lost their first sound: the tokens must give as many.</summary>
    public static int CountSuspects(string text) => SuspectsIn(text).Count();

    private static IEnumerable<Match> SuspectsIn(string text) =>
        Word().Matches(text).Where(m => Restored.ContainsKey(m.Value.ToLowerInvariant()));

    /// <summary>Seconds of the recording heard again before the word starts: the soft sound the model dropped.</summary>
    private const float Before = 1.0f;

    /// <summary>Seconds heard again from the word's start on: the word itself.</summary>
    private const float After = 1.5f;

    /// <summary>How far, in seconds, the word heard again may start from where the first pass put it.</summary>
    private const float Near = 0.3f;

    /// <summary>
    /// The words of a recognised text with the second each starts at. GigaAM marks a word start with a leading space
    /// on its first token; punctuation tokens join the word before and are dropped.
    /// </summary>
    public static IReadOnlyList<HeardWord> Words(IReadOnlyList<string> tokens, IReadOnlyList<float> starts)
    {
        var words = new List<HeardWord>();
        var text = new StringBuilder();
        var start = 0f;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].StartsWith(' ') || i == 0)
            {
                Add();
                start = starts[i];
            }

            text.Append(tokens[i]);
        }

        Add();
        return words;

        void Add()
        {
            var word = text.ToString().Trim().Trim(Punctuation);
            if (word.Length > 0)
            {
                words.Add(new HeardWord(word, start));
            }

            text.Clear();
        }
    }

    private static readonly char[] Punctuation = [.. ".,!?…:;—–-«»\"()"];

    /// <summary>The words that may have lost their first sound, numbered among themselves in the order of the text.</summary>
    public static IReadOnlyList<Suspect> Suspects(IReadOnlyList<HeardWord> words) =>
        [.. words.Where(w => Restored.ContainsKey(w.Text.ToLowerInvariant())).Select((w, i) => new Suspect(w.Text.ToLowerInvariant(), i, w.Start))];

    /// <summary>The samples to hear again for a word starting at <paramref name="start"/> seconds.</summary>
    public static (int From, int To) Window(float start, int sampleRate, int length) =>
        (Math.Max(0, (int)Math.Round((start - Before) * sampleRate)), Math.Min(length, (int)Math.Round((start + After) * sampleRate)));

    /// <summary>
    /// The full word, when the window heard it at <paramref name="at"/> seconds into the window (where the first pass
    /// put the suspect); otherwise <c>null</c>.
    /// </summary>
    public static string? HeardFull(string suspect, IReadOnlyList<HeardWord> window, float at)
    {
        var full = Restored[suspect.ToLowerInvariant()];
        var heard = window.Where(w => Math.Abs(w.Start - at) <= Near).MinBy(w => Math.Abs(w.Start - at));
        return heard != null && heard.Text.Equals(full, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>The text with the <paramref name="occurrence"/>-th suspect word (see <see cref="Suspects"/>) made full, keeping its case.</summary>
    public static string Restore(string text, int occurrence, string full)
    {
        var word = SuspectsIn(text).ElementAtOrDefault(occurrence);
        if (word == null)
        {
            return text;
        }

        var cased = char.IsUpper(word.Value[0]) ? char.ToUpperInvariant(full[0]) + full[1..] : full;
        return text[..word.Index] + cased + text[(word.Index + word.Length)..];
    }
}

public sealed record HeardWord(string Text, float Start);

public sealed record Suspect(string Word, int Occurrence, float Start);
