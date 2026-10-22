namespace Vox.Core.Text;

/// <summary>
/// Russian case forms for dictionary replacements: "на гитхабе" matches the entry "гитхаб", and
/// "с керилом" + "керил → Кирилл" becomes "с Кириллом". A small fixed set of noun endings — not a
/// morphological analyser; only single Cyrillic words of 4+ letters are declined.
/// </summary>
public static class Declension
{
    private const int MinLength = 4;

    // Paradigms by the word's last letter (nouns): consonant, -а/-я, -ь, -й.
    private static readonly string[] Hard = ["а", "у", "ом", "е", "ы", "ов", "ам", "ами", "ах"];
    private static readonly string[] AYa = ["ы", "и", "е", "у", "ой", "ою", "ам", "ами", "ах", "ям", "ями", "ях"];
    private static readonly string[] Soft = ["я", "ю", "ем", "и", "ей", "е", "ям", "ями", "ях"];
    private static readonly string[] Yot = ["я", "ю", "ем", "е", "ев", "и", "ям", "ями", "ях"];

    private static readonly string[] AllEndings = [.. Hard.Concat(AYa).Concat(Soft).Concat(Yot).Distinct().OrderByDescending(e => e.Length)];

    /// <summary>
    /// Case forms of <paramref name="word"/> other than the word itself, with their endings. The last word of
    /// a phrase may be shorter (<paramref name="minLength"/> 3: "дата сет" → "дата сете").
    /// </summary>
    public static IEnumerable<(string Form, string Ending)> Forms(string word, int minLength = MinLength)
    {
        var lower = word.ToLowerInvariant();
        if (Paradigm(lower, minLength) is not var (stem, endings))
        {
            yield break;
        }

        foreach (var ending in endings)
        {
            var form = stem + ending;
            if (form != lower)
            {
                yield return (form, ending);
            }
        }
    }

    /// <summary>
    /// The replacement for a case form of <paramref name="source"/>: a Latin target as is ("GitHub"),
    /// a Cyrillic word with the same ending ("Кирилл" + "ом", "ИИ-агент" + "ы"), or null when the ending
    /// cannot carry over.
    /// </summary>
    public static string? Inflect(string target, string source, string ending, int minLength = MinLength)
    {
        if (target.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
        {
            return target;
        }

        var head = target.LastIndexOf('-') + 1; // "ИИ-агент" declines "агент"
        if (target.Contains(' ', StringComparison.Ordinal)
            || Paradigm(target[head..].ToLowerInvariant(), Math.Min(minLength, MinLength)) is not var (stem, endings)
            || Paradigm(source.ToLowerInvariant(), minLength) is not var (_, sourceEndings)
            || !ReferenceEquals(endings, sourceEndings))
        {
            return null;
        }

        return target[..(head + stem.Length)] + ending;
    }

    /// <summary>
    /// A learned pair in its base form, so one fix covers every case: "керила → Кирилла" →
    /// "керил → Кирилл", "гитхабе → GitHub" → "гитхаб → GitHub".
    /// </summary>
    public static (string Wrong, string Correct) BaseForm(string wrong, string correct)
    {
        var latin = correct.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        foreach (var ending in AllEndings)
        {
            if (!wrong.EndsWith(ending, StringComparison.Ordinal) || wrong.Length - ending.Length < MinLength
                || !IsCyrillic(wrong[..^ending.Length]))
            {
                continue;
            }

            if (latin)
            {
                return (wrong[..^ending.Length], correct);
            }

            if (correct.EndsWith(ending, StringComparison.OrdinalIgnoreCase) && correct.Length - ending.Length >= MinLength)
            {
                var (w, c) = (wrong[..^ending.Length], correct[..^ending.Length]);
                if (IsConsonant(w[^1]) && IsConsonant(char.ToLowerInvariant(c[^1])))
                {
                    return (w, c);
                }
            }
        }

        return (wrong, correct);
    }

    private static (string Stem, string[] Endings)? Paradigm(string word, int minLength = MinLength)
    {
        if (word.Length < minLength || !IsCyrillic(word))
        {
            return null;
        }

        return word[^1] switch
        {
            'а' or 'я' => (word[..^1], AYa),
            'ь' => (word[..^1], Soft),
            'й' => (word[..^1], Yot),
            var c when IsConsonant(c) => (word, Hard),
            _ => null, // -о, -е, -и…: usually indeclinable borrowings
        };
    }

    private static bool IsConsonant(char c) => "бвгджзклмнпрстфхцчшщ".Contains(c, StringComparison.Ordinal);

    private static bool IsCyrillic(string word) => word.All(c => c is >= 'а' and <= 'я' or 'ё');
}
