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
    // The target app reads the clipboard asynchronously after Ctrl+V.
    // debt: fixed delay; a very busy app could read after the restore. Upgrade path: delayed
    // clipboard rendering (WM_RENDERFORMAT) to learn exactly when the paste happened.
    private static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(500);

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
        catch (SystemClipboard.ClipboardBusyException e)
        {
            // A remote desktop syncing the clipboard can hold it for seconds; the dictation is not lost.
            Log.Info($"[CLIP] {e.Holder} kept the clipboard busy; typing the text instead");
            return Type(text);
        }

        Log.Info(restoreClipboard ? "[CLIP] Copied to clipboard (kept out of clipboard history)" : "[CLIP] Copied to clipboard");
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

        if (previous is null || blocked)
        {
            return sent == keys.Count;
        }

        Thread.Sleep(RestoreDelay);
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
    public void Copy(string text) => ui.Invoke(() => SystemClipboard.WriteText(text, keepPrivate: false));

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
