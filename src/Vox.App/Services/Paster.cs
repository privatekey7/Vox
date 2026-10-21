using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Vox.Core.Text;

namespace Vox.App.Services;

/// <summary>
/// Puts a dictation at the cursor: clipboard + synthetic Ctrl+V, then gives the user their own
/// clipboard back. Clipboard calls run on the UI (STA) thread.
/// </summary>
internal sealed class Paster(Dispatcher ui)
{
    // The target app reads the clipboard asynchronously after Ctrl+V.
    // debt: fixed delay; a very busy app could read after the restore. Upgrade path: delayed
    // clipboard rendering (WM_RENDERFORMAT) to learn exactly when the paste happened.
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(500);

    public void Paste(string text, bool restoreClipboard)
    {
        var (separator, body) = Spacing.SplitLeadingSeparator(text);
        var previous = restoreClipboard ? ui.Invoke(ReadText) : null;

        ui.Invoke(() => WriteText(body));
        Log.Info("[CLIP] Copied to clipboard");
        Thread.Sleep(50);

        var keys = new List<Native.INPUT>();
        if (separator.Length > 0)
        {
            // Typed, not pasted: the Chrome/Edge address bar trims pasted leading spaces.
            keys.Add(Native.Key(Native.VK_SPACE, up: false));
            keys.Add(Native.Key(Native.VK_SPACE, up: true));
        }

        keys.Add(Native.Key(Native.VK_CONTROL, up: false));
        keys.Add(Native.Key(Native.VK_V, up: false));
        keys.Add(Native.Key(Native.VK_V, up: true));
        keys.Add(Native.Key(Native.VK_CONTROL, up: true));
        var (target, elevated) = Native.ForegroundApp();
        var sent = Native.SendInput((uint)keys.Count, [.. keys], Marshal.SizeOf<Native.INPUT>());
        Log.Info(sent == keys.Count ? $"[PASTE] Pasted into {target}" : $"[PASTE] SendInput sent {sent} of {keys.Count} keys to {target}");
        var blocked = elevated && !Environment.IsPrivilegedProcess;
        if (blocked)
        {
            // Windows drops keys sent to an admin window without any error (UIPI).
            Log.Info($"[PASTE] {target} probably runs as administrator — Windows blocks the paste; the text stays in the clipboard");
        }

        if (previous is null || previous == body || blocked)
        {
            return;
        }

        Thread.Sleep(RestoreDelay);
        ui.Invoke(() =>
        {
            if (ReadText() == body) // the user did not copy something new meanwhile
            {
                WriteText(previous);
                Log.Info("[CLIP] Restored previous clipboard");
            }
        });
    }

    /// <summary>Copy without pasting (tray: "Скопировать последнюю диктовку").</summary>
    public void Copy(string text) => ui.Invoke(() => WriteText(text));

    private static string? ReadText() => Retry(() => Clipboard.ContainsText() ? Clipboard.GetText() : null);

    private static void WriteText(string text) => Retry(() =>
    {
        Clipboard.SetDataObject(text, copy: true);
        return true;
    });

    /// <summary>Another app may hold the clipboard open for a moment (CLIPBRD_E_CANT_OPEN).</summary>
    private static T? Retry<T>(Func<T?> action)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (ExternalException e) when (attempt < 10)
            {
                Log.Info($"[CLIP] Clipboard busy ({e.Message}), retrying");
                Thread.Sleep(30);
            }
        }
    }
}
