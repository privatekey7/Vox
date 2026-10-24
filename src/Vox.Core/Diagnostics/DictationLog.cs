using System.Text.RegularExpressions;

namespace Vox.Core.Diagnostics;

/// <summary>A dictation as logged: the model's text and, when known, what Vox pasted.</summary>
public sealed record LoggedDictation(string Raw, string? Final);

/// <summary>
/// Reads dictations back from vox.log (any version: "12:00:00.000 [STT] Raw: "…"" in 2.0, other
/// timestamps and quotes in 1.x, CRLF), from recordings.tsv or from plain lines "Raw[&lt;TAB&gt;Final]"
/// (tools/Vox.Replay). A log written without texts (<see cref="LogText"/>) has no dictations to read.
/// </summary>
public static partial class DictationLog
{
    [GeneratedRegex(@"\[STT\]\s*(Raw|Final)(?:\s+text)?\s*:\s*(.*?)\s*$")]
    private static partial Regex SttLine();

    public static IEnumerable<LoggedDictation> Read(IEnumerable<string> lines)
    {
        var all = lines.Select(l => l.TrimEnd('\r')).ToList();
        if (all.Count > 0 && all[0].StartsWith("file\ttime\tmodel\traw\tfinal", StringComparison.Ordinal))
        {
            // recordings.tsv (Settings → "Сохранять записи диктовок"): the texts are there even when the log has none.
            foreach (var fields in all.Skip(1).Select(l => l.Split('\t')).Where(p => p.Length >= 5 && p[3].Length > 0))
            {
                yield return new LoggedDictation(fields[3], fields[4]);
            }

            yield break;
        }

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
