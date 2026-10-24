using System.Security.Cryptography;
using ICSharpCode.SharpZipLib.BZip2;
using ICSharpCode.SharpZipLib.Tar;

namespace Vox.Core.Models;

/// <summary>Progress of a model download: bytes so far, total (or -1), and the stage.</summary>
public readonly record struct DownloadProgress(long Done, long Total, DownloadStage Stage);

public enum DownloadStage
{
    Downloading,
    Extracting,
    Done,
}

/// <summary>
/// Downloads a model archive, checks it against its pin and installs it atomically into the models folder (D034).
/// </summary>
public sealed class ModelDownloader(HttpClient http, string modelsRoot)
{
    private const int ChunkSize = 256 * 1024;
    private const long MiB = 1024 * 1024;

    private const string NotTheFile =
        "Скачанный файл модели не совпал с ожидаемым: он повреждён или подменён. Vox его удалил, попробуйте скачать ещё раз.";

    public async Task InstallAsync(SpeechModel model, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var target = ModelCatalog.DirectoryOf(model, modelsRoot);
        var rejectLinks = ModelFiles.IsSharedFolder(modelsRoot);
        if (ModelCatalog.IsInstalled(model, modelsRoot)
            && await Task.Run(() => ModelFiles.IsIntact(model, modelsRoot, rejectLinks), ct).ConfigureAwait(false))
        {
            return;
        }

        RemoveBroken(target); // before the download: a folder Vox cannot delete would waste it

        Directory.CreateDirectory(modelsRoot);
        var archivePath = Path.Combine(modelsRoot, $"{model.Id}-{Guid.NewGuid():N}.tar.bz2");
        var staging = Path.Combine(modelsRoot, $"{model.Id}-stage-{Guid.NewGuid():N}");
        try
        {
            // FileShare.None: nobody can swap the archive between the hash check and unpacking.
            // DeleteOnClose: it is gone even if Vox is killed half-way through the download.
            await using (var archive = new FileStream(archivePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
                ChunkSize, FileOptions.DeleteOnClose))
            {
                await DownloadAsync(model, archive, progress, ct).ConfigureAwait(false);

                progress?.Report(new DownloadProgress(0, -1, DownloadStage.Extracting));
                archive.Position = 0;
                await Task.Run(() => Extract(archive, staging, ct), ct).ConfigureAwait(false);
            }

            var extracted = Path.Combine(staging, model.ExtractedDir);
            if (!Directory.Exists(extracted))
            {
                throw new InvalidDataException($"Archive has no '{model.ExtractedDir}' folder");
            }

            // The archive matched its pin, so this fails only if a file pin in the catalog is wrong.
            await Task.Run(() => ModelFiles.OpenVerified(model, staging, rejectLinks).Dispose(), ct).ConfigureAwait(false);

            RemoveBroken(target); // appeared while downloading
            Directory.Move(extracted, target);
            progress?.Report(new DownloadProgress(1, 1, DownloadStage.Done));
        }
        finally
        {
            TryDelete(() => File.Delete(archivePath)); // DeleteOnClose did it; this is for a failed open
            TryDelete(() => Directory.Delete(staging, recursive: true));
        }
    }

    public void Uninstall(SpeechModel model)
    {
        var dir = ModelCatalog.DirectoryOf(model, modelsRoot);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private async Task DownloadAsync(SpeechModel model, FileStream file, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var expected = model.Archive.Size;
        using var response = await http.GetAsync(model.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length != expected)
        {
            throw new ModelIntegrityException(NotTheFile);
        }

        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[ChunkSize];
        long done = 0;
        progress?.Report(new DownloadProgress(0, expected, DownloadStage.Downloading));
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            done += read;
            if (done > expected)
            {
                throw new ModelIntegrityException(NotTheFile); // stop before it fills the disk
            }

            sha.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            progress?.Report(new DownloadProgress(done, expected, DownloadStage.Downloading));
        }

        if (done != expected)
        {
            throw new IOException($"Загрузка оборвалась: скачано {done / MiB} из {expected / MiB} МБ.");
        }

        if (Convert.ToHexStringLower(sha.GetHashAndReset()) != model.Archive.Sha256)
        {
            throw new ModelIntegrityException(NotTheFile);
        }
    }

    /// <summary>Removes a model folder that failed its check; one Vox may not delete (another user's) is reported.</summary>
    private static void RemoveBroken(string target)
    {
        if (!Directory.Exists(target))
        {
            return;
        }

        try
        {
            Directory.Delete(target, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new ModelIntegrityException(
                $"Файлы модели в папке {target} изменены, и Vox не может их удалить. Возможно, их положил другой " +
                "пользователь этого компьютера. Удалите папку от имени администратора и попробуйте ещё раз.", e);
        }
    }

    /// <summary>
    /// Deletes archives and staging folders an install left behind when Vox was closed mid-way
    /// (<c>{id}-{guid}.tar.bz2</c>, <c>{id}-stage-{guid}</c>). Installed models are not touched.
    /// </summary>
    public static void RemoveLeftovers(string modelsRoot)
    {
        if (!Directory.Exists(modelsRoot))
        {
            return;
        }

        foreach (var model in ModelCatalog.All)
        {
            foreach (var archive in Directory.EnumerateFiles(modelsRoot, $"{model.Id}-*.tar.bz2"))
            {
                TryDelete(() => File.Delete(archive));
            }

            foreach (var staging in Directory.EnumerateDirectories(modelsRoot, $"{model.Id}-stage-*"))
            {
                TryDelete(() => Directory.Delete(staging, recursive: true));
            }
        }
    }

    // Cancellable per entry, so closing the wizard stops a long extraction and the finally cleans up.
    private static void Extract(Stream archive, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        using var bz = new BZip2InputStream(archive) { IsStreamOwner = false }; // the caller closes (and so deletes) it
        using var tar = TarArchive.CreateInputTarArchive(bz, System.Text.Encoding.UTF8);
        tar.ProgressMessageEvent += (_, _, _) => ct.ThrowIfCancellationRequested();
        tar.ExtractContents(destination, allowParentTraversal: false);
    }

    private static void TryDelete(Action delete)
    {
        try
        {
            delete();
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
