namespace Vox.Core.Diagnostics;

/// <summary>How the previous run ended, read from the tail of vox.log.</summary>
public static class RunEnd
{
    public const string Banner = "=== Vox";

    /// <summary>Logged by a normal exit (tray → Выход, the installer).</summary>
    public const string Bye = "[EXIT] Bye";

    /// <summary>
    /// Logged when Windows shuts down, restarts or signs out. Vox may be closed before it logs "Bye":
    /// the laptop's log showed five false "ended without a normal exit" in two days.
    /// </summary>
    public const string SessionEnding = "[EXIT] Windows is ending the session (shutdown, restart or sign-out)";

    /// <summary>False when the last run in <paramref name="logTail"/> ended without logging its exit.</summary>
    public static bool EndedNormally(string logTail) => throw new NotImplementedException();
}
