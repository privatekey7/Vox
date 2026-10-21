using System.Globalization;
using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// Inverse text normalisation for Russian: spoken number-words → digits.
/// </summary>
/// <remarks>
/// Parakeet often emits half-normalised numbers ("30 шесть", "2 тысячи двадцать пять"), so
/// digits and number-words are parsed together. Each element occupies decimal slots
/// (1 = ones, 2 = tens, 3 = hundreds); an element may only extend the current number if its
/// highest slot is below the lowest slot already filled. Otherwise a new number starts —
/// "одиннадцать тридцать" is 11 and 30, not 41.
/// </remarks>
public static partial class Itn
{
    private const string TrailingPunct = ".,!?;:…";

    private static readonly Dictionary<string, int> Ones = new()
    {
        ["ноль"] = 0, ["нуль"] = 0, ["один"] = 1, ["одна"] = 1, ["одно"] = 1, ["два"] = 2,
        ["две"] = 2, ["три"] = 3, ["четыре"] = 4, ["пять"] = 5, ["шесть"] = 6, ["семь"] = 7,
        ["восемь"] = 8, ["девять"] = 9, ["десять"] = 10, ["одиннадцать"] = 11,
        ["двенадцать"] = 12, ["тринадцать"] = 13, ["четырнадцать"] = 14, ["пятнадцать"] = 15,
        ["шестнадцать"] = 16, ["семнадцать"] = 17, ["восемнадцать"] = 18, ["девятнадцать"] = 19,
    };

    private static readonly Dictionary<string, int> Tens = new()
    {
        ["двадцать"] = 20, ["тридцать"] = 30, ["сорок"] = 40, ["пятьдесят"] = 50,
        ["шестьдесят"] = 60, ["семьдесят"] = 70, ["восемьдесят"] = 80, ["девяносто"] = 90,
    };

    private static readonly Dictionary<string, int> Hundreds = new()
    {
        ["сто"] = 100, ["двести"] = 200, ["триста"] = 300, ["четыреста"] = 400,
        ["пятьсот"] = 500, ["шестьсот"] = 600, ["семьсот"] = 700, ["восемьсот"] = 800,
        ["девятьсот"] = 900,
    };

    private static readonly Dictionary<string, long> Multipliers = new()
    {
        ["тысяча"] = 1_000, ["тысячи"] = 1_000, ["тысяч"] = 1_000,
        ["миллион"] = 1_000_000, ["миллиона"] = 1_000_000, ["миллионов"] = 1_000_000,
        ["миллиард"] = 1_000_000_000, ["миллиарда"] = 1_000_000_000, ["миллиардов"] = 1_000_000_000,
    };

    private static readonly HashSet<string> PronounOnes = ["один", "одна", "одно"];
    private static readonly HashSet<string> LoneMultipliers = ["тысяча"];

    [GeneratedRegex(
        @"^(перв|втор|трет|четв[её]рт|пят|шест|седьм|восьм|девят|десят|\w+надцат|двадцат" +
        @"|тридцат|сороков|\w+десят|девяност|\w*сот|тысячн|миллионн)" +
        @"(ый|ой|ий|ая|ое|ья|ье|ьи|ого|ему|ому|ым|ом|ую|ых|ыми|ые|ьего|ьему|ьим|ьем|ью|ьей|ьих)$")]
    private static partial Regex OrdinalRegex();

    private sealed record Piece(string Token, long Value, int Hi, int Lo, bool IsDigit, bool IsMultiplier);

    /// <summary>Convert number-words to digits. Languages other than Russian pass through.</summary>
    public static string Apply(string text, string lang = "ru")
    {
        if (string.IsNullOrWhiteSpace(text) || lang != "ru")
        {
            return text;
        }

        var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var output = new List<string>(tokens.Length);
        var i = 0;
        while (i < tokens.Length)
        {
            var span = new List<Piece>();
            while (i < tokens.Length && ParsePiece(tokens[i]) is { } piece)
            {
                span.Add(piece);
                i++;
                if (SplitTrailingPunct(piece.Token).Trailing.Length > 0)
                {
                    break; // punctuation closes the number
                }
            }

            if (span.Count == 0)
            {
                output.Add(tokens[i]);
                i++;
                continue;
            }

            var nextWord = i < tokens.Length ? SplitTrailingPunct(tokens[i]).Core.ToLowerInvariant() : "";
            if (OrdinalRegex().IsMatch(nextWord))
            {
                output.AddRange(span.Select(p => p.Token)); // "двадцать шестой" stays words
                continue;
            }

            foreach (var number in Group(span))
            {
                output.AddRange(Render(number));
            }
        }

        return string.Join(' ', output);
    }

    private static (string Core, string Trailing) SplitTrailingPunct(string token)
    {
        var core = token.TrimEnd(TrailingPunct.ToCharArray());
        return (core, token[core.Length..]);
    }

    /// <summary>(highest, lowest) decimal slot of a sub-1000 value; teens fill tens and ones.</summary>
    private static (int Hi, int Lo) Slots(long value)
    {
        if (value == 0)
        {
            return (1, 1);
        }

        long[] digits = [value / 100, value / 10 % 10, value % 10]; // slots 3, 2, 1
        var hi = 3 - Array.FindIndex(digits, d => d != 0);
        var lo = 1 + Array.FindIndex(digits.Reverse().ToArray(), d => d != 0);
        if (digits[1] == 1)
        {
            lo = 1; // 10..19 is a single word occupying tens + ones
        }

        return (hi, lo);
    }

    private static Piece? ParsePiece(string token)
    {
        var word = SplitTrailingPunct(token).Core.ToLowerInvariant();
        if (word.Length is > 0 and <= 3 && word.All(char.IsAsciiDigit) && (word == "0" || word[0] != '0'))
        {
            var value = long.Parse(word, CultureInfo.InvariantCulture);
            var (hi, lo) = Slots(value);
            return new Piece(token, value, hi, lo, IsDigit: true, IsMultiplier: false);
        }

        if (Multipliers.TryGetValue(word, out var mult))
        {
            return new Piece(token, mult, 0, 0, IsDigit: false, IsMultiplier: true);
        }

        foreach (var table in new[] { Ones, Tens, Hundreds })
        {
            if (table.TryGetValue(word, out var v))
            {
                var (hi, lo) = Slots(v);
                return new Piece(token, v, hi, lo, IsDigit: false, IsMultiplier: false);
            }
        }

        return null;
    }

    /// <summary>Split one contiguous run of number tokens into separate numbers.</summary>
    private static List<List<Piece>> Group(List<Piece> pieces)
    {
        var numbers = new List<List<Piece>>();
        var current = new List<Piece>();
        var freeBelow = 4; // next element's highest slot must be below this
        var lastMult = long.MaxValue;

        foreach (var p in pieces)
        {
            bool joins = p.IsMultiplier
                ? p.Value < lastMult && !(current.Count > 0 && current[^1].IsMultiplier)
                : current.Count > 0 && p.Hi < freeBelow && !(p.IsDigit && current[^1].IsDigit); // never merge "900 55"

            if (current.Count > 0 && !joins)
            {
                numbers.Add(current);
                current = [];
                freeBelow = 4;
                lastMult = long.MaxValue;
            }

            current.Add(p);
            if (p.IsMultiplier)
            {
                freeBelow = 4;
                lastMult = p.Value;
            }
            else
            {
                freeBelow = p.Lo;
            }
        }

        if (current.Count > 0)
        {
            numbers.Add(current);
        }

        return numbers;
    }

    /// <summary>Digits for a parsed number, or the original tokens when converting would hurt.</summary>
    private static IEnumerable<string> Render(List<Piece> pieces)
    {
        var words = pieces.Where(p => !p.IsDigit).ToList();
        var plainWords = words.Where(p => !p.IsMultiplier).ToList();
        var first = SplitTrailingPunct(pieces[0].Token).Core.ToLowerInvariant();
        var keepOriginal =
            words.Count == 0 // pure digit run, e.g. a phone number
            || (plainWords.Count == 0 && pieces.Any(p => p.IsDigit)) // "150 тысяч"
            || (pieces.Count == 1 && PronounOnes.Contains(first)) // "один из"
            || (pieces.Count == 1 && pieces[0].IsMultiplier && !LoneMultipliers.Contains(first));
        if (keepOriginal)
        {
            return pieces.Select(p => p.Token);
        }

        long total = 0, current = 0;
        foreach (var p in pieces)
        {
            if (p.IsMultiplier)
            {
                total += (current == 0 ? 1 : current) * p.Value;
                current = 0;
            }
            else
            {
                current += p.Value;
            }
        }

        var trailing = SplitTrailingPunct(pieces[^1].Token).Trailing;
        return [(total + current).ToString(CultureInfo.InvariantCulture) + trailing];
    }
}
