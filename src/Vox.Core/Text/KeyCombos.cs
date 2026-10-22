using System.Text.RegularExpressions;

namespace Vox.Core.Text;

/// <summary>
/// Spoken keyboard shortcuts → key names: "контрол шифт ти" → "Ctrl+Shift+T", "альт эф четыре" →
/// "Alt+F4". Only a modifier followed by a key counts, so "контроль качества" stays as it is.
/// </summary>
public static partial class KeyCombos
{
    private static readonly Dictionary<string, string> SpokenKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["эй"] = "A", ["си"] = "C", ["сии"] = "C", ["цэ"] = "C", ["це"] = "C", ["ви"] = "V", ["вэ"] = "V",
        ["ве"] = "V", ["зет"] = "Z", ["зэт"] = "Z", ["зед"] = "Z", ["экс"] = "X", ["икс"] = "X", ["эс"] = "S",
        ["ти"] = "T", ["тэ"] = "T", ["эф"] = "F", ["пи"] = "P", ["пэ"] = "P", ["эн"] = "N",
        ["энтер"] = "Enter", ["enter"] = "Enter", ["таб"] = "Tab", ["tab"] = "Tab", ["эскейп"] = "Esc",
        ["escape"] = "Esc", ["esc"] = "Esc", ["делит"] = "Delete", ["delete"] = "Delete", ["del"] = "Delete",
    };

    private static readonly string[] Numbers =
        ["один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять", "десять", "одиннадцать", "двенадцать"];

    // After the real words "контроль"/"control", keys that are also words or numbers ("control a lot",
    // "контроль 2 этапов") need an explicit "+"/"плюс"; after "контрол", "Contrl", "Ctrl"… they do not.
    private static bool NeedsExplicitPlus(string modifier, string key) =>
        modifier.ToLowerInvariant() is "контроль" or "кантроль" or "control"
        && (key is "a" or "A" or "i" or "I" || char.IsDigit(key[0]));

    private const string Modifier = @"к[оа]нтр(?:[оа]?ль?)?|contr(?:[oa]?l)?|ctrl|шифт|shift|альт|alt";
    private const string Separator = @"\s*(?:\+|плюс|plus)\s*|\s*[,-]?\s+";

    [GeneratedRegex(
        @"(?<![\p{L}\p{Nd}])(?:(?<mod>" + Modifier + @")(?<sep>" + Separator + @"))+" +
        @"(?<key>(?:эф|f)\s*(?:1[0-2]|[1-9]|один|два|три|четыре|пять|шесть|семь|восемь|девять|десять|одиннадцать|двенадцать)" +
        @"|[a-z]|\d|энтер|enter|таб|tab|эскейп|escape|esc|делит|delete|del|сии|си|цэ|це|ви|вэ|ве|зет|зэт|зед|экс|икс|эс|ти|тэ|эф|пи|пэ|эн|эй)" +
        @"(?![\p{L}\p{Nd}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Combo();

    public static string Apply(string text) => Combo().Replace(text, m =>
    {
        var key = m.Groups["key"].Value;
        var lastSep = m.Groups["sep"].Captures[^1].Value;
        if (NeedsExplicitPlus(m.Groups["mod"].Captures[^1].Value, key) && !lastSep.Contains('+') && !lastSep.Contains("плюс", StringComparison.OrdinalIgnoreCase)
            && !lastSep.Contains("plus", StringComparison.OrdinalIgnoreCase))
        {
            return m.Value;
        }

        var mods = m.Groups["mod"].Captures.Select(c => ModifierName(c.Value)).Distinct();
        return string.Join('+', mods.Append(KeyName(key)));
    });

    private static string ModifierName(string spoken) => spoken.ToLowerInvariant() switch
    {
        "шифт" or "shift" => "Shift",
        "альт" or "alt" => "Alt",
        _ => "Ctrl",
    };

    private static string KeyName(string spoken)
    {
        var fKey = FKey().Match(spoken);
        if (fKey.Success)
        {
            var n = fKey.Groups["n"].Value;
            return "F" + (int.TryParse(n, out var number) ? number : Array.IndexOf(Numbers, n.ToLowerInvariant()) + 1);
        }

        return SpokenKeys.TryGetValue(spoken, out var name) ? name : spoken.ToUpperInvariant();
    }

    [GeneratedRegex(@"^(?:эф|f)\s*(?<n>.+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FKey();
}
