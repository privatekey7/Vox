using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// A stutter the model wrote as heard goes, the word stays (owner's log 09.11): "ш-ш-что" → "что",
/// "во-во-вот" → "вот", "су-шустрая" → "шустрая"; a repeated short word too: "мне мне" → "мне", "Мне, мне кажется" → "Мне кажется". Hyphenated
/// words ("что-то", "по-моему", "веб-хук") and repeats said on purpose ("да-да", "вот, вот", "на-на-на") stay.
/// </summary>
public static partial class Stutters
{
    // Pieces of one to three letters, the same each time, then the word: "ш-ш-что", "во-во-вот", "как-какое".
    [GeneratedRegex(@"(?<![\p{L}\p{Nd}-])(?<piece>\p{L}{1,3})(?:-\k<piece>)*-(?<word>\p{L}+)(?![\p{L}\p{Nd}-])", RegexOptions.IgnoreCase)]
    private static partial Regex Stutter();

    // Short words a speaker repeats while looking for the next one; not "да", "нет", "так", "вот" (said twice on
    // purpose), not "то" ("то то, то это") and not one-letter vowels (letters spelled out).
    [GeneratedRegex(@"(?<![\p{L}\p{Nd}-])(?<word>я|ты|он|она|оно|мы|вы|они|мне|меня|мной|нам|нас|тебе|тебя|ему|его|ей|её|им|их"
        + @"|что|чтобы|но|ну|в|на|с|к|по|за|из|об|про|для|от|до|это|как|когда|если)(?:,?\s+\k<word>)+(?![\p{L}\p{Nd}-])",
        RegexOptions.IgnoreCase)]
    private static partial Regex Repeated();

    // Sounds the model mixes up at a word's start: "су-шустрая", "за-жаркий".
    private static readonly HashSet<(char, char)> Close = new[]
    {
        ('с', 'ш'), ('с', 'щ'), ('ш', 'щ'), ('ч', 'щ'), ('ч', 'ш'), ('з', 'ж'), ('з', 'с'), ('ж', 'ш'), ('ц', 'с'),
        ('в', 'ф'), ('б', 'п'), ('д', 'т'), ('г', 'к'),
    }.SelectMany(p => new[] { p, (p.Item2, p.Item1) }).ToHashSet();

    public static string Remove(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = Stutter().Replace(text, m =>
        {
            var piece = m.Groups["piece"].Value;
            var word = m.Groups["word"].Value;
            if (!IsStutter(piece.ToLowerInvariant(), word.ToLowerInvariant()))
            {
                return m.Value;
            }

            return char.IsUpper(piece[0]) ? char.ToUpperInvariant(word[0]) + word[1..] : word;
        });
        return Repeated().Replace(text, m => IsRepeat(text, m) ? m.Groups["word"].Value : m.Value);
    }

    // Mid-sentence a comma parts two clauses ("подскажи мне, мне кажется") and a capital is a name ("аккаунт Про,
    // Про"): only "мне мне" or a sentence start ("Мне, мне кажется") is a repeat.
    private static bool IsRepeat(string text, Match m)
    {
        var before = text.AsSpan(0, m.Index).TrimEnd();
        var sentenceStart = before.IsEmpty || ".!?…".Contains(before[^1]);
        return sentenceStart || (!m.Value.Contains(',', StringComparison.Ordinal) && !char.IsUpper(m.Value[0]));
    }

    private static bool IsStutter(string piece, string word)
    {
        if (word == piece || word.Length <= piece.Length)
        {
            return false; // "да-да", "на-на-на": a word said again
        }

        return piece.Length == 1
            ? piece[0] == word[0] || Close.Contains((piece[0], word[0])) // "п-привет", "ш-ш-что"
            : word.StartsWith(piece, StringComparison.Ordinal) // "во-во-вот", "как-какое"
                || (piece.Length == 2 && piece[1] == word[1] && Close.Contains((piece[0], word[0]))); // "су-шустрая"
    }
}
