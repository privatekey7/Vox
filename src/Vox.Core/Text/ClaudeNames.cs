using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// The Russian model hears "Claude" as "Cloud" / "клауд" / "клот" (owner's log, ~30 times). In Russian
/// speech a cloud is "облако", so a lone "Cloud" means Claude — except in cloud product names
/// ("Google Cloud", "Яндекс Клауд", "Cloud Run", "Cloudflare"). The Latin "Cloud" only for the Russian
/// model (Parakeet's English "cloud" is a cloud); Cyrillic forms for both.
/// </summary>
public static partial class ClaudeNames
{
    private const string Before = "google|гугл|yandex|яндекс|oracle|amazon|амазон|microsoft|майкрософт|mail|мейл|hetzner"
        + "|selectel|селектел|sound|саунд|next|i|ай|alibaba|алибаба|vk|вк|грок|groq|private|hybrid";

    private const string After = "flare|флер|флэр|run|storage|functions|console|platform|shell|drive|native|computing|gaming"
        + "|ран|сторадж|сторедж|консоль|платформ[а-я]*|шелл|драйв|нейтив";

    private const string Lead =
        @"(?<![\p{L}\p{Nd}-])(?<!(?:" + Before + @")\s+)" +
        @"(?:(?<in>клаудн|клауден|клаудан|клаудин)\s+(?:хром[а-я]*|chrome)" +
        @"|(?<code>клоудкоз[а-я]*|клоудкод[а-я]*|клаудкод[а-я]*|клотко[а-я]*)|";

    private const string Tail =
        @"(?:а|у|ом|е|ы)?(?![\p{L}\p{Nd}-])(?!\s+(?:" + After + @")(?![\p{L}]))" +
        @"(?:\s+(?:(?<code>code|код[а-я]*)|(?<desktop>desktop|десктоп[а-я]*|дескоп[а-я]*|дископ[а-я]*)" +
        @"|(?:(?:and|in|ин|энд|н)\s+)?(?<chrome>chrome|хром[а-я]*)|(?<ai>ai|ай|эй\s+ай)))?" +
        @")(?![\p{L}\p{Nd}-])";

    private const string Pattern = Lead + "(?:cloud|клауд|клоуд|клот|клод)" + Tail;
    private const string CyrillicPattern = Lead + "(?:клауд|клоуд|клот|клод)" + Tail;

    [GeneratedRegex(Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Name();

    [GeneratedRegex(CyrillicPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CyrillicName();

    /// <param name="latinCloud">Russian model: a Latin "Cloud" is Claude too (English: a cloud is a cloud).</param>
    public static string Apply(string text, bool latinCloud) => string.IsNullOrEmpty(text) ? text : (latinCloud ? Name() : CyrillicName()).Replace(text, m =>
        m.Groups["code"].Success ? "Claude Code"
        : m.Groups["desktop"].Success ? "Claude Desktop"
        : m.Groups["in"].Success || m.Groups["chrome"].Success ? "Claude in Chrome"
        : m.Groups["ai"].Success ? "Claude AI"
        : "Claude");
}
