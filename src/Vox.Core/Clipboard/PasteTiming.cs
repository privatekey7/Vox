namespace Vox.Core.Clipboard;

/// <summary>How long a paste waits around Ctrl+V, by the app it goes to.</summary>
public static class PasteTiming
{
    /// <summary>
    /// <paramref name="BeforePaste"/>: from writing the clipboard to Ctrl+V. <paramref name="BeforeRestore"/>:
    /// from Ctrl+V to putting the user's clipboard back (the target reads the clipboard asynchronously).
    /// </summary>
    public readonly record struct Delays(TimeSpan BeforePaste, TimeSpan BeforeRestore);

    private static readonly Delays Quick = new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(500));

    // RustDesk sends the clipboard to the other computer every 333 ms (CLIPBOARD_INTERVAL) but a key at
    // once: Ctrl+V has to wait for a sync, and the restore for the other computer to read it.
    private static readonly Delays RustDesk = new(TimeSpan.FromMilliseconds(700), TimeSpan.FromMilliseconds(1500));

    /// <param name="process">The foreground process name, as the log shows it ("rustdesk", "chrome").</param>
    public static Delays For(string process) =>
        process.Equals("rustdesk", StringComparison.OrdinalIgnoreCase) ? RustDesk : Quick;
}
