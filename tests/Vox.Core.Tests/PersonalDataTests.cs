using Vox.Core.Storage;

namespace Vox.Core.Tests;

public sealed class PersonalDataTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vox-personal-").FullName;

    public void Dispose()
    {
        foreach (var file in new DirectoryInfo(_dir).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            file.IsReadOnly = false;
        }

        Directory.Delete(_dir, recursive: true);
    }

    private AppPaths Paths(string user = "ivan") =>
        new(Path.Combine(_dir, user, "Roaming", "vox"), Path.Combine(_dir, "ProgramData", "Vox"), Path.Combine(_dir, user, "Local", "vox"));

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
    }

    [Fact]
    public void Deletes_appdata_and_crash_dumps_with_everything_inside()
    {
        var paths = Paths();
        Touch(paths.Config);
        Touch(paths.LogFile);
        Touch(Path.Combine(paths.Recordings, "1.wav"));
        Touch(Path.Combine(paths.Models, "gigaam", "tokens.txt"));
        Touch(Path.Combine(paths.LocalRoot!, "crashes", "Vox.exe.1.dmp"));
        File.SetAttributes(paths.Config, FileAttributes.ReadOnly);

        Assert.Empty(PersonalData.Delete(paths));
        Assert.False(Directory.Exists(paths.Root));
        Assert.False(Directory.Exists(paths.LocalRoot));
    }

    [Fact]
    public void Deletes_a_closed_database()
    {
        var paths = Paths();
        using (var db = new VoxDatabase(paths.Database))
        {
            db.AddTranscription("а", "б", "m", "ru", 1);
        }

        Assert.Empty(PersonalData.Delete(paths));
        Assert.False(File.Exists(paths.Database));
    }

    [Fact]
    public void Leaves_the_models_other_users_share()
    {
        var paths = Paths("Иван");
        Touch(paths.Config);
        Touch(Path.Combine(paths.Models, "gigaam", "tokens.txt"));

        Assert.True(paths.SharedModels);
        Assert.DoesNotContain(PersonalData.Folders(paths), f => paths.Models.StartsWith(f, StringComparison.OrdinalIgnoreCase));
        Assert.Empty(PersonalData.Delete(paths));
        Assert.False(Directory.Exists(paths.Root));
        Assert.True(File.Exists(Path.Combine(paths.Models, "gigaam", "tokens.txt")));
    }

    [Fact]
    public void Nothing_to_delete_is_not_an_error() => Assert.Empty(PersonalData.Delete(Paths()));

    [Fact]
    public void Reports_a_folder_with_a_file_in_use()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // only Windows refuses to delete an open file
        }

        var paths = Paths();
        Touch(paths.LogFile);
        using (new FileStream(paths.LogFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal([paths.Root], PersonalData.Delete(paths));
        }
    }
}
