using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>Russian phonetic → English tech term replacements ("гитхаб" → "GitHub").</summary>
public static partial class Terms
{
    // Every source and its case forms, keyed by lower-case words joined with one space:
    // "гитхаб", "гитхабе" ("гитхаб" + "е"), "докер компоузе" (multi-word sources decline the last word).
    // Single-word forms that are ordinary Russian words are left out (Data/no-declension.json,
    // generated against a frequency list: docs/declension.md).
    private static readonly Lazy<(Dictionary<string, Entry> Phrases, int MaxWords)> Index = new(BuildIndex);

    private sealed record Entry(string Replacement, string LastSourceWord, string? Ending, int MinLength = 4);

    // The last word of a phrase may be short: the phrase itself guards against ordinary words.
    private const int PhraseWordMinLength = 3;

    // A word is letters/digits/underscores; a hyphen joins parts into one word ("пиф-паф" is not "паф").
    [GeneratedRegex(@"[\p{L}\p{Nd}_]+(?:-[\p{L}\p{Nd}_]+)*")]
    private static partial Regex Word();

    /// <summary>Replace terms in one pass, the longest phrase first ("гитхаб экшенс" before "гитхаб").</summary>
    public static string Replace(string text)
    {
        var (phrases, maxWords) = Index.Value;
        var words = Word().Matches(text);
        var result = new System.Text.StringBuilder(text.Length);
        var copied = 0;
        for (var i = 0; i < words.Count;)
        {
            var matched = 0;
            for (var n = Math.Min(maxWords, words.Count - i); n >= 1 && matched == 0; n--)
            {
                if (Phrase(text, words, i, n) is { } key && phrases.TryGetValue(key, out var entry)
                    && Replacement(entry, words[i].Value) is { } replacement)
                {
                    result.Append(text, copied, words[i].Index - copied).Append(replacement);
                    copied = words[i + n - 1].Index + words[i + n - 1].Length;
                    matched = n;
                }
            }

            i += Math.Max(matched, 1);
        }

        return copied == 0 ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    // Words i..i+n-1 as a lookup key, or null when they are not separated by whitespace only.
    private static string? Phrase(string text, MatchCollection words, int i, int n)
    {
        if (n == 1)
        {
            return words[i].Value.ToLowerInvariant();
        }

        for (var k = i; k < i + n - 1; k++)
        {
            var gapStart = words[k].Index + words[k].Length;
            if (text.AsSpan(gapStart, words[k + 1].Index - gapStart).ContainsAnyExcept(' ', '\u00A0'))
            {
                return null;
            }
        }

        return string.Join(' ', Enumerable.Range(i, n).Select(k => words[k].Value.ToLowerInvariant()));
    }

    // A Cyrillic spelling fix keeps the spoken capital ("Промт" → "Промпт"); Latin terms keep their own case.
    private static string? Replacement(Entry entry, string spoken)
    {
        var replacement = entry.Ending is null
            ? entry.Replacement
            : Declension.Inflect(entry.Replacement, entry.LastSourceWord, entry.Ending, entry.MinLength);
        return replacement is { Length: > 0 } && char.IsUpper(spoken[0]) && char.IsLower(replacement[0]) && !HasLatin(replacement)
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;
    }

    private static bool HasLatin(string s) => s.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');

    private static (Dictionary<string, Entry>, int) BuildIndex()
    {
        var pairs = LoadPairs();
        var phrases = new Dictionary<string, Entry>();
        foreach (var pair in pairs)
        {
            var source = pair[0].ToLowerInvariant();
            phrases[source] = new Entry(pair[1], source.Split(' ')[^1], null);
        }

        var excluded = LoadResource<string[]>("Vox.Core.no-declension.json").ToHashSet();
        var forms = new Dictionary<string, Entry>();
        var ambiguous = new HashSet<string>();
        foreach (var pair in pairs)
        {
            var source = pair[0].ToLowerInvariant();
            var space = source.LastIndexOf(' ');
            var (prefix, last) = space < 0 ? ("", source) : (source[..(space + 1)], source[(space + 1)..]);
            var minLength = space < 0 ? 4 : PhraseWordMinLength;
            foreach (var (form, ending) in Declension.Forms(last, minLength))
            {
                var key = prefix + form;
                if (key.Length < 5 || phrases.ContainsKey(key) || (space < 0 && excluded.Contains(form)))
                {
                    continue; // too short to be safe, a term of its own, or an ordinary word
                }

                if (!forms.TryAdd(key, new Entry(pair[1], last, ending, minLength)) && forms[key].Replacement != pair[1])
                {
                    ambiguous.Add(key); // two terms share this form: keep neither
                }
            }
        }

        foreach (var (key, entry) in forms)
        {
            if (!ambiguous.Contains(key))
            {
                phrases[key] = entry;
            }
        }

        // A phrase is also said with hyphens: "клод-код", "клод-кода" (owner's log).
        foreach (var (key, entry) in phrases.Where(p => p.Key.Contains(' ', StringComparison.Ordinal)).ToList())
        {
            phrases.TryAdd(key.Replace(' ', '-'), entry);
        }

        return (phrases, phrases.Keys.Max(k => k.Count(c => c == ' ') + 1));
    }

    // Latin terms ("Docker", "Claude Code") and one-word Cyrillic brands ("Шедеврум"); not "Дарт Вейдер",
    // which is in the dictionary only to shield "дарт" from "Dart".
    private static readonly Lazy<HashSet<string>> TermWords = new(() =>
        LoadPairs().Select(p => p[1]).Where(t => HasLatin(t) || !t.Contains(' ', StringComparison.Ordinal))
            .SelectMany(t => t.Split(' ')).ToHashSet(StringComparer.Ordinal));

    /// <summary>Is this word (exact case) part of a term's spelling ("Docker", "GitHub")?</summary>
    public static bool IsTermForm(string word) => TermWords.Value.Contains(word);

    /// <summary>terms.json: { group: { target: [spoken sources] } }, flattened to (source, target).</summary>
    internal static string[][] LoadPairs() =>
        [.. LoadGroups().Values.SelectMany(g => g).SelectMany(t => t.Value.Select(source => new[] { source, t.Key }))];

    internal static Dictionary<string, Dictionary<string, string[]>> LoadGroups() =>
        LoadResource<Dictionary<string, Dictionary<string, string[]>>>("Vox.Core.terms.json");

    private static T LoadResource<T>(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded {name} is missing");
        return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidOperationException($"{name} is empty");
    }
}

/// <summary>Removes pure hesitation sounds ("э-э", "ммм", "uh"); meaningful words like "ну" stay.</summary>
public static partial class Fillers
{
    // ru: drawn-out or repeated sounds of any length ("эээ", "Ааа", "э-э-э", "м-м-м", "хммм") and a
    // lone "э"; real words made of the same letters ("а", "мы", "ам", "мама") are not matched.
    private const string Russian = @"э{2,}|а{3,}|м{2,}|э+(?:-э+)+|а+(?:-а+)+|м+(?:-м+)+|хм+|э";

    private static readonly string[] English = ["um", "umm", "uh", "uhh", "er", "err", "ah", "ahh", "hmm", "hm", "mm", "mmm"];

    // \b does not work with Cyrillic hyphens, so word edges are explicit lookarounds.
    private static readonly Regex Pattern = new(
        "(?<![а-яёa-z-])(?:" + Russian + "|" + string.Join('|', English.OrderByDescending(w => w.Length).Select(Regex.Escape)) + ")(?![а-яёa-z-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const string Marker = "\u2063";

    // Only at a sentence start: "…стоит. А-а, ещё" → "…стоит. Ещё"; mid-sentence fillers do not count.
    [GeneratedRegex(@"(?<=(?:^|[.!?…])\s*)\u2063[\s,;:—–-]*(\p{Ll})")] private static partial Regex CapitalAfterMarker();

    [GeneratedRegex(@"^[\s,;:—–-]+")] private static partial Regex LeadingPunct();
    [GeneratedRegex(@"[,;]\s*[,;]")] private static partial Regex DoubledComma();
    [GeneratedRegex(@"\s[,;]\s")] private static partial Regex LoneComma();
    [GeneratedRegex(@"\s+([.!?…])")] private static partial Regex SpaceBeforeEnd();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex ManySpaces();
    [GeneratedRegex("[а-яёa-z0-9]", RegexOptions.IgnoreCase)] private static partial Regex AnyWordChar();

    // Words a letter "А" legitimately follows: "пункт А", "витамин А", "класс А" (stems, any case form).
    private static readonly string[] LetterNouns =
    [
        "букв", "пункт", "вариант", "план", "класс", "групп", "корпус", "литер", "витамин", "категори", "тип",
        "блок", "формат", "точк", "сторон", "отдел", "строк", "столб", "колонк", "раздел", "приложени",
    ];

    [GeneratedRegex(@"(?<prev>\S+)\s+А(?![\p{L}\p{Nd}-])(?=\s+\p{Ll}|\s*[,;.!?…]|\s*$)")] private static partial Regex CapitalA();

    /// <summary>
    /// "пользователь А говорит" → "пользователь говорит": GigaAM writes a hesitation "а-а" as a capital
    /// "А" mid-sentence. Kept at a sentence start, after a number and after "пункт", "вариант", "витамин"…
    /// </summary>
    private static string RemoveHesitationA(string text) => !text.Contains(" А", StringComparison.Ordinal) ? text
        : CapitalA().Replace(text, m =>
        {
            var prev = m.Groups["prev"].Value;
            var word = prev.TrimEnd(',', ';', ':', '»', '"', ')').ToLowerInvariant();
            var keep = prev.Length == 0 || ".!?…".Contains(prev[^1], StringComparison.Ordinal) || char.IsDigit(prev[^1])
                || LetterNouns.Any(n => word.StartsWith(n, StringComparison.Ordinal));
            return keep ? m.Value : prev;
        });

    public static string Remove(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        text = RemoveHesitationA(text);

        // A capitalised filler leaves a marker, so the next word can take over its capital.
        var result = Pattern.Replace(text, m => char.IsUpper(m.Value[0]) ? Marker : "");
        if (result == text)
        {
            return result;
        }

        result = CapitalAfterMarker().Replace(result, m => m.Groups[1].Value.ToUpperInvariant());
        result = result.Replace(Marker, "", StringComparison.Ordinal);

        // Tidy punctuation left behind: "Э-э, запусти" → ", запусти" → "запусти".
        result = LeadingPunct().Replace(result, "");
        result = DoubledComma().Replace(result, ",");
        result = LoneComma().Replace(result, " ");
        result = SpaceBeforeEnd().Replace(result, "$1");
        result = ManySpaces().Replace(result, " ").Trim();

        if (result.Length > 0 && !AnyWordChar().IsMatch(result))
        {
            return ""; // only punctuation remained: "Mmm." → ""
        }

        var startedUpper = text[..Math.Min(5, text.Length)].Where(char.IsLetter).Any(char.IsUpper);
        if (result.Length > 0 && startedUpper && char.IsLetter(result[0]) && char.IsLower(result[0]))
        {
            result = char.ToUpperInvariant(result[0]) + result[1..];
        }

        return result;
    }
}

/// <summary>"500 ₽" → "500 рублей", agreeing with the number before the sign.</summary>
public static partial class Currency
{
    [GeneratedRegex(@"(?:(\d+(?:[  ]\d{3})*(?:[.,]\d+)?)[  ]?)?₽")]
    private static partial Regex RublePattern();

    public static string SpellOut(string text) =>
        !text.Contains('₽', StringComparison.Ordinal) ? text : RublePattern().Replace(text, m =>
        {
            var number = m.Groups[1].Success ? m.Groups[1].Value : null;
            var word = RubleWord(number);
            return number is null ? word : $"{number} {word}";
        });

    private static string RubleWord(string? number)
    {
        if (number is null)
        {
            return "рублях"; // "цена в ₽" → "цена в рублях"
        }

        if (number.Contains(',', StringComparison.Ordinal) || number.Contains('.', StringComparison.Ordinal))
        {
            return "рубля"; // fractional amounts take the genitive singular
        }

        var n = long.Parse(number.Replace(" ", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal),
            CultureInfo.InvariantCulture);
        if (n % 100 is >= 11 and <= 14)
        {
            return "рублей";
        }

        return (n % 10) switch
        {
            1 => "рубль",
            >= 2 and <= 4 => "рубля",
            _ => "рублей",
        };
    }
}

/// <summary>
/// "точка" before a file extension: "Точка MP4" → ".mp4", "index точка html" → "index.html" (glued only
/// after a Latin file name; "файл точка пдф" → "файл .pdf").
/// </summary>
public static partial class FileExtensions
{
    [GeneratedRegex(@"(?<![\p{L}\p{Nd}])(?<name>[A-Za-z][\w-]*\s+)?точка\s+\.?(?<ext>mp4|mp3|html|js|ts|py|json|md|txt|exe|png|jpg|pdf|docx|xlsx|csv|zip|log)(?![\p{L}\p{Nd}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Spoken();

    public static string Apply(string text) => !text.Contains("точка", StringComparison.OrdinalIgnoreCase) ? text
        : Spoken().Replace(text, m => (m.Groups["name"].Success ? m.Groups["name"].Value.TrimEnd() : "") + "." + m.Groups["ext"].Value.ToLowerInvariant());
}

/// <summary>
/// Long dashes become hyphens (owner: people do not type "—", it reads as machine text):
/// "1—2" → "1-2", " — " → " - ", any other "—"/"–" → "-".
/// </summary>
public static partial class Dashes
{
    [GeneratedRegex(@"(?<=\d)\s*[—–]\s*(?=\d)")] private static partial Regex BetweenDigits();
    [GeneratedRegex(@"[—–]")] private static partial Regex AnyDash();

    public static string Normalize(string text) =>
        text.AsSpan().IndexOfAny('—', '–') < 0 ? text : AnyDash().Replace(BetweenDigits().Replace(text, "-"), "-");
}

/// <summary>
/// Parakeet's "&lt;unk&gt;" token: it has no "ё", so inside a Cyrillic word or at the end of a short one it
/// stands for "ё" ("ч&lt;unk&gt;" → "чё", "бо&lt;unk&gt;к" → "боёк"); anywhere else it is dropped
/// ("&lt;unk&gt;Поговорим&lt;unk&gt;." → "Поговорим.").
/// </summary>
public static partial class UnknownToken
{
    private const int ShortWord = 4; // "чё", "всё", "ещё", "твоё"; not "Поговорим<unk>"

    [GeneratedRegex(@"(?<=[а-яё])<unk>(?=[а-яё])", RegexOptions.IgnoreCase)] private static partial Regex InsideWord();
    [GeneratedRegex(@"(?<![а-яё])([а-яё]{1,3})<unk>(?![\p{L}\p{Nd}])", RegexOptions.IgnoreCase)] private static partial Regex EndOfShortWord();
    [GeneratedRegex(@"\s*<unk>\s*")] private static partial Regex Token();
    [GeneratedRegex(@"\s+([.,!?;:…])")] private static partial Regex SpaceBeforePunct();

    public static string Remove(string text)
    {
        if (!text.Contains("<unk>", StringComparison.Ordinal))
        {
            return text;
        }

        text = InsideWord().Replace(text, "ё");
        text = EndOfShortWord().Replace(text, m => m.Groups[1].Value.Length < ShortWord ? m.Groups[1].Value + "ё" : m.Value);
        text = Token().Replace(text, m => m.Index == 0 || m.Index + m.Length == text.Length ? "" : " ");
        return SpaceBeforePunct().Replace(text, "$1").Trim();
    }
}

/// <summary>Inserts the space a punctuation-aware model glues away: "Привет.Что" → "Привет. Что".</summary>
public static partial class SentenceSpacing
{
    // A letter, but not a digit or underscore — decimals and versions are never split.
    private const string Letter = @"[^\W\d_]";

    [GeneratedRegex("([?!…]+)(" + Letter + ")")] private static partial Regex StrongEnd();

    // A full stop starts a sentence only after two letters and before a capital: protects "т.е.",
    // "U.S.A.", "3.14", "github.com", "vox.log".
    [GeneratedRegex("(" + Letter + Letter + @"\.+)(\p{Lu})")] private static partial Regex DotEnd();

    [GeneratedRegex("([,;:])(" + Letter + ")")] private static partial Regex Middle();

    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = StrongEnd().Replace(text, m => $"{m.Groups[1].Value} {m.Groups[2].Value.ToUpperInvariant()}");
        text = DotEnd().Replace(text, m => $"{m.Groups[1].Value} {m.Groups[2].Value.ToUpperInvariant()}");
        return Middle().Replace(text, "$1 $2");
    }
}

/// <summary>Separator handling between consecutive dictations.</summary>
public static class Spacing
{
    /// <summary>Always one leading space so dictations never glue ("Привет.Как дела").</summary>
    public static string WithLeadingSpace(string text) =>
        string.IsNullOrEmpty(text) || text.StartsWith(' ') ? text : " " + text;

    /// <summary>
    /// Split one leading space off. The Chrome/Edge address bar trims pasted leading whitespace,
    /// so the separator is typed as a key press and only the body is pasted.
    /// </summary>
    public static (string Separator, string Body) SplitLeadingSeparator(string text) =>
        text.StartsWith(' ') ? (" ", text[1..]) : ("", text);
}

/// <summary>Applies the user's own corrections ("кубернетис" → "Kubernetes").</summary>
public static class UserCorrections
{
    private const string WordPunct = ".,!?;:—–-\"'«»()[]{}…";

    public static string Apply(string text, IReadOnlyDictionary<string, string> corrections)
    {
        if (corrections.Count == 0 || string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Phrases learned from a term the model split ("zena koder" → "Zencoder"): whole-phrase match.
        foreach (var (source, correct) in corrections.Where(c => c.Key.Contains(' ', StringComparison.Ordinal)))
        {
            var pattern = @"(?<![\p{L}\p{Nd}])" + string.Join(@"\s+", source.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Regex.Escape))
                + @"(?![\p{L}\p{Nd}])";
            text = Regex.Replace(text, pattern, correct.Replace("$", "$$", StringComparison.Ordinal), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        // Case forms of each entry: a learned pair is kept as the user fixed it ("керила" → "Кирилла"),
        // so its base form ("керил" → "Кирилл") is worked out here and declined too.
        var declined = new Dictionary<string, (string Source, string Correct, string Ending)>();
        foreach (var (source, correct) in corrections)
        {
            var (baseSource, baseCorrect) = Declension.BaseForm(source, correct);
            if (baseSource != source && !corrections.ContainsKey(baseSource))
            {
                declined.TryAdd(baseSource, (baseSource, baseCorrect, ""));
            }

            foreach (var (s, c) in new[] { (source, correct), (baseSource, baseCorrect) }.Distinct())
            {
                foreach (var (form, ending) in Declension.Forms(s))
                {
                    if (!corrections.ContainsKey(form))
                    {
                        declined.TryAdd(form, (s, c, ending));
                    }
                }
            }
        }

        var words = text.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var stripped = word.Trim(WordPunct.ToCharArray());
            var key = stripped.ToLowerInvariant();
            if (!corrections.TryGetValue(key, out var replacement)
                && !(declined.TryGetValue(key, out var d) && Declension.Inflect(d.Correct, d.Source, d.Ending) is { } inflected
                     && (replacement = inflected) is not null))
            {
                continue;
            }

            var prefix = stripped.Length > 0 ? word[..word.IndexOf(stripped, StringComparison.Ordinal)] : "";
            var suffix = stripped.Length > 0 ? word[(prefix.Length + stripped.Length)..] : "";
            if (stripped.Length > 0 && char.IsUpper(stripped[0]) && replacement.Length > 0 && char.IsLower(replacement[0]))
            {
                replacement = char.ToUpperInvariant(replacement[0]) + replacement[1..];
            }

            words[i] = prefix + replacement + suffix;
        }

        return string.Join(' ', words);
    }
}
