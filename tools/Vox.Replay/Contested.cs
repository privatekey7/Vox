using System.Text.Json;
using System.Text.RegularExpressions;
using Vox.Core.Text;

/// <summary>
/// Experiment (tools/llm-fix): words of a Final that no list knows, with the closest terms by sound, for a small
/// LLM to choose from. Known: frequent Russian words, English words (SCOWL), terms.json spellings and their case
/// forms, short words. Contested: an unknown Latin word, a word of mixed alphabets, an unknown Cyrillic word that
/// sounds like a term (0.9, 5+ letters, 4+ sounds: the frequent-word list misses many ordinary forms, and a short
/// skeleton matches anything); neighbours glue into one span.
/// </summary>
internal static partial class Contested
{
    private const int MaxCandidates = 5;
    private const double MinScore = 0.55;
    private const int MaxSpanWords = 3;
    private const double CyrillicScore = 0.9;

    [GeneratedRegex(@"[\p{L}\p{Nd}]+(?:[.\-+][\p{L}\p{Nd}]+)*\+*")] private static partial Regex Word();

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Lazy<(string Target, string[] Skeletons)[]> Pool = new(() =>
        [.. LoadTerms().Values.SelectMany(g => g)
            .GroupBy(t => t.Key)
            .Select(g => (g.Key, g.SelectMany(t => t.Value).Append(g.Key).Select(FuzzyTerms.Skeleton).Where(s => s.Length >= 2).Distinct().ToArray()))]);

    // Cyrillic targets with their case forms ("промпта", "деплоя"): a known stem plus a short ending.
    private static readonly Lazy<string[]> CyrillicTargets = new(() =>
        [.. Pool.Value.Select(p => p.Target.ToLowerInvariant()).Where(t => t.Length >= 4 && !t.Contains(' ') && t.All(c => !char.IsAsciiLetter(c)))]);

    public static string Line(string raw, string final)
    {
        var words = Word().Matches(final).Cast<Match>().ToList();
        var spans = new List<object>();
        for (var i = 0; i < words.Count; i++)
        {
            if (IsKnown(words[i].Value))
            {
                continue;
            }

            var end = i;
            while (end + 1 < words.Count && end - i + 1 < MaxSpanWords && !IsKnown(words[end + 1].Value)
                && final.AsSpan(words[end].Index + words[end].Length, words[end + 1].Index - words[end].Index - words[end].Length).SequenceEqual(" "))
            {
                end++;
            }

            var start = words[i].Index;
            var text = final[start..(words[end].Index + words[end].Length)];
            var candidates = Candidates(text);
            var cyrillic = !text.Any(char.IsAsciiLetter);
            if (candidates.Length > 0 && candidates[0].Target != text
                && (!cyrillic || (candidates[0].Score >= CyrillicScore && text.Length >= 5 && FuzzyTerms.Skeleton(text).Length >= 4)))
            {
                spans.Add(new { text, start, candidates = candidates.Select(c => c.Target), score = Math.Round(candidates[0].Score, 2) });
            }

            i = end;
        }

        return JsonSerializer.Serialize(new { raw, final, spans }, Json);
    }

    // terms.json as Vox.Core embeds it: { group: { target: [sources] } }.
    private static Dictionary<string, Dictionary<string, string[]>> LoadTerms()
    {
        using var stream = typeof(Terms).Assembly.GetManifestResourceStream("Vox.Core.terms.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string[]>>>(stream)!;
    }

    private static bool IsKnown(string word)
    {
        if (word.Any(char.IsDigit) || word.Count(char.IsLetter) <= 2)
        {
            return true;
        }

        var latin = word.Any(char.IsAsciiLetter);
        var cyrillic = word.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
        if (latin && cyrillic)
        {
            return false;
        }

        if (Terms.IsTermForm(word) || Terms.IsTermForm(word.TrimEnd('+')))
        {
            return true;
        }

        if (latin)
        {
            return word.Split('.', '-').All(p => p.Length <= 2 || FuzzyTerms.IsKnownName(p) || Terms.IsTermForm(p));
        }

        var lower = word.ToLowerInvariant();
        return lower.Split('-').All(p => p.Length <= 2 || SelfLearning.IsCommonWord(p))
            || CyrillicTargets.Value.Any(t => lower.StartsWith(t, StringComparison.Ordinal) && lower.Length - t.Length <= 3);
    }

    private static (string Target, double Score)[] Candidates(string text)
    {
        var skeleton = FuzzyTerms.Skeleton(text);
        if (skeleton.Length < 2)
        {
            return [];
        }

        return [.. Pool.Value
            .Where(p => p.Skeletons.Length > 0)
            .Select(p => (p.Target, Score: p.Skeletons.Max(s => FuzzyTerms.Similarity(skeleton, s))))
            .Where(p => p.Score >= MinScore)
            .OrderByDescending(p => p.Score)
            .Take(MaxCandidates)];
    }
}
