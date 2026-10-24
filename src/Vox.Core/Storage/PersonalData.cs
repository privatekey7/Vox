namespace Vox.Core.Storage;

/// <summary>
/// «Удалить мои данные»: everything Vox keeps for the current Windows user. The app does this, not the
/// uninstaller: an admin-mode uninstaller runs as the administrator, who may be another account, so its
/// %APPDATA% is not this user's (docs/adr/034).
/// </summary>
public static class PersonalData
{
    /// <summary>
    /// %APPDATA%\vox (history, dictionary, settings, logs, recordings, backups, screen names and, for an
    /// ASCII profile path, the models) and %LOCALAPPDATA%\vox (crash dumps). The shared models folder in
    /// %ProgramData% is not listed: other users of this PC may load the same models from it.
    /// </summary>
    public static IReadOnlyList<string> Folders(AppPaths paths) =>
        paths.LocalRoot is { } local ? [paths.Root, local] : [paths.Root];

    /// <summary>Deletes <see cref="Folders"/>. Vox must have closed its database and log first.</summary>
    /// <returns>The folders that are still there (a file in use, no access).</returns>
    public static IReadOnlyList<string> Delete(AppPaths paths)
    {
        var left = new List<string>();
        foreach (var folder in Folders(paths))
        {
            if (!TryDelete(folder))
            {
                left.Add(folder);
            }
        }

        return left;
    }

    private static bool TryDelete(string folder)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!Directory.Exists(folder))
            {
                return true;
            }

            try
            {
                Directory.Delete(folder, recursive: true);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                ClearReadOnly(folder); // Directory.Delete refuses read-only files
            }
            catch (IOException)
            {
                // A file still in use: one retry, then report the folder.
            }
        }

        return !Directory.Exists(folder);
    }

    private static void ClearReadOnly(string folder)
    {
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", options))
            {
                if (file.IsReadOnly)
                {
                    file.IsReadOnly = false;
                }
            }
        }
        catch (IOException)
        {
            // Reported by the caller as a folder that is still there.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }
    }
}
