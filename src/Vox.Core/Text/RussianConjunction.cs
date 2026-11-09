using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// The Russian "и" that GigaAM writes in Latin next to Latin words (owner's log 09.11): "Handy and Vox" → "Handy и Vox",
/// a drawn-out "и-и-и" as "and and and and" → "и", a lone "i" → "и". Only in a sentence with Russian words; an English
/// phrase stays: ordinary English words around "and" ("Rock and Roll", "drag and drop") or an English word such as
/// "of" next to them ("Proof of Concept and MVP").
/// </summary>
public static partial class RussianConjunction
{
    // Words that make the Latin words around "and" English speech, not a list of names.
    private static readonly HashSet<string> EnglishGlue = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "of", "to", "in", "on", "at", "by", "for", "from", "with", "without", "is", "are", "was", "be",
        "as", "or", "not", "no", "my", "your", "you", "we", "it", "this", "that", "i", "me", "our", "their", "his", "her",
    };

    // A sentence: up to an end mark followed by a space or the end ("Node.js" does not end one).
    [GeneratedRegex(@".+?(?:[.!?…]+(?=\s|$)|$)", RegexOptions.Singleline)]
    private static partial Regex Sentence();

    // "and and and", "and, and": a drawn-out "и"; the commas the model put between them go too.
    [GeneratedRegex(@"[\s,]*\band(?:[\s,]+and)+\b,?", RegexOptions.IgnoreCase)]
    private static partial Regex RepeatedAnd();

    // "Python and C#", "Python and& C#" (the model glues "&" on).
    [GeneratedRegex(@"(?<=(?<left>" + LatinToken + @") )and&?(?= (?<right>" + LatinToken + "))")]
    private static partial Regex SingleAnd();

    [GeneratedRegex(@"(?<![\p{L}\p{Nd}.'’-])i(?![\p{L}\p{Nd}'’-]|\.[\p{L}\p{Nd}])(?!\s+[A-Za-z])")]
    private static partial Regex LoneI();

    [GeneratedRegex(LatinToken + "$")] private static partial Regex LatinBefore();
    [GeneratedRegex("^" + LatinToken)] private static partial Regex LatinAfter();

    // "Vox", "C#", "Node.js", "3D".
    private const string LatinToken = @"(?<![\p{L}\p{Nd}])[A-Za-z0-9](?:[A-Za-z0-9#+]|[.-](?=[A-Za-z0-9]))*";

    public static string Fix(string text) =>
        text.Contains('a', StringComparison.OrdinalIgnoreCase) || text.Contains('i', StringComparison.Ordinal)
            ? Sentence().Replace(text, m => FixSentence(m.Value))
            : text;

    private static string FixSentence(string sentence)
    {
        if (!sentence.Any(IsCyrillic))
        {
            return sentence;
        }

        sentence = RepeatedAnd().Replace(sentence, m =>
        {
            var start = string.IsNullOrWhiteSpace(sentence[..m.Index]);
            var and = m.Value.TrimStart(' ', ',');
            return (start ? "" : " ") + (char.IsUpper(and[0]) ? "И" : "и");
        });
        sentence = SingleAnd().Replace(sentence, m =>
            IsListOfNames(sentence, m.Groups["left"], m.Groups["right"]) ? "и" : m.Value);
        return LoneI().Replace(sentence, "и");
    }

    private static bool IsListOfNames(string sentence, Group left, Group right)
    {
        if (!IsName(left.Value) || !IsName(right.Value)
            || (FuzzyTerms.IsPlainEnglish(left.Value) && FuzzyTerms.IsPlainEnglish(right.Value)))
        {
            return false;
        }

        return !LatinRun(sentence, left.Index, right.Index + right.Length).Any(EnglishGlue.Contains);
    }

    // A name starts with a capital or a digit, or has a sign in it ("C#", "C++"); "drop", "design" are words.
    private static bool IsName(string token) =>
        char.IsAsciiLetterUpper(token[0]) || char.IsAsciiDigit(token[0]) || token.AsSpan().IndexOfAny('#', '+') >= 0;

    // The Latin words joined by single spaces before "start" and after "end": the phrase "and" stands in.
    private static IEnumerable<string> LatinRun(string sentence, int start, int end)
    {
        while (start > 1 && sentence[start - 1] == ' ' && LatinBefore().Match(sentence[..(start - 1)]) is { Success: true } before)
        {
            yield return before.Value;
            start = before.Index;
        }

        while (end < sentence.Length - 1 && sentence[end] == ' ' && LatinAfter().Match(sentence[(end + 1)..]) is { Success: true } after)
        {
            yield return after.Value;
            end += 1 + after.Length;
        }
    }

    private static bool IsCyrillic(char c) => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё';
}
