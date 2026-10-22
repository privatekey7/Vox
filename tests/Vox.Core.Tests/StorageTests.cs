using Vox.Core.Storage;

namespace Vox.Core.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vox-test-").FullName;

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void History_returns_newest_first_and_search_matches_word_prefixes()
    {
        using var db = new VoxDatabase(Path.Combine(_dir, "vox_data.db"));
        db.AddTranscription("а", " Запушь в GitHub.", "gigaam-v3-rnnt-punct", "ru", 1000);
        db.AddTranscription("б", " Переведи 500 рублей.", "gigaam-v3-rnnt-punct", "ru", 1000);

        Assert.Equal(" Переведи 500 рублей.", db.History()[0].ProcessedText);
        Assert.Single(db.Search("рубл"));
        Assert.Empty(db.Search("docker"));
    }

    [Fact]
    public void Learning_candidates_survive_a_restart()
    {
        // Regression: a fix seen once before Vox restarted had to be seen twice again.
        var path = Path.Combine(_dir, "vox_data.db");
        using (var db = new VoxDatabase(path))
        {
            Assert.False(new Vox.Core.Text.CorrectionCandidates(db).Confirm("кубернетис", "Kubernetes"));
        }

        using (var db = new VoxDatabase(path))
        {
            var candidates = new Vox.Core.Text.CorrectionCandidates(db);
            Assert.True(candidates.Confirm("кубернетис", "Kubernetes"));
            Assert.False(candidates.Confirm("кубернетис", "Kubernetes")); // learned: the candidate is gone
        }
    }

    [Fact]
    public void Dictionary_entries_upsert_by_word()
    {
        using var db = new VoxDatabase(Path.Combine(_dir, "vox_data.db"));
        db.AddDictionaryEntry("кубернетис", "Kubernets", "ru", "auto");
        db.AddDictionaryEntry("кубернетис", "Kubernetes", "ru", "auto");
        Assert.Equal("Kubernetes", db.Corrections()["кубернетис"]);
    }

    [Fact]
    public void Search_with_quotes_does_not_throw()
    {
        using var db = new VoxDatabase(Path.Combine(_dir, "vox_data.db"));
        Assert.Empty(db.Search("\"OR AND ( *"));
    }
}

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vox-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Reads_vox1_config_and_keeps_unknown_keys()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, """{"hotkey": "caps lock", "active_model": "parakeet-tdt-0.6b-v3", "language": "multi", "legacy": 1}""");

        var settings = Settings.Load(path);
        Assert.Equal("caps lock", settings.Hotkey);
        settings.Sounds = false;
        settings.Save();

        var text = File.ReadAllText(path);
        Assert.Contains("\"legacy\": 1", text, StringComparison.Ordinal);
        Assert.False(Settings.Load(path).Sounds);
    }

    [Fact]
    public void Missing_or_corrupt_config_gives_defaults()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, "{ not json");
        var settings = Settings.Load(path);
        Assert.Equal(Settings.DefaultHotkey, settings.Hotkey);
        Assert.True(settings.RestoreClipboard);
    }

    [Fact]
    public void Unknown_hotkey_falls_back_to_default()
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, """{"hotkey": "windows"}""");
        Assert.Equal(Settings.DefaultHotkey, Settings.Load(path).Hotkey);
    }
}

public class AppPathsTests
{
    [Fact]
    public void Models_stay_in_appdata_for_ascii_profiles() =>
        Assert.Equal(Path.Combine("/u/ivan/vox", "models"), new AppPaths("/u/ivan/vox", "/pd/Vox").Models);

    [Fact]
    public void Models_move_to_shared_folder_for_non_ascii_profiles() =>
        Assert.Equal(Path.Combine("/pd/Vox", "models"), new AppPaths("/u/Иван/vox", "/pd/Vox").Models);
}
