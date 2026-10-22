namespace Vox.Core.Text;

/// <summary>Where self-learning reads a field's text from.</summary>
public enum FieldSource
{
    /// <summary>Not readable (or a password): nothing is learned in this field.</summary>
    None,

    /// <summary>UI Automation TextPattern / ValuePattern (most apps).</summary>
    Uia,

    /// <summary>WM_GETTEXT to the focused control (Notepad++/Scintilla, classic EDIT and RichEdit).</summary>
    WindowText,
}

/// <summary>
/// Picks the way to read the focused field for self-learning. UI Automation first; apps without it
/// (Notepad++) answer WM_GETTEXT — but never a password EDIT and never a huge document.
/// </summary>
public static class FieldReadPolicy
{
    /// <summary>Longer documents are skipped: WM_GETTEXT copies the whole text across processes.</summary>
    public const int MaxWindowTextChars = 1_000_000;

    /// <param name="uiaHasText">UI Automation exposed the field's text.</param>
    /// <param name="isPassword">UIA IsPassword, or ES_PASSWORD on an EDIT control.</param>
    /// <param name="windowTextLength">WM_GETTEXTLENGTH of the focused control, -1 if it did not answer.</param>
    public static FieldSource Choose(bool uiaHasText, bool isPassword, int windowTextLength)
    {
        if (isPassword)
        {
            return FieldSource.None;
        }

        if (uiaHasText)
        {
            return FieldSource.Uia;
        }

        return windowTextLength is > 0 and <= MaxWindowTextChars ? FieldSource.WindowText : FieldSource.None;
    }
}
