namespace Vox.Core.Text;

/// <summary>
/// Fits a dictation to the text around the cursor: after "Привет," it continues the sentence
/// (" как дела"), after "Привет." it starts a new one (" Как дела"), at the start of a field it
/// adds no space, and in the middle of a word run it drops the final full stop.
/// </summary>
/// <remarks>
/// Deterministic on purpose (no language model). Known limit: a proper name at the start of a
/// continued sentence is lowered ("…, пушкин"); abbreviations (API), mixed case (GitHub), known
/// terms (Docker) and "I" keep their case.
/// </remarks>
public static class ContextJoiner
{
    private const string SentenceEnds = ".!?…";
    private const string Openers = "([{«\"'“„";
    private const string Continuers = ",;:—–-";

    /// <param name="text">Processed dictation, with its usual leading space.</param>
    /// <param name="before">Up to two characters before the cursor ("" at the start of the field), or null if unknown.</param>
    /// <param name="after">The character after the cursor ("" at the end), or null if unknown.</param>
    public static string Apply(string text, string? before, string? after)
    {
        if (before is null || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var body = text.TrimStart();
        var (space, lower) = Lead(before);
        if (lower)
        {
            body = LowerFirstWord(body);
        }

        if (!string.IsNullOrEmpty(after) && after[0] != '\n' && after[0] != '\r')
        {
            if (body.EndsWith('.') && !body.EndsWith("..", StringComparison.Ordinal))
            {
                body = body[..^1]; // the sentence goes on after the cursor
            }

            if (char.IsLetterOrDigit(after[0]))
            {
                body += " ";
            }
        }

        return (space ? " " : "") + body;
    }

    /// <summary>Short name of the rule for the log (no user text).</summary>
    public static string Describe(string? before) => before switch
    {
        null => "unknown",
        "" => "field start",
        _ => Lead(before) switch
        {
            (false, false) => "start / after opener or new line",
            (true, false) => "after sentence end",
            (_, true) => "continues the sentence",
        },
    };

    /// <summary>(leading space?, lower-case the first word?)</summary>
    private static (bool Space, bool Lower) Lead(string before)
    {
        if (before.Length == 0)
        {
            return (false, false);
        }

        var last = before[^1];
        if (last is '\n' or '\r')
        {
            return (false, false);
        }

        if (char.IsWhiteSpace(last))
        {
            // Already separated: the character before the space decides the case.
            var prev = before.Length > 1 ? before[^2] : '.';
            return (false, char.IsLetterOrDigit(prev) || Continuers.Contains(prev, StringComparison.Ordinal));
        }

        if (Openers.Contains(last, StringComparison.Ordinal))
        {
            return (false, false);
        }

        if (SentenceEnds.Contains(last, StringComparison.Ordinal))
        {
            return (true, false);
        }

        return (true, char.IsLetterOrDigit(last) || Continuers.Contains(last, StringComparison.Ordinal));
    }

    private static string LowerFirstWord(string body)
    {
        var end = 0;
        while (end < body.Length && (char.IsLetter(body[end]) || body[end] == '\''))
        {
            end++;
        }

        var word = body[..end];
        if (word.Length == 0 || !char.IsUpper(word[0]) || KeepsCase(word))
        {
            return body;
        }

        return char.ToLowerInvariant(word[0]) + body[1..];
    }

    private static bool KeepsCase(string word) =>
        word == "I" || word.StartsWith("I'", StringComparison.Ordinal)
        || word.Skip(1).Any(char.IsUpper) // API, GitHub
        || Terms.IsTermForm(word); // Docker, Python
}
