using System.Globalization;
using System.Text;

namespace Vox.Core.Text;

/// <summary>
/// What a dictionary pair may hold (security audit M5): Vox pastes its target into any window, so a pair from a
/// file someone sent must not carry a line break (a command in a console), bidi marks or zero-width characters.
/// </summary>
public static class SafeText
{
    /// <summary>Longer than a phrase: a pair this long is not a word the model mishears.</summary>
    public const int MaxPairLength = 100;

    /// <summary>No control characters (line breaks, tab), no line or paragraph separators, no invisible format ones.</summary>
    public static bool HasNoHiddenChars(string text)
    {
        for (var rest = text.AsSpan(); !rest.IsEmpty;)
        {
            if (Rune.DecodeFromUtf16(rest, out var rune, out var length) != System.Buffers.OperationStatus.Done)
            {
                return false; // a lone surrogate
            }

            rest = rest[length..];
            switch (Rune.GetUnicodeCategory(rune))
            {
                case UnicodeCategory.Control:            // \r \n \t …
                case UnicodeCategory.Format:             // RLO and other bidi marks, zero-width space and joiners, BOM
                case UnicodeCategory.LineSeparator:      // U+2028
                case UnicodeCategory.ParagraphSeparator: // U+2029
                    return false;
            }
        }

        return true;
    }

    /// <summary>The source or the target of a dictionary pair: not empty, at most <see cref="MaxPairLength"/>, nothing hidden.</summary>
    public static bool IsPairPart(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= MaxPairLength && HasNoHiddenChars(text);
}
