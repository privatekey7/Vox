using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>Russian phonetic → English tech term replacements ("гитхаб" → "GitHub").</summary>
public static class Terms
{
    private static readonly Lazy<(Regex Pattern, string Replacement)[]> Rules = new(Load);

    /// <summary>Apply every replacement in dictionary order (later rules see earlier output).</summary>
    public static string Replace(string text)
    {
        foreach (var (pattern, replacement) in Rules.Value)
        {
            text = pattern.Replace(text, _ => replacement);
        }

        return text;
    }

    private static (Regex, string)[] Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Vox.Core.terms.json")
            ?? throw new InvalidOperationException("Embedded terms.json is missing");
        var pairs = JsonSerializer.Deserialize<string[][]>(stream)
            ?? throw new InvalidOperationException("terms.json is empty");
        return pairs
            .Select(p => (new Regex(@"\b" + Regex.Escape(p[0]) + @"\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled), p[1]))
            .ToArray();
    }
}

/// <summary>Removes pure hesitation sounds ("э-э", "ммм", "uh"); meaningful words like "ну" stay.</summary>
public static partial class Fillers
{
    private static readonly string[] Words =
    [
        // ru
        "э-э", "э", "ээ", "эээ", "а-а", "м-м", "мм", "ммм", "хм", "хмм",
        // en
        "um", "umm", "uh", "uhh", "er", "err", "ah", "ahh", "hmm", "hm", "mm", "mmm",
    ];

    // \b does not work with Cyrillic hyphens, so word edges are explicit lookarounds.
    private static readonly Regex Pattern = new(
        "(?<![а-яёa-z])(?:" + string.Join('|', Words.OrderByDescending(w => w.Length).Select(Regex.Escape)) + ")(?![а-яёa-z])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [GeneratedRegex(@"^[\s,;:]+")] private static partial Regex LeadingPunct();
    [GeneratedRegex(@"[,;]\s*[,;]")] private static partial Regex DoubledComma();
    [GeneratedRegex(@"\s[,;]\s")] private static partial Regex LoneComma();
    [GeneratedRegex(@"\s+([.!?…])")] private static partial Regex SpaceBeforeEnd();
    [GeneratedRegex(@"\s{2,}")] private static partial Regex ManySpaces();
    [GeneratedRegex("[а-яёa-z0-9]", RegexOptions.IgnoreCase)] private static partial Regex AnyWordChar();

    public static string Remove(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var result = Pattern.Replace(text, "");
        if (result == text)
        {
            return result;
        }

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

/// <summary>Inserts the space a punctuation-aware model glues away: "Привет.Что" → "Привет. Что".</summary>
public static partial class SentenceSpacing
{
    // A letter, but not a digit or underscore — decimals and versions are never split.
    private const string Letter = @"[^\W\d_]";

    [GeneratedRegex("([?!…]+)(" + Letter + ")")] private static partial Regex StrongEnd();

    // A full stop starts a sentence only after two letters: protects "т.е.", "U.S.A.", "3.14".
    [GeneratedRegex("(" + Letter + Letter + @"\.+)(" + Letter + ")")] private static partial Regex DotEnd();

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

        var words = text.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            var stripped = word.Trim(WordPunct.ToCharArray());
            if (!corrections.TryGetValue(stripped.ToLowerInvariant(), out var replacement))
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
