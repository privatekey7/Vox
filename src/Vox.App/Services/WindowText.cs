using System.Diagnostics;
using System.Windows.Automation;

namespace Vox.App.Services;

/// <summary>
/// Text of the window the user dictates into, read at a dictation for <see cref="Vox.Core.Text.ScreenWords.FromWindowText"/>
/// (a page in a browser, the messages above a chat's input field).
/// </summary>
/// <remarks>
/// Read-only UI Automation; the text stays in memory and is never logged or saved, only names from it.
/// Never read: password fields and the focused field itself, where Vox pastes its own dictations (a misheard
/// word would come back as a name). At most <see cref="MaxChars"/> characters and <see cref="MaxElements"/>
/// elements; the whole read has a timeout, so a slow or hung app cannot hold up the dictation.
/// </remarks>
internal static class WindowText
{
    private const int MaxChars = 20_000;
    private const int MaxElements = 2_000;

    public static string? Read(TimeSpan timeout)
    {
        var read = Task.Run(ReadActive);
        try
        {
            return read.Wait(timeout) ? read.Result : null;
        }
        catch (AggregateException e)
        {
            Debug.WriteLine(e); // the window went away or does not answer: no names from it
            return null;
        }
    }

    private static string? ReadActive()
    {
        var hwnd = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (hwnd == 0 || pid == (uint)Environment.ProcessId)
        {
            return null;
        }

        var window = AutomationElement.FromHandle(hwnd);
        var focused = AutomationElement.FocusedElement;
        var focusedText = focused is not null && !IsPassword(focused) ? ReadPattern(focused) : null;
        var text = new System.Text.StringBuilder();
        var walker = TreeWalker.ControlViewWalker;
        var stack = new Stack<AutomationElement>([window]);
        var seen = 0;
        while (stack.Count > 0 && text.Length < MaxChars && seen++ < MaxElements)
        {
            var element = stack.Pop();
            if (IsPassword(element) || (focused is not null && Automation.Compare(element, focused)))
            {
                continue;
            }

            // A document (a page, a chat in Electron) gives all its text at once: no need to walk inside it.
            var type = element.Current.ControlType;
            if ((type == ControlType.Document || type == ControlType.Edit) && ReadPattern(element) is { } whole)
            {
                Append(text, Without(whole, focusedText));
                continue;
            }

            if (type == ControlType.Text)
            {
                Append(text, element.Current.Name);
            }

            // Children pushed in reverse, so the text comes out in reading order.
            var children = new List<AutomationElement>();
            for (var child = walker.GetFirstChild(element); child is not null && children.Count < MaxElements; child = walker.GetNextSibling(child))
            {
                children.Add(child);
            }

            for (var i = children.Count - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }

        return text.Length > 0 ? text.ToString() : null;
    }

    // The input field inside a chat's document: its text (the user's draft and Vox's pastes) is cut out.
    private static string Without(string text, string? focusedText) =>
        string.IsNullOrWhiteSpace(focusedText) ? text : text.Replace(focusedText, " ", StringComparison.Ordinal);

    private static void Append(System.Text.StringBuilder text, string? part)
    {
        if (string.IsNullOrWhiteSpace(part))
        {
            return;
        }

        var room = MaxChars - text.Length;
        text.Append(part.Length > room ? part[..room] : part).Append('\n');
    }

    private static bool IsPassword(AutomationElement element) =>
        element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty) is true;

    private static string? ReadPattern(AutomationElement element)
    {
        if (element.TryGetCurrentPattern(TextPattern.Pattern, out var text))
        {
            return ((TextPattern)text).DocumentRange.GetText(MaxChars);
        }

        if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var value))
        {
            var s = ((ValuePattern)value).Current.Value;
            return s.Length > MaxChars ? s[..MaxChars] : s;
        }

        return null;
    }
}
