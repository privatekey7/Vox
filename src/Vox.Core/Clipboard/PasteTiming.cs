namespace Vox.Core.Clipboard;

/// <summary>How long a paste waits around Ctrl+V, by the app it goes to.</summary>
public static class PasteTiming
{
    /// <summary>
    /// <paramref name="beforePaste"/>: from writing the clipboard to Ctrl+V. <paramref name="beforeRestore"/>:
    /// from Ctrl+V to putting the user's clipboard back (the target reads the clipboard asynchronously).
    /// </summary>
    public readonly record struct Delays(TimeSpan BeforePaste, TimeSpan BeforeRestore);

    public static Delays For(string process) => new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(500));
}
