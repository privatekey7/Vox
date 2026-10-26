using System.Globalization;
using System.Text;

namespace Vox.Core.Clipboard;

/// <summary>
/// The text Vox types key by key when the clipboard stays busy (another app holds it open). Typed
/// keys act at once, so a line break would be Enter: a sent message, a command run in a console.
/// </summary>
public static class TypedText
{
    /// <summary>
    /// Every run of control characters (line breaks, tab) and line or paragraph separators becomes one
    /// space; a lone surrogate is dropped. Everything else is typed as it is.
    /// </summary>
    public static string ForTyping(string text)
    {
        var result = new StringBuilder(text.Length);
        var gap = false;
        for (var rest = text.AsSpan(); !rest.IsEmpty;)
        {
            var status = Rune.DecodeFromUtf16(rest, out var rune, out var length);
            var part = rest[..length];
            rest = rest[length..];
            if (status != System.Buffers.OperationStatus.Done)
            {
                continue;
            }

            if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                gap = true;
                continue;
            }

            if (gap)
            {
                if (result.Length > 0 && result[^1] != ' ' && rune.Value != ' ')
                {
                    result.Append(' ');
                }

                gap = false;
            }

            result.Append(part);
        }

        return result.ToString();
    }
}
