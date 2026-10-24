using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Vox.Core.Clipboard;
using Vox.Core.Text;

namespace Vox.App.Services;

/// <summary>
/// Puts a dictation at the cursor: clipboard + synthetic Ctrl+V, then gives the user their own
/// clipboard back, every format of it that is plain memory. Clipboard calls run on the UI thread.
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
        var previous = restoreClipboard ? ui.Invoke(SystemClipboard.TrySave) : null;

        // A copy that exists only until the restore stays out of Win+V history and the cloud clipboard.
        // With restoring off the dictation stays as the user's clipboard: an ordinary copy.
        var written = ui.Invoke(() => SystemClipboard.WriteText(body, keepPrivate: restoreClipboard));
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
            return;
        }

        Thread.Sleep(RestoreDelay);
        ui.Invoke(() => Restore(previous, written));
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
