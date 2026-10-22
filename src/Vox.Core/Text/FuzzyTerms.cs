using System.Text;
using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// Phonetic search in terms.json, after the exact <see cref="Terms"/>: a model's own spelling that no
/// entry lists ("Fruentand", "Bek End", "фронтанд") is replaced when one term is clearly the closest.
/// </summary>
/// <remarks>
/// Latin words (one, or two the model split: "Frount End") are compared with every term by a sound
/// skeleton. Off by default: without an English word list ordinary words come too close to brands
/// ("water" ~ "Wouter", "course" ~ "CORS"); see docs/csharp-migration.md. A Cyrillic word is compared only
/// with the Cyrillic spellings of the "spelling" group ("фронтенд", "бэкенд") and their case forms,
/// letter by letter: by sound, ordinary word forms come too close to brands ("домены" ~ "джемини").
/// Never touched: under 5 letters, ordinary Russian words (common-words), words already spelled as a
/// term; a Cyrillic capital mid-sentence (a name) only on an exact match; two terms about as close.
/// The user's own dictionary is not searched: it is exact, and a learned typo must not pull in other words.
/// </remarks>
public sealed partial class FuzzyTerms
{
    /// <summary>A replacement, for the log: "[FUZZY] "Fruentand" → "фронтенд" 0.92".</summary>
    public sealed record Match(string Spoken, string Replacement, double Score);

    /// <param name="Key">Sound skeleton (Latin search) or normalised Cyrillic form (Cyrillic search).</param>
    /// <param name="Ending">Case ending of a Cyrillic form ("фронтенде": "е"), null for the base form.</param>
    private sealed record Candidate(string Key, string Source, string Target, string? Ending = null);

    private const int MinLetters = 5;
    private const double Threshold = 0.85;
    private const double LatinThreshold = 0.8; // a Latin word in Russian speech is rarely an ordinary word
    private const double Margin = 0.05;

    private static readonly Lazy<FuzzyTerms> Default = new(() =>
    {
        var groups = Terms.LoadGroups();
        return new FuzzyTerms(
            groups.Values.SelectMany(g => g).SelectMany(t => t.Value.Select(s => (s, t.Key))),
            groups.TryGetValue("spelling", out var spelling) ? spelling.SelectMany(t => t.Value.Select(s => (s, t.Key))) : [],
            latin: false);
    });

    private readonly bool _latinOn;

    private readonly Dictionary<char, List<Candidate>> _latin = [];
    private readonly Dictionary<char, List<Candidate>> _cyrillic = [];

    [GeneratedRegex(@"\p{L}+(?:-\p{L}+)*")] private static partial Regex Word();

    /// <param name="pairs">(spoken source, term) as in terms.json; each term also stands for itself.</param>
    /// <param name="cyrillicPairs">Pairs whose one-word Cyrillic sources a Cyrillic word may match ("spelling").</param>
    /// <param name="latin">Also search Latin words (experimental, see remarks).</param>
    public FuzzyTerms(IEnumerable<(string Source, string Target)> pairs, IEnumerable<(string Source, string Target)> cyrillicPairs, bool latin)
    {
        _latinOn = latin;
        var list = pairs.ToList();
        foreach (var (source, target) in list.Concat(list.Select(p => p.Target).Distinct().Select(t => (t, t))))
        {
            // Spelled letters ("бэ гэ пэ") and abbreviations ("BGP" → "бгп") are too short to compare by sound.
            var skeleton = Skeleton(source);
            if (skeleton.Length >= 4 && !source.Split(' ', '-').Any(w => w.Length <= 2))
            {
                Add(_latin, new Candidate(skeleton, source, target));
            }
        }

        foreach (var (source, target) in cyrillicPairs.Where(p => IsCyrillic(p.Source) && !p.Source.Contains(' ', StringComparison.Ordinal)))
        {
            Add(_cyrillic, new Candidate(Normal(source), source, target));
            foreach (var (form, ending) in Declension.Forms(source))
            {
                Add(_cyrillic, new Candidate(Normal(form), source, target, ending));
            }
        }
    }

    private static void Add(Dictionary<char, List<Candidate>> index, Candidate c)
    {
        var key = Class(c.Key[0]);
        if (!index.TryGetValue(key, out var bucket))
        {
            index[key] = bucket = [];
        }

        if (!bucket.Any(b => b.Key == c.Key && b.Target == c.Target))
        {
            bucket.Add(c);
        }
    }

    /// <summary>Replaces unknown spellings of terms in <paramref name="text"/>; each replacement goes to <paramref name="log"/>.</summary>
    public static string Apply(string text, ICollection<Match>? log = null) => Default.Value.Replace(text, log);

    public string Replace(string text, ICollection<Match>? log = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var words = Word().Matches(text);
        var result = new StringBuilder(text.Length);
        var copied = 0;
        for (var i = 0; i < words.Count;)
        {
            var n = 0;
            (string Replacement, double Score)? found = null;
            // Two Latin words the model split ("Frount End") first, then one word.
            for (var span = Math.Min(2, words.Count - i); span >= 1 && found is null; span--)
            {
                var spoken = string.Concat(Enumerable.Range(i, span).Select(k => words[k].Value));
                if ((IsCyrillic(spoken) ? span == 1 : _latinOn) && Eligible(text, words, i, span, out var name) && Find(spoken, name) is { } match)
                {
                    found = match;
                    n = span;
                }
            }

            if (found is var (replacement, score))
            {
                var start = words[i].Index;
                var end = words[i + n - 1].Index + words[i + n - 1].Length;
                var spoken = text[start..end];
                replacement = Cased(replacement, spoken, StartsSentence(text, start));
                log?.Add(new Match(spoken, replacement, Math.Round(score, 2)));
                result.Append(text, copied, start - copied).Append(replacement);
                copied = end;
                i += n;
            }
            else
            {
                i++;
            }
        }

        return copied == 0 ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    private static bool Eligible(string text, MatchCollection words, int i, int n, out bool name)
    {
        name = false;
        if (Enumerable.Range(i, n).Sum(k => words[k].Value.Count(char.IsLetter)) < MinLetters)
        {
            return false;
        }

        for (var k = i; k < i + n; k++)
        {
            var word = words[k].Value;
            if (word.Length < 2 || SelfLearning.IsCommonWord(word) || Terms.IsTermForm(word))
            {
                return false; // an ordinary word or a term already
            }

            name |= IsCyrillic(word) && char.IsUpper(word[0]) && !StartsSentence(text, words[k].Index);

            if (k > i)
            {
                var gap = text.AsSpan(words[k - 1].Index + words[k - 1].Length, words[k].Index - words[k - 1].Index - words[k - 1].Length);
                if (gap.ContainsAnyExcept(' ', ' '))
                {
                    return false; // "Bek, End" is two words
                }
            }
        }

        return true;
    }

    private (string Replacement, double Score)? Find(string spoken, bool name)
    {
        var cyrillic = IsCyrillic(spoken);
        var key = cyrillic ? Normal(spoken) : Skeleton(spoken);
        if (key.Length < 4 || !(cyrillic ? _cyrillic : _latin).TryGetValue(Class(key[0]), out var bucket))
        {
            return null;
        }

        Candidate? best = null;
        double bestScore = 0, second = 0;
        foreach (var c in bucket)
        {
            if (Math.Abs(c.Key.Length - key.Length) > 3)
            {
                continue;
            }

            var score = Similarity(key, c.Key);
            if (score > bestScore)
            {
                if (best is not null && best.Target != c.Target)
                {
                    second = bestScore;
                }

                (best, bestScore) = (c, score);
            }
            else if (best is not null && c.Target != best.Target && score > second)
            {
                second = score;
            }
        }

        var threshold = name ? 1 : cyrillic ? Threshold : LatinThreshold;
        if (best is null || bestScore < threshold || bestScore - second < Margin)
        {
            return null; // nothing close, or two terms about as close
        }

        // "фронтанде" ~ "фронтенде": the case form carries over to the term.
        var replacement = best.Ending is { Length: > 0 } ending
            ? Declension.Inflect(best.Target, best.Source, ending) ?? best.Target
            : best.Target;
        var target = cyrillic ? Normal(best.Target) : "";
        if (LettersOf(replacement) == LettersOf(spoken) || (cyrillic && key.StartsWith(target, StringComparison.Ordinal)))
        {
            return null; // already the term ("Notepad" of "Notepad++") or its other form ("нейросети")
        }

        return (replacement, bestScore);
    }

    private static string LettersOf(string s) => string.Concat(s.Where(char.IsLetter)).ToLowerInvariant();

    // Cyrillic compared letter by letter, with spellings a model mixes up merged: ё/э → е, no ь/ъ, no doubles.
    private static string Normal(string word)
    {
        var sb = new StringBuilder(word.Length);
        foreach (var ch in word.ToLowerInvariant())
        {
            var c = ch switch { 'ё' or 'э' => 'е', _ => ch };
            if (c is >= 'а' and <= 'я' && c is not ('ь' or 'ъ') && (sb.Length == 0 || sb[^1] != c))
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    // Same rule as Terms: a Cyrillic capital is the speaker's, a Latin one is the model's (kept at a sentence start).
    private static string Cased(string replacement, string spoken, bool sentenceStart) =>
        replacement.Length > 0 && char.IsUpper(spoken[0]) && char.IsLower(replacement[0]) && IsCyrillic(replacement)
            && (sentenceStart || IsCyrillic(spoken))
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;

    private static bool StartsSentence(string text, int index)
    {
        var before = text.AsSpan(0, index).TrimEnd();
        return before.IsEmpty || ".!?…".Contains(before[^1]);
    }

    private static bool IsCyrillic(string s) => s.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');

    private static readonly (string Latin, string Cyrillic)[] Digraphs =
    [
        ("sch", "ш"), ("tch", "ч"), ("chr", "кр"), ("ign", "айн"), ("sh", "ш"), ("ch", "ч"), ("zh", "ж"), ("kh", "х"),
        ("ph", "ф"), ("th", "т"), ("ck", "к"), ("qu", "кв"), ("oo", "у"), ("ee", "и"),
        ("ce", "се"), ("ci", "си"), ("cy", "си"),
    ];

    private static readonly Dictionary<char, string> Letters = new()
    {
        ['a'] = "а", ['b'] = "б", ['c'] = "к", ['d'] = "д", ['e'] = "е", ['f'] = "ф", ['g'] = "г", ['h'] = "х",
        ['i'] = "и", ['j'] = "дж", ['k'] = "к", ['l'] = "л", ['m'] = "м", ['n'] = "н", ['o'] = "о", ['p'] = "п",
        ['q'] = "к", ['r'] = "р", ['s'] = "с", ['t'] = "т", ['u'] = "у", ['v'] = "в", ['w'] = "в", ['x'] = "кс",
        ['y'] = "и", ['z'] = "з",
    };

    /// <summary>
    /// The sound skeleton: Latin read as Cyrillic, every vowel run one "а", no ь/ъ/й, no doubled
    /// consonants, no final vowel, a devoiced final consonant ("Frount End" and "фронтенд" → "франтант").
    /// </summary>
    public static string Skeleton(string word)
    {
        var s = word.ToLowerInvariant();
        foreach (var (latin, cyrillic) in Digraphs)
        {
            s = s.Replace(latin, cyrillic, StringComparison.Ordinal);
        }

        var cyr = new StringBuilder(s.Length + 4);
        foreach (var ch in s)
        {
            cyr.Append(Letters.TryGetValue(ch, out var t) ? t : ch.ToString());
        }

        var sb = new StringBuilder(cyr.Length);
        foreach (var ch in cyr.ToString())
        {
            if ("ьъй".Contains(ch, StringComparison.Ordinal) || !(ch is >= 'а' and <= 'я' or 'ё'))
            {
                continue;
            }

            var c = "аеёиоуыэюя".Contains(ch, StringComparison.Ordinal) ? 'а' : ch;
            if (sb.Length == 0 || sb[^1] != c)
            {
                sb.Append(c);
            }
        }

        if (sb.Length > 1 && sb[^1] == 'а')
        {
            sb.Length--;
        }

        if (sb.Length > 0 && Devoiced.TryGetValue(sb[^1], out var voiceless))
        {
            sb[^1] = voiceless;
        }

        return sb.ToString();
    }

    private static readonly Dictionary<char, char> Devoiced = new() { ['б'] = 'п', ['в'] = 'ф', ['г'] = 'к', ['д'] = 'т', ['ж'] = 'ш', ['з'] = 'с' };

    // Sounds a model mixes up: a swap inside one class costs half.
    private static char Class(char c) => c switch
    {
        'б' or 'в' or 'п' or 'ф' => 'б',
        'д' or 'т' => 'д',
        'г' or 'к' or 'х' => 'г',
        'з' or 'с' or 'ц' => 'з',
        'ж' or 'ш' or 'щ' or 'ч' => 'ж',
        _ => c,
    };

    /// <summary>1 − weighted edit distance / longer length, on skeletons.</summary>
    public static double Similarity(string a, string b)
    {
        var prev = new double[b.Length + 1];
        var cur = new double[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : Class(a[i - 1]) == Class(b[j - 1]) ? 0.5 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, cur) = (cur, prev);
        }

        var longest = Math.Max(a.Length, b.Length);
        return longest == 0 ? 0 : 1 - prev[b.Length] / longest;
    }
}
