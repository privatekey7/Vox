using System.Diagnostics;
using System.Text.RegularExpressions;
using Vox.Core.Models;

namespace Vox.Core.Tests;

/// <summary>A fact that needs Windows file semantics (locks on folders, junctions); skipped on the Linux CI.</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip = "Windows only";
        }
    }
}

public sealed partial class ModelFilesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("vox-models-").FullName;
    private readonly ModelDownloaderTests.Fixture _fixture = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Matching_files_pass()
    {
        Write(_root);

        Assert.True(ModelFiles.IsIntact(_fixture.Model, _root, rejectLinks: false));
    }

    [Fact]
    public void Planted_file_with_the_right_name_and_size_is_refused()
    {
        var dir = Write(_root);
        var tokens = Path.Combine(dir, "tokens.txt");
        var planted = File.ReadAllBytes(tokens);
        planted[0] ^= 0x01;
        File.WriteAllBytes(tokens, planted);

        var error = Assert.Throws<ModelIntegrityException>(() => ModelFiles.OpenVerified(_fixture.Model, _root, rejectLinks: false));
        Assert.Contains("tokens.txt", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_file_is_refused()
    {
        var dir = Write(_root);
        File.Delete(Path.Combine(dir, "joiner.onnx"));

        Assert.False(ModelFiles.IsIntact(_fixture.Model, _root, rejectLinks: false));
    }

    [WindowsFact]
    public void Checked_files_cannot_be_changed_or_moved_until_released()
    {
        // Between the check and sherpa-onnx opening the files, another user must not swap them.
        var dir = Write(_root);
        var encoder = Path.Combine(dir, "encoder.onnx");

        using (ModelFiles.OpenVerified(_fixture.Model, _root, rejectLinks: false))
        {
            Assert.Equal("encoder bytes", File.ReadAllText(encoder)); // sherpa-onnx can still read
            Assert.ThrowsAny<IOException>(() => File.WriteAllText(encoder, "evil"));
            Assert.ThrowsAny<Exception>(() => Directory.Move(dir, dir + "-old"));
        }

        File.WriteAllText(encoder, "free again");
    }

    [WindowsFact]
    public void File_held_open_for_writing_is_refused()
    {
        var dir = Write(_root);
        using var writer = new FileStream(Path.Combine(dir, "decoder.onnx"), FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

        Assert.False(ModelFiles.IsIntact(_fixture.Model, _root, rejectLinks: false));
    }

    [WindowsFact]
    public void Model_behind_a_junction_is_refused_in_the_shared_folder()
    {
        // Another user who owns the parent folder could point the junction elsewhere after the check.
        var real = Path.Combine(_root, "real");
        Write(real);
        var link = Path.Combine(_root, "models");
        Junction(link, real);
        try
        {
            Assert.True(ModelFiles.IsIntact(_fixture.Model, link, rejectLinks: false));
            Assert.False(ModelFiles.IsIntact(_fixture.Model, link, rejectLinks: true));
            Assert.True(ModelFiles.IsIntact(_fixture.Model, real, rejectLinks: true));
        }
        finally
        {
            Directory.Delete(link); // the junction only, not the folder it points to
        }
    }

    [Fact]
    public void ProgramData_is_the_shared_folder()
    {
        var shared = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

        Assert.True(ModelFiles.IsSharedFolder(Path.Combine(shared, "Vox", "models")));
        Assert.False(ModelFiles.IsSharedFolder(_root));
        Assert.False(ModelFiles.IsSharedFolder(shared + "Other"));
    }

    [Fact]
    public void Every_model_has_pins_for_its_archive_and_the_files_it_loads()
    {
        foreach (var model in ModelCatalog.Experimental)
        {
            Assert.Equal(model.Url.Segments[^1], model.Archive.Path);
            Assert.All([model.Archive, .. model.Files], pin =>
            {
                Assert.Matches(Sha256Hex(), pin.Sha256);
                Assert.True(pin.Size > 0);
            });
            var paths = model.Files.Select(f => f.Path).ToList();
            Assert.Contains(model.Encoder, paths);
            Assert.Contains(model.Decoder, paths);
            Assert.Contains(model.Joiner, paths);
            Assert.Contains(paths, p => p == model.Tokens || p.StartsWith(model.Tokens + "/", StringComparison.Ordinal));
            Assert.InRange(model.Archive.Size / 1048576.0, model.DownloadMb * 0.9, model.DownloadMb * 1.1);
        }
    }

    private string Write(string modelsRoot)
    {
        var dir = Directory.CreateDirectory(ModelCatalog.DirectoryOf(_fixture.Model, modelsRoot)).FullName;
        using var http = new HttpClient();
        var text = new Dictionary<string, string>
        {
            ["encoder.onnx"] = "encoder bytes",
            ["decoder.onnx"] = "decoder bytes",
            ["joiner.onnx"] = "joiner bytes",
            ["tokens.txt"] = "<blk> 0\nа 1\n",
        };
        foreach (var (name, content) in text)
        {
            File.WriteAllText(Path.Combine(dir, name), content);
        }

        return dir;
    }

    private static void Junction(string link, string target)
    {
        // mklink /J needs no admin rights, unlike a symbolic link.
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        mklink.WaitForExit();
        Assert.Equal(0, mklink.ExitCode);
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();
}
