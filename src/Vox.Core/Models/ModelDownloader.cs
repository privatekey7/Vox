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

/// <summary>Downloads a model archive and installs it atomically into the models folder.</summary>
public sealed class ModelDownloader(HttpClient http, string modelsRoot)
{
    private const int ChunkSize = 256 * 1024;

    public async Task InstallAsync(SpeechModel model, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        var target = ModelCatalog.DirectoryOf(model, modelsRoot);
        if (ModelCatalog.IsInstalled(model, modelsRoot))
        {
            return;
        }

        Directory.CreateDirectory(modelsRoot);
        var archive = Path.Combine(modelsRoot, $"{model.Id}-{Guid.NewGuid():N}.tar.bz2");
        var staging = Path.Combine(modelsRoot, $"{model.Id}-stage-{Guid.NewGuid():N}");
        try
        {
            await DownloadAsync(model.Url, archive, progress, ct).ConfigureAwait(false);

            progress?.Report(new DownloadProgress(0, -1, DownloadStage.Extracting));
            await Task.Run(() => Extract(archive, staging, ct), ct).ConfigureAwait(false);

            var extracted = Path.Combine(staging, model.ExtractedDir);
            if (!Directory.Exists(extracted))
            {
                throw new InvalidDataException($"Archive has no '{model.ExtractedDir}' folder");
            }

            if (Directory.Exists(target))
            {
                Directory.Delete(target, recursive: true); // a broken earlier install
            }

            Directory.Move(extracted, target);
            progress?.Report(new DownloadProgress(1, 1, DownloadStage.Done));
        }
        finally
        {
            TryDelete(() => File.Delete(archive));
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

    private async Task DownloadAsync(Uri url, string path, IProgress<DownloadProgress>? progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1;
        await using var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = File.Create(path);
        var buffer = new byte[ChunkSize];
        long done = 0;
        progress?.Report(new DownloadProgress(0, total, DownloadStage.Downloading));
        int read;
        while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            progress?.Report(new DownloadProgress(done, total, DownloadStage.Downloading));
        }

        if (total > 0 && done != total)
        {
            throw new IOException($"Download interrupted: {done} of {total} bytes");
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
    private static void Extract(string archive, string destination, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);
        using var file = File.OpenRead(archive);
        using var bz = new BZip2InputStream(file);
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
