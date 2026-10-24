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
    public void Dictionary_entries_upsert_by_word()
    {
        using var db = new VoxDatabase(Path.Combine(_dir, "vox_data.db"));
        db.AddDictionaryEntry("кубернетис", "Kubernets", "ru", "auto");
        db.AddDictionaryEntry("кубернетис", "Kubernetes", "ru", "auto");
        Assert.Equal("Kubernetes", db.Corrections()["кубернетис"]);
    }

    // Security audit M4: a plain DELETE left the text in free pages, the WAL and the FTS5 segments.
    [Fact]
    public void Clearing_the_history_leaves_no_dictation_text_in_the_database_files()
    {
        var path = Path.Combine(_dir, "vox_data.db");
        var marker = System.Text.Encoding.UTF8.GetBytes("zorkmidsecret");
        using (var db = new VoxDatabase(path))
        {
            for (var i = 0; i < 50; i++)
            {
                db.AddTranscription($"zorkmidsecret {i}", $" Zorkmidsecret {i} plughword.", "gigaam-v3-rnnt-punct", "ru", 1000);
            }

            db.AddDictionaryEntry("кубернетис", "Kubernetes", "ru", "auto");
            Assert.True(Contains(path, marker)); // otherwise the check below proves nothing

            db.ClearHistory();

            Assert.Empty(db.History());
            Assert.Empty(db.Search("zorkmidsecret"));
            Assert.Equal("Kubernetes", db.Corrections()["кубернетис"]); // the dictionary stays
            db.AddTranscription("после", " После.", "gigaam-v3-rnnt-punct", "ru", 1000); // still works
            Assert.Single(db.Search("после"));
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Assert.False(Contains(path, marker));
        Assert.False(Contains(path, System.Text.Encoding.UTF8.GetBytes("plughword")));
    }

    // The database and its WAL (the -shm file holds no text), read while SQLite may still have them open.
    private static bool Contains(string db, byte[] needle) =>
        new[] { db, db + "-wal" }.Where(File.Exists).Any(f =>
        {
            using var file = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var bytes = new MemoryStream();
            file.CopyTo(bytes);
            return bytes.GetBuffer().AsSpan(0, (int)bytes.Length).IndexOf(needle) >= 0;
        });

    [Fact]
    public void Search_with_quotes_does_not_throw()
    {
        using var db = new VoxDatabase(Path.Combine(_dir, "vox_data.db"));
        Assert.Empty(db.Search("\"OR AND ( *"));
    }
}

public sealed class ImportBackupsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vox-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Only_the_two_newest_backups_stay_and_clearing_deletes_them_all()
    {
        var start = new DateTime(2026, 10, 25, 9, 0, 0);
        var paths = Enumerable.Range(0, 4).Select(i => ImportBackups.PathFor(_dir, start.AddDays(i))).ToList();
        foreach (var p in paths)
        {
            File.WriteAllText(p, "zip");
        }

        File.WriteAllText(Path.Combine(_dir, "vox-перенос-2026-10-25.zip"), "the user's own export");

        Assert.Equal("vox-до-импорта-2026-10-25-090000.zip", Path.GetFileName(paths[0]));
        Assert.Equal(2, ImportBackups.Prune(_dir));
        Assert.Equal([paths[2], paths[3]], Directory.GetFiles(_dir, "vox-до-импорта-*").Order(StringComparer.Ordinal));

        Assert.Equal(2, ImportBackups.DeleteAll(_dir));
        Assert.Equal(["vox-перенос-2026-10-25.zip"], Directory.GetFiles(_dir).Select(Path.GetFileName));
        Assert.Equal(0, ImportBackups.DeleteAll(Path.Combine(_dir, "missing")));
    }
}

public sealed class CrashDumpsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vox-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Clearing_deletes_the_dumps_keeps_the_folder_and_skips_a_locked_one()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // only Windows refuses to delete an open file
        }

        File.WriteAllText(Path.Combine(_dir, "Vox.exe.1234.dmp"), "memory");
        File.WriteAllText(Path.Combine(_dir, "Vox.exe.5678.dmp"), "memory");
        var locked = Path.Combine(_dir, "Vox.exe.9999.dmp");
        File.WriteAllText(locked, "being written");

        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Equal(2, Vox.Core.Diagnostics.CrashDumps.DeleteAll(_dir));
        }

        Assert.True(Directory.Exists(_dir));
        Assert.Equal([locked], Directory.GetFiles(_dir));
        Assert.Equal(1, Vox.Core.Diagnostics.CrashDumps.DeleteAll(_dir));
        Assert.Equal(0, Vox.Core.Diagnostics.CrashDumps.DeleteAll(Path.Combine(_dir, "missing")));
    }
}

public sealed class SettingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vox-test-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Texts_in_the_log_are_off_by_default_and_never_come_from_a_transfer_file()
    {
        var settings = Settings.Load(Path.Combine(_dir, "config.json"));
        Assert.False(settings.LogTexts);

        settings.Merge("""{"log_texts": true, "sounds": false}""");
        Assert.False(settings.LogTexts); // audit L1: an import must not turn it on
        Assert.False(settings.Sounds);

        settings.LogTexts = true;
        Assert.DoesNotContain("log_texts", settings.ForTransfer(), StringComparison.Ordinal);
    }

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
