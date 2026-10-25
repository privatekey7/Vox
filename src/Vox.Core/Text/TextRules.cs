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

    [GeneratedRegex(@"\G[  ]+плюс[-  ]плюс(?![\p{L}\p{Nd}])", RegexOptions.IgnoreCase)]
    private static partial Regex SpokenPlus();

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
                if (Phrase(text, words, i, n) is { } key && Lookup(phrases, key, text, words[i + n - 1]) is { } entry
                    && Replacement(entry, words[i].Value, StartsSentence(text, words[i].Index)) is { } replacement)
                {
                    result.Append(text, copied, words[i].Index - copied).Append(replacement);
                    copied = words[i + n - 1].Index + words[i + n - 1].Length;
                    if (replacement.EndsWith('+'))
                    {
                        // The model's own tail: "нотпад++" → "Notepad++", not "Notepad++++"; "Си-++" → "C++".
                        copied += PlusTail(text, copied);

                        // "нотпад плюс-плюс": the source took "нотпад", the rest is the "++" said aloud.
                        if (SpokenPlus().Match(text, copied) is { Success: true } plus)
                        {
                            copied += plus.Length;
                            i = words.Count(w => w.Index < copied) - n;
                        }
                    }

                    matched = n;
                }
            }

            i += Math.Max(matched, 1);
        }

        return copied == 0 ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    // "Си++": a source ending in "+" ("си++" → C++) matches only a word the model wrote with a "+" after it
    // ("Си-++" too), and not one glued to a following word ("c+d").
    private static Entry? Lookup(Dictionary<string, Entry> phrases, string key, string text, Match last)
    {
        var end = last.Index + last.Length;
        return phrases.TryGetValue(key, out var entry) ? entry
            : PlusTail(text, end) is > 0 and var tail && !(end + tail < text.Length && char.IsLetterOrDigit(text[end + tail]))
                && phrases.TryGetValue(key + "+", out entry) ? entry
            : null;
    }

    /// <summary>Length of the "+" run at <paramref name="index"/>, with a hyphen before it ("-++"); 0 without a "+".</summary>
    internal static int PlusTail(string text, int index)
    {
        var at = index < text.Length - 1 && text[index] == '-' && text[index + 1] == '+' ? index + 1 : index;
        var end = at;
        while (end < text.Length && text[end] == '+')
        {
            end++;
        }

        return end == at ? 0 : end - index;
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
    // A capital on a Latin spelling is the model's, not the speaker's: kept only at a sentence start
    // ("Это Frount End" → "Это фронтенд").
    private static string? Replacement(Entry entry, string spoken, bool sentenceStart)
    {
        var replacement = entry.Ending is null
            ? entry.Replacement
            : Declension.Inflect(entry.Replacement, entry.LastSourceWord, entry.Ending, entry.MinLength);
        return replacement is { Length: > 0 } && char.IsUpper(spoken[0]) && char.IsLower(replacement[0]) && !HasLatin(replacement[..1])
            && (sentenceStart || !HasLatin(spoken))
            ? char.ToUpperInvariant(replacement[0]) + replacement[1..]
            : replacement;
    }

    private static bool StartsSentence(string text, int index)
    {
        var before = text.AsSpan(0, index).TrimEnd();
        return before.IsEmpty || ".!?…".Contains(before[^1]);
    }

    private static bool HasLatin(string s) => s.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');

    private static (Dictionary<string, Entry>, int) BuildIndex()
    {
        var pairs = LoadPairs();
        var phrases = new Dictionary<string, Entry>();
        foreach (var pair in pairs)
        {
            var source = pair[0].ToLowerInvariant();
            source = source.EndsWith('+') ? source.TrimEnd('+') + "+" : source; // see Lookup
            phrases[source] = new Entry(pair[1], source.Split(' ')[^1], null);
        }

        var excluded = LoadResource<string[]>("Vox.Core.no-declension.json").ToHashSet();
        var forms = new Dictionary<string, Entry>();
        var ambiguous = new HashSet<string>();
        foreach (var pair in pairs.Where(p => !p[0].EndsWith('+')))
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
    [GeneratedRegex(@"[,;]\s*([.!?…]|$)")] private static partial Regex CommaBeforeEnd();
    [GeneratedRegex(@"^\s*(?:ну|вот|то есть|кстати|короче|в общем|значит|наверное|кажется|видимо)\s*,", RegexOptions.IgnoreCase)]
    private static partial Regex IntroWord();
    [GeneratedRegex(@"(?<=[.!?…])\s*(?:\.{3}|…)(?=\s|$)")] private static partial Regex EllipsisAfterEnd();
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

    // A letter, not a hesitation: "часть А и Б", "выбираем А." (end of the phrase).
    [GeneratedRegex(@"^(?:\s+(?:и|или)\s+Б(?![\p{L}\p{Nd}-])|\s*[.!?…]|\s*$)")] private static partial Regex LetterAfterA();

    /// <summary>
    /// "пользователь А говорит" → "пользователь а говорит": GigaAM writes a hesitation "а-а" or the
    /// conjunction "а" as a capital "А" mid-sentence. It is lowered, never removed: "на Piton А новая"
    /// keeps its "а". Kept as is at a sentence start, at the end of the phrase, before "и Б", after a
    /// number and after "пункт", "вариант", "витамин"…
    /// </summary>
    private static string LowerHesitationA(string text) => !text.Contains(" А", StringComparison.Ordinal) ? text
        : CapitalA().Replace(text, m =>
        {
            var prev = m.Groups["prev"].Value;
            var word = prev.TrimEnd(',', ';', ':', '»', '"', ')').ToLowerInvariant();
            var keep = prev.Length == 0 || ".!?…".Contains(prev[^1], StringComparison.Ordinal) || char.IsDigit(prev[^1])
                || LetterNouns.Any(n => word.StartsWith(n, StringComparison.Ordinal))
                || LetterAfterA().IsMatch(text.AsSpan(m.Index + m.Length));
            return keep ? m.Value : m.Value[..^1] + "а";
        });

    public static string Remove(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        text = LowerHesitationA(text);

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
        // "если мы, а-а, уменьшим" → "если мы уменьшим", but "в словарь, э-э, ну, не факт" keeps "в словарь, ну,".
        result = DoubledComma().Replace(result, m => IntroWord().IsMatch(result.AsSpan(m.Index + m.Length)) ? ", " : " ");
        result = EllipsisAfterEnd().Replace(result, ""); // "Угу. А-а-а... Как бы" → "Угу. Как бы"
        result = CommaBeforeEnd().Replace(result, "$1"); // "если мы, а-а." → "если мы."
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

/// <summary>"1600 МГ" → "1600 МГц": the model drops "ц" after a number; lower-case "мг" (milligrams) stays.</summary>
/// <summary>
/// A word of mixed alphabets ("индoint") is read in one: the Latin letters spelled in Cyrillic or the other way
/// round, and kept only if the dictionary knows the result ("индоинт" → "эндпоинт").
/// </summary>
public static partial class MixedScript
{
    [GeneratedRegex(@"\p{L}+")] private static partial Regex Word();

    private static readonly Dictionary<string, string> ToCyrillic = new()
    {
        ["sh"] = "ш", ["ch"] = "ч", ["zh"] = "ж", ["ts"] = "ц", ["ya"] = "я", ["yu"] = "ю",
        ["a"] = "а", ["b"] = "б", ["c"] = "к", ["d"] = "д", ["e"] = "е", ["f"] = "ф", ["g"] = "г", ["h"] = "х",
        ["i"] = "и", ["j"] = "дж", ["k"] = "к", ["l"] = "л", ["m"] = "м", ["n"] = "н", ["o"] = "о", ["p"] = "п",
        ["q"] = "к", ["r"] = "р", ["s"] = "с", ["t"] = "т", ["u"] = "у", ["v"] = "в", ["w"] = "в", ["x"] = "кс",
        ["y"] = "й", ["z"] = "з",
    };

    private static readonly Dictionary<char, string> ToLatin = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ё'] = "e", ['ж'] = "zh",
        ['з'] = "z", ['и'] = "i", ['й'] = "y", ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o",
        ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "h", ['ц'] = "ts",
        ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e", ['ю'] = "yu",
        ['я'] = "ya",
    };

    public static string Fix(string text) => Word().Replace(text, m =>
    {
        var word = m.Value;
        if (!word.Any(IsCyrillic) || !word.Any(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z'))
        {
            return word;
        }

        foreach (var candidate in new[] { Cyrillic(word), Latin(word) })
        {
            if (candidate is null)
            {
                continue;
            }

            var known = FuzzyTerms.Apply(Terms.Replace(candidate));
            if (known != candidate)
            {
                return known;
            }
        }

        return word;
    });

    private static bool IsCyrillic(char c) => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё';

    private static string? Cyrillic(string word)
    {
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < word.Length;)
        {
            if (IsCyrillic(word[i]))
            {
                result.Append(word[i++]);
                continue;
            }

            var lower = char.ToLowerInvariant(word[i]);
            var two = i + 1 < word.Length ? $"{lower}{char.ToLowerInvariant(word[i + 1])}" : null;
            var (letters, length) = two is not null && ToCyrillic.TryGetValue(two, out var d) ? (d, 2)
                : ToCyrillic.TryGetValue(lower.ToString(), out var one) ? (one, 1) : (null, 0);
            if (letters is null)
            {
                return null;
            }

            result.Append(char.IsUpper(word[i]) ? char.ToUpperInvariant(letters[0]) + letters[1..] : letters);
            i += length;
        }

        return result.ToString();
    }

    private static string? Latin(string word)
    {
        var result = new System.Text.StringBuilder();
        foreach (var c in word)
        {
            if (!IsCyrillic(c))
            {
                result.Append(c);
            }
            else if (ToLatin.TryGetValue(char.ToLowerInvariant(c), out var letters))
            {
                result.Append(char.IsUpper(c) && letters.Length > 0 ? char.ToUpperInvariant(letters[0]) + letters[1..] : letters);
            }
            else
            {
                return null;
            }
        }

        return result.ToString();
    }
}

/// <summary>"Сити Arbitrum" → "сети Arbitrum": the model hears "сети" before a blockchain as "Сити".</summary>
public static partial class Networks
{
    [GeneratedRegex(@"(?<![\p{L}\p{Nd}])[Сс]ити(?=[  ]+(?:Arbitrum|Optimism|Polygon|Solana|Ethereum|Base|TON|TRON|Cardano|Celestia|Monad|Avalanche|BNB|Bitcoin|Sui|Aptos|Linea|zkSync|Scroll|Blast|Starknet|Evmos|Cosmos|Near|NEAR)(?![\p{L}\p{Nd}]))")]
    private static partial Regex City();

    public static string Fix(string text) =>
        text.Contains("ити", StringComparison.Ordinal) ? City().Replace(text, m => m.Value[0] == 'С' && IsSentenceStart(text, m.Index) ? "Сети" : "сети") : text;

    private static bool IsSentenceStart(string text, int index)
    {
        var before = text.AsSpan(0, index).TrimEnd();
        return before.IsEmpty || ".!?…".Contains(before[^1]);
    }
}

public static partial class Frequency
{
    [GeneratedRegex(@"(?<=\d)\s*([МГ]Г)(?!\p{L})")]
    private static partial Regex Unit();

    public static string Fix(string text) =>
        text.Contains('Г', StringComparison.Ordinal) ? Unit().Replace(text, m => " " + m.Groups[1].Value + "ц") : text;
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
    [GeneratedRegex(@"(?<![\p{L}\p{Nd}])(?<name>(?:[A-Za-z]|(?<=[\p{L}\p{Nd},;:]\s+)(?-i:[А-ЯЁ])(?=[а-яё]))[\w-]*\s+)?точка\s+\.?(?<ext>mp4|mp3|html|js|ts|py|json|md|txt|exe|png|jpg|pdf|docx|xlsx|csv|zip|log)(?![\p{L}\p{Nd}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Spoken();

    public static string Apply(string text) => !text.Contains("точка", StringComparison.OrdinalIgnoreCase) ? text
        : Spoken().Replace(text, m => (m.Groups["name"].Success ? m.Groups["name"].Value.TrimEnd() : "") + "." + m.Groups["ext"].Value.ToLowerInvariant());

    // A model's ".json" glued to a file name: "terms.gson", "terms.Gon", "config.джейсон" (before Terms makes it " JSON").
    [GeneratedRegex(@"(?<=[\p{L}\p{Nd}_-])\.(?:gson|gon|jeson|джейсон|джсон|джесон)(?![\p{L}\p{Nd}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GluedJson();

    /// <summary>"terms.gson", "config.джейсон" → ".json"; runs before <see cref="Terms"/>.</summary>
    public static string FixGlued(string text) => GluedJson().Replace(text, ".json");
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
        // A target with a line break or an invisible character is never pasted, even one already in the database
        // (security audit M5: a pair from someone else's transfer file); the word loop below checks it too.
        foreach (var (source, correct) in corrections.Where(c => c.Key.Contains(' ', StringComparison.Ordinal) && SafeText.HasNoHiddenChars(c.Value)))
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
            if ((!corrections.TryGetValue(key, out var replacement)
                 && !(declined.TryGetValue(key, out var d) && Declension.Inflect(d.Correct, d.Source, d.Ending) is { } inflected
                      && (replacement = inflected) is not null))
                || !SafeText.HasNoHiddenChars(replacement))
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
