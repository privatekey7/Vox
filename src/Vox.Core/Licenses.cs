namespace Vox.Core;

/// <summary>
/// The license notices Vox ships in the <c>licenses</c> folder next to Vox.exe: BSD (ru_RU), CC BY-SA
/// (common-words), OFL (Golos Text), MIT and Apache-2.0 (packages) ask for them with a binary. The folder is
/// filled at build time from the repository's own files (Vox.Core.csproj), not from copies of their text.
/// </summary>
public static class Licenses
{
    public const string FolderName = "licenses";

    /// <summary>The list of everything Vox is built with (THIRD-PARTY-NOTICES.txt of the repository).</summary>
    public const string Notices = "THIRD-PARTY-NOTICES.txt";

    /// <summary>Vox's own MIT license (LICENSE of the repository).</summary>
    public const string Own = "Vox-LICENSE.txt";

    /// <param name="baseDirectory">The folder of Vox.exe (<see cref="AppContext.BaseDirectory"/>).</param>
    public static string Folder(string baseDirectory) => Path.Combine(baseDirectory, FolderName);

    /// <summary>The notice files present in <paramref name="baseDirectory"/>, by name; empty when the folder is missing.</summary>
    public static IReadOnlyList<string> Files(string baseDirectory)
    {
        var folder = Folder(baseDirectory);
        return Directory.Exists(folder)
            ? [.. Directory.GetFiles(folder).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal)]
            : [];
    }
}
