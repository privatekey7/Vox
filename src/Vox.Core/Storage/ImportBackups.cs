using System.Globalization;

namespace Vox.Core.Storage;

/// <summary>
/// The copy of the user's data made before each import (<see cref="DataTransfer"/>), in the Vox folder:
/// vox-до-импорта-2026-10-25-120000.zip. It holds the whole history, so only the newest <see cref="Keep"/>
/// stay, and clearing the history deletes them all.
/// </summary>
public static class ImportBackups
{
    public const int Keep = 2;
    private const string Prefix = "vox-до-импорта-";

    /// <summary>The path for a backup made at <paramref name="time"/>.</summary>
    public static string PathFor(string root, DateTime time) =>
        Path.Combine(root, $"{Prefix}{time.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}.zip");

    /// <summary>Deletes all but the newest <paramref name="keep"/> backups (names sort by time); returns how many went.</summary>
    public static int Prune(string root, int keep = Keep) =>
        Delete(Find(root).OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(keep));

    /// <summary>Deletes every backup; returns how many went.</summary>
    public static int DeleteAll(string root) => Delete(Find(root));

    private static FileInfo[] Find(string root) =>
        Directory.Exists(root) ? new DirectoryInfo(root).GetFiles(Prefix + "*.zip") : [];

    private static int Delete(IEnumerable<FileInfo> files)
    {
        var n = 0;
        foreach (var file in files)
        {
            file.Delete();
            n++;
        }

        return n;
    }
}
