using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// Is a word Russian at all, in any form: the Hunspell dictionary ru_RU by Alexander I. Lebedev (BSD,
/// Data/ru_RU.LICENSE.md), 146 000 stems with suffix rules, read on first use (~0.1 s, ~20 MB). Unlike the
/// 48 000 frequent forms of <see cref="SelfLearning.IsCommonWord"/>, it knows rare words too ("курсор", "аргон"):
/// what is not here is a name or a word heard in Russian ("крузаде", "вистер").
/// </summary>
public static class RussianWords
{
    private static readonly Lazy<Lexicon> Default = new(Load);

    /// <summary>A Russian word form (ё as е, any case); a hyphenated word is not one, the caller checks its parts.</summary>
    public static bool Contains(string word) => Default.Value.Stems(word).Any();

    /// <summary>A form of a name only, never of an ordinary word: "Кирилла", "Москве" (the dictionary spells them
    /// with a capital); "Вера" is also "вера", so it is not.</summary>
    public static bool IsName(string word)
    {
        var lexicon = Default.Value;
        var stems = lexicon.Stems(word).ToList();
        return stems.Count > 0 && stems.All(lexicon.IsName);
    }

    private sealed record Rule(char Flag, string Strip, Regex? Condition);

    private sealed class Lexicon(
        Dictionary<string, string> stems, HashSet<string> names, Dictionary<string, List<Rule>> rules, int longestEnding)
    {
        public bool IsName(string stem) => names.Contains(stem);

        /// <summary>The stems the word is a form of.</summary>
        public IEnumerable<string> Stems(string word)
        {
            var w = Normal(word);
            if (stems.ContainsKey(w))
            {
                yield return w;
            }

            // A form is a stem with its ending swapped by a rule of one of the stem's flags.
            for (var k = 0; k <= Math.Min(longestEnding, w.Length - 1); k++)
            {
                if (!rules.TryGetValue(w[^k..], out var byEnding))
                {
                    continue;
                }

                var root = w[..^k];
                foreach (var rule in byEnding)
                {
                    var stem = root + rule.Strip;
                    if (stems.TryGetValue(stem, out var flags) && flags.Contains(rule.Flag, StringComparison.Ordinal)
                        && (rule.Condition is null || rule.Condition.IsMatch(stem)))
                    {
                        yield return stem;
                    }
                }
            }
        }
    }

    private static string Normal(string word) => word.ToLowerInvariant().Replace('ё', 'е');

    private static Lexicon Load()
    {
        // ru_RU.aff: "SFX <flag> <strip> <ending> <condition>", 0 for an empty strip or ending.
        var rules = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);
        var conditions = new Dictionary<string, Regex>(StringComparer.Ordinal);
        var longest = 0;
        foreach (var line in ReadLines("Vox.Core.ru_RU.aff.gz"))
        {
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 5 || p[0] != "SFX")
            {
                continue;
            }

            var strip = p[2] == "0" ? "" : Normal(p[2]);
            var ending = p[3] == "0" ? "" : Normal(p[3].Split('/')[0]);
            var condition = Normal(p[4]);
            Regex? regex = null;
            if (condition != ".")
            {
                if (!conditions.TryGetValue(condition, out regex))
                {
                    conditions[condition] = regex = new Regex("(?:" + condition + ")$", RegexOptions.CultureInvariant);
                }
            }

            if (!rules.TryGetValue(ending, out var list))
            {
                rules[ending] = list = [];
            }

            list.Add(new Rule(p[1][0], strip, regex));
            longest = Math.Max(longest, ending.Length);
        }

        // ru_RU.dic: a count, then "stem/flags"; a stem twice (ё and е, a capital) keeps the flags of both.
        // A stem spelled only with a capital is a name ("Кирилл/K").
        var stems = new Dictionary<string, string>(150_000, StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var lower = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in ReadLines("Vox.Core.ru_RU.dic.gz").Skip(1))
        {
            var slash = line.IndexOf('/', StringComparison.Ordinal);
            var spelled = slash < 0 ? line : line[..slash];
            var stem = Normal(spelled);
            var flags = slash < 0 ? "" : line[(slash + 1)..];
            stems[stem] = stems.TryGetValue(stem, out var had) ? had + flags : flags;
            (char.IsUpper(spelled[0]) ? names : lower).Add(stem);
        }

        names.ExceptWith(lower);
        return new Lexicon(stems, names, rules, longest);
    }

    private static IEnumerable<string> ReadLines(string resource)
    {
        using var stream = typeof(RussianWords).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Embedded {resource} is missing");
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length > 0)
            {
                yield return line;
            }
        }
    }
}
