namespace Vox.Core.Diagnostics;

/// <summary>
/// Minidumps Windows Error Reporting writes when Vox crashes natively (vox.iss: LocalDumps\Vox.exe). A dump is
/// a copy of Vox's memory and may hold recent dictations, so clearing the history deletes them too.
/// </summary>
public static class CrashDumps
{
    /// <summary>%LOCALAPPDATA%\vox\crashes, the DumpFolder vox.iss sets (keep in sync).</summary>
    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vox", "crashes");

    /// <summary>
    /// Deletes every file in <paramref name="folder"/>, which stays. A missing folder or a file Windows is still
    /// writing is skipped quietly. Returns how many files went.
    /// </summary>
    public static int DeleteAll(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        var n = 0;
        foreach (var file in new DirectoryInfo(folder).GetFiles())
        {
            try
            {
                file.Delete();
                n++;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Locked by WER or read-only: left for the next clear.
            }
        }

        return n;
    }
}
