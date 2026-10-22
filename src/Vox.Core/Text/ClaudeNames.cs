using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// The Russian model hears "Claude" as "Cloud" / "клауд" / "клот" (owner's log, ~30 times). In Russian
/// speech a cloud is "облако", so a lone "Cloud" means Claude — except in cloud product names
/// ("Google Cloud", "Яндекс Клауд", "Cloud Run", "Cloudflare"). Russian model only: for English, cloud is cloud.
/// </summary>
public static partial class ClaudeNames
{
    private const string Before = "google|гугл|yandex|яндекс|oracle|amazon|амазон|microsoft|майкрософт|mail|мейл|hetzner"
        + "|selectel|селектел|sound|саунд|next|i|ай|alibaba|алибаба|vk|вк|грок|groq|private|hybrid";

    private const string After = "flare|флер|флэр|run|storage|functions|console|platform|shell|drive|native|computing|gaming"
        + "|ран|сторадж|сторедж|консоль|платформ[а-я]*|шелл|драйв|нейтив";

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}-])(?<!(?:" + Before + @")\s+)" +
        @"(?:(?<in>клаудн|клауден|клаудан|клаудин)\s+(?:хром[а-я]*|chrome)" +
        @"|(?<code>клоудкоз[а-я]*|клоудкод[а-я]*|клаудкод[а-я]*|клотко[а-я]*)" +
        @"|(?:cloud|клауд|клоуд|клот|клод)(?:а|у|ом|е|ы)?(?![\p{L}\p{Nd}-])(?!\s+(?:" + After + @")(?![\p{L}]))" +
        @"(?:\s+(?:(?<code>code|код[а-я]*)|(?<desktop>desktop|десктоп[а-я]*|дескоп[а-я]*|дископ[а-я]*)" +
        @"|(?:(?:and|in|ин|энд|н)\s+)?(?<chrome>chrome|хром[а-я]*)|(?<ai>ai|ай|эй\s+ай)))?" +
        @")(?![\p{L}\p{Nd}-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Name();

    public static string Apply(string text) => string.IsNullOrEmpty(text) ? text : Name().Replace(text, m =>
        m.Groups["code"].Success ? "Claude Code"
        : m.Groups["desktop"].Success ? "Claude Desktop"
        : m.Groups["in"].Success || m.Groups["chrome"].Success ? "Claude in Chrome"
        : m.Groups["ai"].Success ? "Claude AI"
        : "Claude");
}
