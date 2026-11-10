using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// A two-word Latin term with one word heard as an English word (owner's test of Parakeet, 10.11): "clot code" →
/// "Claude Code". <see cref="FuzzyTerms"/> leaves two English words alone; here one of them must be the term's word
/// and the other sound like its pair: the same sound skeleton and first letter, at least four letters, no word of
/// another term ("cloud code" stays).
/// </summary>
public static partial class LatinPairs
{
    private const int MinLetters = 4;

    [GeneratedRegex(@"(?<![\p{L}\p{Nd}_.\-/\\@])(?<a>[A-Za-z]+) (?<b>[A-Za-z]+)(?![\p{L}\p{Nd}_\-/\\@]|\.[\p{L}\p{Nd}])")]
    private static partial Regex Pair();

    // Two-word terms of Latin letters by each word, lower case: "code" → ("Claude Code", word 1).
    private static readonly Lazy<Dictionary<string, List<(string Target, int At)>>> ByWord = new(() =>
    {
        var index = new Dictionary<string, List<(string, int)>>(StringComparer.Ordinal);
        foreach (var target in Terms.LoadPairs().Select(p => p[1]).Distinct())
        {
            var parts = target.Split(' ');
            if (parts.Length == 2 && parts.All(p => p.Length >= 2 && p.All(char.IsAsciiLetter)))
            {
                for (var at = 0; at < 2; at++)
                {
                    var key = parts[at].ToLowerInvariant();
                    if (!index.TryGetValue(key, out var list))
                    {
                        index[key] = list = [];
                    }

                    list.Add((target, at));
                }
            }
        }

        return index;
    });

    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Any(char.IsAsciiLetter))
        {
            return text;
        }

        // Overlapping pairs: "run clot code" tries "run clot", then "clot code".
        var result = new System.Text.StringBuilder(text.Length);
        var copied = 0;
        for (var at = 0; Pair().Match(text, at) is { Success: true } m; at = m.Groups["b"].Index)
        {
            if (m.Index < copied)
            {
                continue;
            }

            if (Term(m.Groups["a"].Value, m.Groups["b"].Value) is { } term)
            {
                result.Append(text, copied, m.Index - copied).Append(term);
                copied = m.Index + m.Length;
            }
        }

        return copied == 0 ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    private static string? Term(string a, string b)
    {
        var found = Candidates(b, 1, a).Concat(Candidates(a, 0, b)).Distinct().ToList();
        return found.Count == 1 ? found[0] : null;
    }

    // Terms with `same` as their word number `at` and `other` heard for the other word.
    private static IEnumerable<string> Candidates(string same, int at, string other)
    {
        if (!ByWord.Value.TryGetValue(same.ToLowerInvariant(), out var terms))
        {
            yield break;
        }

        foreach (var (target, position) in terms.Where(t => t.At == at))
        {
            var word = target.Split(' ')[1 - position];
            if (other.Length >= MinLetters && !other.Equals(word, StringComparison.OrdinalIgnoreCase)
                && char.ToLowerInvariant(other[0]) == char.ToLowerInvariant(word[0])
                && !Terms.IsTermForm(other) && !Terms.IsTermForm(char.ToUpperInvariant(other[0]) + other[1..].ToLowerInvariant())
                && FuzzyTerms.Skeleton(other) == FuzzyTerms.Skeleton(word))
            {
                yield return target;
            }
        }
    }
}
