using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// AI model names with versions, after <see cref="Terms"/>: "Claude Opus четыре и пять" → "Claude Opus 4.5",
/// "GPT пять" → "GPT-5", "Gemini 2,5 про." → "Gemini 2.5 Pro.", "DeepSeek эр один" → "DeepSeek R1".
/// Works on number words (Parakeet before ITN) and digits (GigaAM) alike.
/// </summary>
public static partial class ModelVersions
{
    /// <param name="Join">Between the name and the version: " ", "-" or "".</param>
    /// <param name="Prefixes">Spoken letter before the number → its letter ("эр" → "R").</param>
    /// <param name="Suffixes">Spoken word after the version → its spelling ("про" → " Pro").</param>
    /// <param name="DecimalOnly">An ordinary Russian word ("опус", "сонет"): only "4.5", never a bare "4".</param>
    /// <param name="NeedsDetail">A bare number is not a version ("Claude 18 плюс"): only "3.5" or "4 Sonnet".</param>
    private sealed record Brand(string Name, string Join, Dictionary<string, string>? Prefixes = null,
        Dictionary<string, string>? Suffixes = null, bool DecimalOnly = false, bool NeedsDetail = false);

    private static readonly Dictionary<string, string> ClaudeFamily = new()
    {
        ["опус"] = " Opus", ["opus"] = " Opus", ["сонет"] = " Sonnet", ["соннет"] = " Sonnet",
        ["sonnet"] = " Sonnet", ["хайку"] = " Haiku", ["haiku"] = " Haiku",
    };

    private static readonly Dictionary<string, string> Gemini = new()
    {
        ["про"] = " Pro", ["pro"] = " Pro", ["флэш"] = " Flash", ["флеш"] = " Flash", ["flash"] = " Flash",
        ["лайт"] = " Lite", ["lite"] = " Lite",
    };

    private static readonly Dictionary<string, string> Gpt = new() { ["о"] = "o", ["o"] = "o", ["мини"] = " mini", ["mini"] = " mini" };
    private static readonly Dictionary<string, string> K = new() { ["кей"] = "K", ["к"] = "K", ["k"] = "K" };
    private static readonly Dictionary<string, string> V = new() { ["ви"] = "V", ["вэ"] = "V", ["v"] = "V" };

    // Keys are matched case-insensitively; a longer name wins ("Claude Opus" before "Claude").
    private static readonly Dictionary<string, Brand> Brands = new Brand[]
    {
        new("Claude Opus", " "), new("Claude Sonnet", " "), new("Claude Haiku", " "),
        new("Claude", " ", Suffixes: ClaudeFamily, NeedsDetail: true),
        new("Opus", " "), new("Sonnet", " "), new("Haiku", " "),
        new("GPT", "-", Suffixes: Gpt), new("GLM", "-"), new("YandexGPT", " "), new("GigaChat", " "),
        new("Gemini", " ", Suffixes: Gemini), new("Gemma", " "), new("Grok", " "), new("Llama", " "),
        new("DeepSeek", " ", new() { ["ви"] = "V", ["вэ"] = "V", ["v"] = "V", ["эр"] = "R", ["r"] = "R" }),
        new("Qwen", ""), new("Mistral", " "), new("Mixtral", " "), new("Kimi", " ", K),
        new("Midjourney", " ", V), new("Suno", " ", V), new("Stable Diffusion", " "), new("Sora", " "),
        new("Veo", " "), new("Kling", " "), new("Flux", " "), new("Kandinsky", " "),
    }.ToDictionary(b => b.Name, StringComparer.OrdinalIgnoreCase);

    // Ordinary Russian words that name a model only next to a version ("сонет четыре и пять").
    private static readonly Dictionary<string, Brand> CyrillicNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["опус"] = new("Opus", " ", DecimalOnly: true), ["сонет"] = new("Sonnet", " ", DecimalOnly: true),
        ["соннет"] = new("Sonnet", " ", DecimalOnly: true), ["хайку"] = new("Haiku", " ", DecimalOnly: true),
        ["лама"] = new("Llama", " "), ["джемма"] = new("Gemma", " "), ["кандинский"] = new("Kandinsky", " "),
        ["сора"] = new("Sora", " "), ["кими"] = new("Kimi", " ", K),
    };

    private static readonly Dictionary<string, int> Numbers = new()
    {
        ["ноль"] = 0, ["один"] = 1, ["одна"] = 1, ["два"] = 2, ["три"] = 3, ["четыре"] = 4, ["пять"] = 5,
        ["шесть"] = 6, ["семь"] = 7, ["восемь"] = 8, ["девять"] = 9, ["десять"] = 10, ["одиннадцать"] = 11,
        ["двенадцать"] = 12, ["тринадцать"] = 13, ["четырнадцать"] = 14, ["пятнадцать"] = 15,
        ["шестнадцать"] = 16, ["семнадцать"] = 17, ["восемнадцать"] = 18, ["девятнадцать"] = 19, ["двадцать"] = 20,
    };

    private static readonly Lazy<Regex> Pattern = new(() =>
    {
        var names = string.Join('|', Brands.Keys.Concat(CyrillicNames.Keys).OrderByDescending(k => k.Length)
            .Select(k => Regex.Escape(k).Replace(@"\ ", @"\s+", StringComparison.Ordinal)));
        var prefixes = string.Join('|', Brands.Values.SelectMany(b => (IEnumerable<string>?)b.Prefixes?.Keys ?? []).Distinct().OrderByDescending(k => k.Length));
        var suffixes = string.Join('|', Brands.Values.SelectMany(b => (IEnumerable<string>?)b.Suffixes?.Keys ?? []).Distinct().OrderByDescending(k => k.Length));
        var number = @"\d+|" + string.Join('|', Numbers.Keys.OrderByDescending(k => k.Length));
        return new Regex(
            $@"(?<![\p{{L}}\p{{Nd}}-])(?<name>{names})(?:\s+|-)(?:(?<pre>{prefixes})\s*)?(?<major>{number})" +
            $@"(?:(?:\s+и\s+|\s*(?:\.|точка)\s*|,)(?<minor>{number}))?(?![\p{{L}}\p{{Nd}}])" +
            $@"(?:\s*(?<suffix>{suffixes})(?![\p{{L}}\p{{Nd}}]))?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    });

    // Suffixes that are also Russian words: taken only before punctuation or the end ("Gemini 2.5 про." but
    // "Gemini 2.5 про погоду").
    private static readonly HashSet<string> Ambiguous = ["про", "о"];

    public static string Apply(string text) => string.IsNullOrEmpty(text) ? text : Pattern.Value.Replace(text, m => Format(m, text));

    private static string Format(Match m, string text)
    {
        var name = Regex.Replace(m.Groups["name"].Value, @"\s+", " ");
        var brand = Brands.GetValueOrDefault(name) ?? CyrillicNames[name];
        var minor = m.Groups["minor"];
        string? prefix = null;
        if (m.Groups["pre"].Success && brand.Prefixes?.TryGetValue(m.Groups["pre"].Value.ToLowerInvariant(), out prefix) != true)
        {
            return m.Value; // "Kimi эр 2": a letter this model does not use
        }

        if (brand.DecimalOnly && !minor.Success)
        {
            return m.Value; // "сонет 66" is a poem
        }

        var spoken = m.Groups["suffix"];
        var suffix = spoken.Success && brand.Suffixes?.TryGetValue(spoken.Value.ToLowerInvariant(), out var s) == true
            && (!Ambiguous.Contains(spoken.Value.ToLowerInvariant()) || EndsPhrase(text, m.Index + m.Length))
            ? s
            : null;
        if (brand.NeedsDetail && !minor.Success && suffix is null)
        {
            return m.Value;
        }

        var result = brand.Name + brand.Join + prefix + Number(m.Groups["major"].Value)
            + (minor.Success ? "." + Number(minor.Value) : "");
        var versionEnd = minor.Success ? minor.Index + minor.Length : m.Groups["major"].Index + m.Groups["major"].Length;
        return suffix is not null ? result + suffix : result + text[versionEnd..(m.Index + m.Length)];
    }

    private static bool EndsPhrase(string text, int from)
    {
        while (from < text.Length && char.IsWhiteSpace(text[from]))
        {
            from++;
        }

        return from == text.Length || !char.IsLetterOrDigit(text[from]);
    }

    private static string Number(string token) =>
        Numbers.TryGetValue(token.ToLowerInvariant(), out var n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : token;
}
