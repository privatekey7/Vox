using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;

namespace Vox.App.Services;

/// <summary>
/// What surrounds the cursor before a paste, for <see cref="Vox.Core.Text.ContextJoiner"/>: two
/// characters before it and one after, read via UI Automation. Apps that hide their text (Qt,
/// some Electron) fall back to the end of Vox's own previous paste into the same window.
/// </summary>
/// <remarks>Password fields are never read; the characters are never logged.</remarks>
internal sealed class CursorContext
{
    private static readonly TimeSpan UiaTimeout = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan LastPasteValidFor = TimeSpan.FromSeconds(60);

    private nint _lastWindow;
    private string _lastTail = "";
    private DateTime _lastAt;

    /// <summary>(before, after, source) — before/after are null when unknown; source is for the log.</summary>
    public (string? Before, string? After, string Source) Read()
    {
        var window = Native.GetForegroundWindow();
        var uia = Task.Run(ReadUia);
        if (uia.Wait(UiaTimeout) && uia.Result is var (before, after))
        {
            return (before, after, "uia");
        }

        if (window == _lastWindow && DateTime.UtcNow - _lastAt < LastPasteValidFor && _lastTail.Length > 0)
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
}
