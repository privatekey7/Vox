using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Vox.App.Services;

/// <summary>
/// What surrounds the cursor before a paste, for <see cref="Vox.Core.Text.ContextJoiner"/>: two
/// characters before it and one after, read via UI Automation, or by messages to Scintilla
/// (Notepad++) and EDIT controls. Apps that hide their text (Qt, some Electron) fall back to the end
/// of Vox's own previous paste into the same window, unless a key was pressed since (Enter, arrows).
/// </summary>
/// <remarks>Password fields are never read; only the two characters before the cursor are logged.</remarks>
internal sealed class CursorContext
{
    private static readonly TimeSpan UiaTimeout = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan LastPasteValidFor = TimeSpan.FromSeconds(60);

    private nint _lastWindow;
    private string _lastTail = "";
    private DateTime _lastAt;
    private long _lastAtTick;

    /// <summary>(before, after, source) — before/after are null when unknown; source is for the log.</summary>
    public (string? Before, string? After, string Source) Read()
    {
        var window = Native.GetForegroundWindow();
        var uia = Task.Run(ReadUia);
        if (uia.Wait(UiaTimeout) && uia.Result is var (before, after))
        {
            return (before, after, "uia");
        }

        if (ReadWindow() is var (wBefore, wAfter, kind))
        {
            return (wBefore, wAfter, kind);
        }

        // Enter, arrows, typing after the paste: its end is no longer what precedes the cursor.
        if (window == _lastWindow && DateTime.UtcNow - _lastAt < LastPasteValidFor && _lastTail.Length > 0
            && KeyboardHook.LastUserKeyTick < _lastAtTick)
        {
            return (_lastTail, null, "last-insert");
        }

        return (null, null, "none");
    }

    /// <summary>Call after each paste: the fallback context for the next dictation in this window.</summary>
    public void Remember(string pasted)
    {
        _lastWindow = Native.GetForegroundWindow();
        _lastTail = pasted.Length > 2 ? pasted[^2..] : pasted;
        _lastAt = DateTime.UtcNow;
        _lastAtTick = Environment.TickCount64;
    }

    private static (string Before, string After)? ReadUia()
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null
                || (bool)element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty)
                || !element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
            {
                return null;
            }

            var selection = ((TextPattern)pattern).GetSelection();
            if (selection.Length == 0)
            {
                return null;
            }

            var caret = selection[0];
            if (caret.CompareEndpoints(TextPatternRangeEndpoint.Start, ((TextPattern)pattern).DocumentRange, TextPatternRangeEndpoint.Start) == 0)
            {
                // The start of the field, whatever text the app reports (a placeholder, an attachment label).
                var atStart = caret.Clone();
                atStart.MoveEndpointByRange(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End);
                atStart.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1);
                return ("", atStart.GetText(2));
            }

            var before = caret.Clone();
            before.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
            before.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -2);
            var after = caret.Clone();
            after.MoveEndpointByRange(TextPatternRangeEndpoint.Start, caret, TextPatternRangeEndpoint.End);
            after.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1);
            return (before.GetText(4), after.GetText(2));
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
        {
            return null;
        }
    }

    // Apps without UI Automation text: Scintilla (Notepad++) and classic EDIT controls answer messages.
    private const uint SCI_GETCHARAT = 2007, SCI_GETCURRENTPOS = 2008, SCI_POSITIONBEFORE = 2417, EM_GETSEL = 0x00B0;
    private const uint MessageTimeoutMs = 50;

    private static (string Before, string After, string Source)? ReadWindow()
    {
        var hwnd = Native.FocusedControl();
        if (hwnd == 0)
        {
            return null;
        }

        var cls = Native.ClassName(hwnd);
        if (cls.Equals("Scintilla", StringComparison.OrdinalIgnoreCase))
        {
            if (Send(hwnd, SCI_GETCURRENTPOS, 0) is not { } pos)
            {
                return null;
            }

            // Byte positions in UTF-8; a non-ASCII byte is taken for a letter (Cyrillic) — enough for the joiner.
            char At(nint p) => Send(hwnd, SCI_GETCHARAT, p) is { } b && b != 0 ? (b is > 0 and < 0x80 ? (char)b : 'а') : '\0';
            var before = "";
            var p1 = pos > 0 ? Send(hwnd, SCI_POSITIONBEFORE, pos) ?? 0 : 0;
            if (pos > 0)
            {
                before = At(p1).ToString();
                if (p1 > 0 && Send(hwnd, SCI_POSITIONBEFORE, p1) is { } p2)
                {
                    before = At(p2) + before;
                }
            }

            var next = At(pos);
            return (before, next == '\0' ? "" : next.ToString(), "scintilla");
        }

        if (cls.Equals("Edit", StringComparison.OrdinalIgnoreCase)
            && (Native.GetWindowLongPtr(hwnd, Native.GWL_STYLE) & Native.ES_PASSWORD) == 0
            && Send(hwnd, EM_GETSEL, 0) is { } sel)
        {
            var caret = (int)(sel & 0xFFFF);
            var length = Native.WindowTextLength(hwnd, MessageTimeoutMs);
            if (length < 0 || length > Vox.Core.Text.FieldReadPolicy.MaxWindowTextChars
                || Native.WindowText(hwnd, length, MessageTimeoutMs) is not { } text || caret > text.Length)
            {
                return null;
            }

            return (text[Math.Max(0, caret - 2)..caret], caret < text.Length ? text[caret..(caret + 1)] : "", "edit");
        }

        return null;
    }

    private static nint? Send(nint hwnd, uint msg, nint wParam) =>
        Native.SendMessageTimeout(hwnd, msg, wParam, 0, Native.SMTO_ABORTIFHUNG, MessageTimeoutMs, out var r) == 0 ? null : r;
}
