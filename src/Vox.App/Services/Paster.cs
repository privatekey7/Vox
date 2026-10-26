using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Vox.Core.Clipboard;
using Vox.Core.Text;

namespace Vox.App.Services;

/// <summary>
/// Puts a dictation at the cursor: clipboard + synthetic Ctrl+V, then gives the user their own
/// clipboard back, every format of it that is plain memory. If another app keeps the clipboard
/// open, the text is typed instead. Clipboard calls run on the UI thread.
/// </summary>
internal sealed class Paster(Dispatcher ui)
{
    // The target app reads the clipboard asynchronously after Ctrl+V (PasteTiming).
    // debt: fixed delays; a very busy app could read after the restore. Upgrade path: delayed
    // clipboard rendering (WM_RENDERFORMAT) to learn exactly when the paste happened.

    /// <summary>False if the text did not reach the cursor (it is still in the history).</summary>
    public bool Paste(string text, bool restoreClipboard)
    {
        var (separator, body) = Spacing.SplitLeadingSeparator(text);
        ClipboardSnapshot? previous;
        uint written;
        try
        {
            previous = restoreClipboard ? ui.Invoke(SystemClipboard.TrySave) : null;

            // A copy that exists only until the restore stays out of Win+V history and the cloud clipboard.
            // With restoring off the dictation stays as the user's clipboard: an ordinary copy.
            written = ui.Invoke(() => SystemClipboard.WriteText(body, keepPrivate: restoreClipboard));
        }
        catch (Win32Exception e)
        {
            // A remote desktop syncing the clipboard can hold it for seconds; the dictation is not lost.
            Log.Info(e is SystemClipboard.ClipboardBusyException busy
                ? $"[CLIP] {busy.Holder} kept the clipboard busy; typing the text instead"
                : $"[CLIP] Could not write the clipboard ({e.Message}); typing the text instead");
            return Type(text);
        }

        Log.Info(restoreClipboard ? "[CLIP] Copied to clipboard (kept out of clipboard history)" : "[CLIP] Copied to clipboard");
        var (target, elevated) = Native.ForegroundApp();
        var delays = PasteTiming.For(target);
        Thread.Sleep(delays.BeforePaste);

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
        var sent = Native.SendInput((uint)keys.Count, [.. keys], Marshal.SizeOf<Native.INPUT>());
        Log.Info(sent == keys.Count ? $"[PASTE] Pasted into {target}" : $"[PASTE] SendInput sent {sent} of {keys.Count} keys to {target}");
        var blocked = elevated && !Environment.IsPrivilegedProcess;
        if (blocked)
        {
            // Windows drops keys sent to an admin window without any error (UIPI).
            Log.Info($"[PASTE] {target} probably runs as administrator — Windows blocks the paste; the text stays in the clipboard");
        }

        if (previous is null || blocked)
        {
            return sent == keys.Count;
        }

        Thread.Sleep(delays.BeforeRestore);
        ui.Invoke(() => Restore(previous, written));
        return sent == keys.Count;
    }

    /// <summary>
    /// Types the text key by key (KEYEVENTF_UNICODE: any layout, any letter) without the clipboard.
    /// Slower than Ctrl+V for a long text, and a few apps (games, some remote desktops) ignore such keys.
    /// </summary>
    private static bool Type(string text)
    {
        var typed = TypedText.ForTyping(text);
        var keys = new Native.INPUT[typed.Length * 2];
        for (var i = 0; i < typed.Length; i++)
        {
            keys[2 * i] = Native.Char(typed[i], up: false);
            keys[(2 * i) + 1] = Native.Char(typed[i], up: true);
        }

        var (target, elevated) = Native.ForegroundApp();
        var sent = Native.SendInput((uint)keys.Length, keys, Marshal.SizeOf<Native.INPUT>());
        Log.Info(sent == keys.Length ? $"[PASTE] Typed into {target}" : $"[PASTE] SendInput typed {sent} of {keys.Length} keys into {target}");
        if (elevated && !Environment.IsPrivilegedProcess)
        {
            Log.Info($"[PASTE] {target} probably runs as administrator — Windows blocks the typed text");
            return false;
        }

        return sent == keys.Length;
    }

    /// <summary>
    /// Copy without pasting (tray: "Скопировать последнюю диктовку", history: "Копировать"). The user
    /// asked for it, so it is an ordinary copy that may go to clipboard history.
    /// </summary>
    public bool Copy(string text)
    {
        try
        {
            ui.Invoke(() => SystemClipboard.WriteText(text, keepPrivate: false));
            return true;
        }
        catch (Win32Exception e)
        {
            Log.Info($"[CLIP] Could not copy ({e.Message}{(e is SystemClipboard.ClipboardBusyException busy ? $", held by {busy.Holder}" : "")})");
            return false;
        }
    }

    private static void Restore(ClipboardSnapshot previous, uint written)
    {
        if (SystemClipboard.SequenceNumber != written)
        {
            Log.Info("[CLIP] Clipboard changed after the paste; not restoring");
            return; // the user (or an app) copied something new meanwhile
        }

        try
        {
            // The saved formats include the markers a password manager put there, so a password comes
            // back still out of history and the cloud.
            SystemClipboard.Restore(previous);
            var skipped = previous.Skipped > 0 ? $", {previous.Skipped} skipped" : "";
            var marked = previous.IsPrivate ? ", kept out of clipboard history" : "";
            Log.Info($"[CLIP] Restored previous clipboard ({previous.Items.Count} formats{skipped}{marked})");
        }
        catch (Win32Exception e)
        {
            Log.Info($"[CLIP] Could not restore the clipboard ({e.Message})");
        }
    }
}
