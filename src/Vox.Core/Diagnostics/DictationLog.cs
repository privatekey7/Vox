using System.Text.RegularExpressions;

namespace Vox.Core.Diagnostics;

/// <summary>A dictation as logged: the model's text and, when known, what Vox pasted.</summary>
public sealed record LoggedDictation(string Raw, string? Final);

/// <summary>
/// Reads dictations back from vox.log (any version: "12:00:00.000 [STT] Raw: "…"" in 2.0, other
/// timestamps and quotes in 1.x, CRLF) or from plain lines "Raw[&lt;TAB&gt;Final]" (tools/Vox.Replay).
/// </summary>
public static partial class DictationLog
{
    [GeneratedRegex(@"\[STT\]\s*(Raw|Final)(?:\s+text)?\s*:\s*(.*?)\s*$")]
    private static partial Regex SttLine();

    public static IEnumerable<LoggedDictation> Read(IEnumerable<string> lines)
    {
        var all = lines.Select(l => l.TrimEnd('\r')).ToList();
        if (!all.Exists(l => l.Contains("[STT]", StringComparison.Ordinal)))
        {
            foreach (var line in all.Where(l => l.Trim().Length > 0))
            {
                var fields = line.Split('\t'); // a plain line: Raw[<TAB>Final]
                yield return new LoggedDictation(fields[0].Trim(), fields.Length > 1 ? fields[1] : null);
            }

            yield break;
        }

        string? raw = null;
        foreach (var line in all)
        {
            var m = SttLine().Match(line);
            if (!m.Success)
            {
                continue; // [INIT], [DL], tracebacks…
            }

            var text = Unquote(m.Groups[2].Value);
            if (m.Groups[1].Value == "Raw")
            {
                if (raw is not null)
                {
                    yield return new LoggedDictation(raw, null);
                }

                raw = text;
            }
            else if (raw is not null)
            {
                yield return new LoggedDictation(raw, text);
                raw = null;
            }
        }

        if (raw is not null)
        {
            yield return new LoggedDictation(raw, null);
        }
    }

    private static string Unquote(string s) =>
        s.Length >= 2 && (s[0], s[^1]) is ('"', '"') or ('\'', '\'') or ('«', '»') ? s[1..^1] : s;
}
