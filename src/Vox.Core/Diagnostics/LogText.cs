using System.Globalization;

namespace Vox.Core.Diagnostics;

/// <summary>
/// How the user's text goes into vox.log. By default only its size: dictations are personal (§11.1 of the
/// owner's standard). With the developer setting "log_texts" the text itself, as Vox always logged it, so
/// <see cref="DictationLog"/> and tools/Vox.Replay can read it back. Both forms keep the tag, for grep.
/// </summary>
/// <remarks>No hash of the text either: a short phrase is easy to guess from an unsalted hash.</remarks>
public static class LogText
{
    /// <summary>
    /// "Raw: "текст"" with texts on, "Raw (5 chars, 1 word)" without. The hidden form has no colon after the
    /// label, so <see cref="DictationLog"/> never takes a size for a dictation.
    /// </summary>
    public static string Field(string label, string? text, bool full) =>
        full ? $"{label}: \"{text}\"" : $"{label} ({Size(text)})";

    /// <summary>A replacement: "\"кубер\" → \"Kubernetes\"" with texts on, "1 word → 1 word" without.</summary>
    public static string Pair(string from, string to, bool full) =>
        full ? $"\"{from}\" → \"{to}\"" : $"{Words(from)} → {Words(to)}";

    /// <summary>"12 chars, 3 words".</summary>
    public static string Size(string? text) =>
        string.Create(CultureInfo.InvariantCulture, $"{text?.Length ?? 0} chars, {Words(text)}");

    private static string Words(string? text)
    {
        var n = text?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length ?? 0;
        return string.Create(CultureInfo.InvariantCulture, $"{n} word{(n == 1 ? "" : "s")}");
    }
}
