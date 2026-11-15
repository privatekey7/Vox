using System.Text.Json;
using Vox.Core.Models;

namespace Vox.Core.Tests;

/// <summary>
/// The license notices travel with the build: this test project references Vox.Core like Vox.App does, so
/// its output folder gets the same <c>licenses</c> folder the installer ships.
/// </summary>
public sealed class LicensesTests
{
    private static readonly string Shipped = Licenses.Folder(AppContext.BaseDirectory);

    [Fact]
    public void Notices_ship_next_to_the_program()
    {
        var files = Licenses.Files(AppContext.BaseDirectory);

        Assert.Contains(Licenses.Own, files);
        Assert.Contains(Licenses.Notices, files);
        Assert.Contains("OFL-GolosText.txt", files);
        Assert.All(files, f => Assert.True(new FileInfo(Path.Combine(Shipped, f)).Length > 200, f));
    }

    [Fact]
    public void Own_license_is_the_repository_one()
    {
        Assert.Equal(File.ReadAllText(Path.Combine(RepositoryRoot(), "LICENSE")), File.ReadAllText(Path.Combine(Shipped, Licenses.Own)));
        Assert.Contains("MIT License", File.ReadAllText(Path.Combine(Shipped, Licenses.Own)), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_license_file_in_the_sources_ships()
    {
        // A new bundled word list or font brings its license file: it must reach the installer too.
        var src = Path.Combine(RepositoryRoot(), "src");
        var sources = Directory.GetFiles(src, "*.LICENSE.md", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(src, "OFL*.txt", SearchOption.AllDirectories))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.True(sources.Count >= 4, $"only {sources.Count} license files found in src");
        foreach (var source in sources)
        {
            var shipped = Path.Combine(Shipped, Path.ChangeExtension(Path.GetFileName(source), ".txt"));
            Assert.True(File.Exists(shipped), $"{Path.GetFileName(source)} is not in the licenses folder");
            Assert.Equal(File.ReadAllText(source), File.ReadAllText(shipped));
        }
    }

    [Fact]
    public void Every_shipped_package_is_named_in_the_notices()
    {
        // The lock file of Vox.App lists everything NuGet puts into the published program, transitive
        // packages too. A package added without a line in THIRD-PARTY-NOTICES.txt fails here.
        var notices = File.ReadAllText(Path.Combine(Shipped, Licenses.Notices));
        using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Vox.App", "packages.lock.json")));
        var packages = lockFile.RootElement.GetProperty("dependencies").EnumerateObject()
            .SelectMany(framework => framework.Value.EnumerateObject())
            .Where(p => p.Value.GetProperty("type").GetString() != "Project")
            .Select(p => p.Name)
            .Where(IsShipped)
            .Distinct()
            .ToList();

        Assert.True(packages.Count >= 10, $"only {packages.Count} packages read from the lock file");
        Assert.Equal([], packages.Where(p => !notices.Contains(p, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Every_model_of_the_catalog_is_named_in_the_notices()
    {
        var notices = File.ReadAllText(Path.Combine(Shipped, Licenses.Notices));

        Assert.All(ModelCatalog.All, m => Assert.Contains(m.ExtractedDir, notices, StringComparison.Ordinal));
    }

    [Fact]
    public void Missing_folder_gives_no_files() =>
        Assert.Empty(Licenses.Files(Path.Combine(AppContext.BaseDirectory, "no-such-folder")));

    // sherpa-onnx lists its native runtime for every platform; Vox publishes win-x64 only.
    private static bool IsShipped(string package) =>
        !package.StartsWith("org.k2fsa.sherpa.onnx.runtime.", StringComparison.Ordinal) || package.EndsWith(".win-x64", StringComparison.Ordinal);

    private static string RepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Vox.sln")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        return root;
    }
}
