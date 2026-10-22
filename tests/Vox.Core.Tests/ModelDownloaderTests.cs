using Vox.Core.Models;

namespace Vox.Core.Tests;

public sealed class ModelDownloaderTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("vox-models-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Leftovers_of_an_interrupted_install_are_removed()
    {
        // Regression: closing the wizard during extraction left the archive and a half-extracted folder.
        var id = ModelCatalog.GigaAm.Id;
        File.WriteAllText(Path.Combine(_root, $"{id}-0123abcd.tar.bz2"), "partial");
        Directory.CreateDirectory(Path.Combine(_root, $"{id}-stage-0123abcd", "half"));
        var installed = Directory.CreateDirectory(ModelCatalog.DirectoryOf(ModelCatalog.Parakeet, _root)).FullName;
        File.WriteAllText(Path.Combine(_root, "notes.txt"), "not ours");

        ModelDownloader.RemoveLeftovers(_root);

        Assert.Equal(["notes.txt"], Directory.GetFiles(_root).Select(Path.GetFileName));
        Assert.Equal([installed], Directory.GetDirectories(_root));
    }

    [Fact]
    public void Missing_models_folder_is_fine() => ModelDownloader.RemoveLeftovers(Path.Combine(_root, "absent"));
}
