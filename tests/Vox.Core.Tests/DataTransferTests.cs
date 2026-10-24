using System.IO.Compression;
using Vox.Core.Audio;
using Vox.Core.Storage;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Moving the learned data to another PC (owner, 24.10).</summary>
public sealed class DataTransferTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vox-transfer-" + Guid.NewGuid().ToString("N"));

    public DataTransferTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    private sealed record Pc(VoxDatabase Db, Settings Settings, ScreenMemory Names, string Recordings) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    private Pc NewPc(string name)
    {
        var root = Path.Combine(_dir, name);
        return new Pc(new VoxDatabase(Path.Combine(root, "vox_data.db")), Settings.Load(Path.Combine(root, "config.json")),
            new ScreenMemory(), Path.Combine(root, "recordings"));
    }

    private string File(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Everything_learned_moves_to_another_pc()
    {
        using var home = NewPc("home");
        home.Db.AddDictionaryEntry("кубернетис", "Kubernetes", "", "auto");
        home.Db.SaveTargetHistory("кубернетис", "", ["Kubernetis", "Kubernetes"], 0);
        home.Db.AddTranscription("настрой кубернетис", "Настрой Kubernetes.", "gigaam", "ru", 900);
        home.Names.See(["Zorblax"], DateTime.UtcNow);
        home.Settings.Hotkey = "f9";
        home.Settings.Microphone = "home-mic";
        new RecordingStore(home.Recordings).Save(new float[1600], 16000, DateTime.Now, "gigaam", "привет", "Привет.");

        var exported = DataTransfer.Export(File("t.zip"), home.Db, home.Settings, home.Names, history: true, home.Recordings);

        using var work = NewPc("work");
        work.Settings.Microphone = "work-mic";
        var added = DataTransfer.Import(File("t.zip"), work.Db, work.Settings, work.Names, work.Recordings);

        Assert.Equal(new TransferCounts(1, 1, 1, 1), exported);
        Assert.Equal(new TransferCounts(1, 1, 1, 1), added);
        Assert.Equal("Kubernetes", work.Db.Corrections()["кубернетис"]);
        Assert.Equal(2, work.Db.TargetHistory("кубернетис", "").Targets.Count);
        Assert.Equal("Настрой Kubernetes.", Assert.Single(work.Db.History()).ProcessedText);
        Assert.Equal("Zorblax", Assert.Single(work.Names.Entries).Name);
        Assert.Equal("f9", work.Settings.Hotkey);
        Assert.Equal("work-mic", work.Settings.Microphone); // a device of this PC stays
        Assert.Single(RecordingStore.Read(work.Recordings));
    }

    [Fact]
    public void Importing_the_same_file_twice_adds_nothing()
    {
        using var home = NewPc("home");
        home.Db.AddDictionaryEntry("гитхаб", "GitHub", "", "manual");
        home.Db.AddTranscription("гитхаб", "GitHub", "gigaam", "ru", 500);
        home.Names.See(["Zorblax"], DateTime.UtcNow);
        DataTransfer.Export(File("t.zip"), home.Db, home.Settings, home.Names, history: true, recordings: null);

        using var work = NewPc("work");
        DataTransfer.Import(File("t.zip"), work.Db, work.Settings, work.Names, work.Recordings);
        var again = DataTransfer.Import(File("t.zip"), work.Db, work.Settings, work.Names, work.Recordings);

        Assert.Equal(new TransferCounts(0, 0, 0, 0), again);
        Assert.Single(work.Db.History());
        Assert.Equal(1, Assert.Single(work.Names.Entries).Count);
    }

    [Fact]
    public void A_newer_entry_wins_a_conflict()
    {
        using var home = NewPc("home");
        home.Db.ImportDictionaryEntry(new StoredEntry("вокс", "Vox", "", "manual", 100));
        DataTransfer.Export(File("old.zip"), home.Db, home.Settings, home.Names, history: false, recordings: null);
        home.Db.ImportDictionaryEntry(new StoredEntry("тест", "Test", "", "manual", 300));

        using var work = NewPc("work");
        work.Db.ImportDictionaryEntry(new StoredEntry("вокс", "VOX", "", "auto", 200));
        DataTransfer.Import(File("old.zip"), work.Db, work.Settings, work.Names, work.Recordings);
        Assert.Equal("VOX", work.Db.Corrections()["вокс"]); // the work PC's entry is newer

        work.Db.ImportDictionaryEntry(new StoredEntry("вокс", "Vox", "", "manual", 400));
        Assert.Equal("Vox", work.Db.Corrections()["вокс"]);
    }

    // Vox 1.x (Python) stored times with microseconds; the file carries milliseconds.
    [Fact]
    public void A_dictation_from_vox_1_is_not_duplicated_on_the_way_back()
    {
        using var home = NewPc("home");
        using (var sql = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_dir, "home", "vox_data.db")}"))
        {
            sql.Open();
            using var cmd = sql.CreateCommand();
            cmd.CommandText = "INSERT INTO transcriptions (raw_text, processed_text, created_at) VALUES ('привет', 'Привет.', 1729500000.123456)";
            cmd.ExecuteNonQuery();
        }

        DataTransfer.Export(File("t.zip"), home.Db, home.Settings, home.Names, history: true, recordings: null);
        var again = DataTransfer.Import(File("t.zip"), home.Db, home.Settings, home.Names, home.Recordings);

        Assert.Equal(0, again.History);
        Assert.Single(home.Db.History());
    }

    // A target fixed today (self-learning, Ctrl+S, by hand) is newer than last month's pair from the other PC.
    [Fact]
    public void A_pair_changed_here_after_the_export_beats_the_older_one_from_the_file()
    {
        using var home = NewPc("home");
        home.Db.ImportDictionaryEntry(new StoredEntry("гитхаб", "Github", "", "auto", 200));
        DataTransfer.Export(File("t.zip"), home.Db, home.Settings, home.Names, history: false, recordings: null);

        using var work = NewPc("work");
        work.Db.ImportDictionaryEntry(new StoredEntry("гитхаб", "Гитхаб", "", "auto", 100));
        work.Db.AddDictionaryEntry("гитхаб", "GitHub", "", "auto"); // fixed now
        DataTransfer.Import(File("t.zip"), work.Db, work.Settings, work.Names, work.Recordings);

        Assert.Equal("GitHub", work.Db.Corrections()["гитхаб"]);
    }

    [Fact]
    public void History_is_left_out_when_not_asked()
    {
        using var home = NewPc("home");
        home.Db.AddTranscription("привет", "Привет.", "gigaam", "ru", 500);

        var exported = DataTransfer.Export(File("t.zip"), home.Db, home.Settings, home.Names, history: false, recordings: null);

        Assert.Equal(0, exported.History);
        using var zip = ZipFile.OpenRead(File("t.zip"));
        Assert.Null(zip.GetEntry("history.json"));
    }

    [Fact]
    public void A_file_that_is_not_a_transfer_is_refused()
    {
        using var work = NewPc("work");
        System.IO.File.WriteAllText(File("notes.zip"), "not a zip");
        using (var zip = ZipFile.Open(File("other.zip"), ZipArchiveMode.Create))
        {
            zip.CreateEntry("readme.txt");
        }

        Assert.Throws<InvalidDataException>(() => DataTransfer.Import(File("notes.zip"), work.Db, work.Settings, work.Names, work.Recordings));
        Assert.Throws<InvalidDataException>(() => DataTransfer.Import(File("other.zip"), work.Db, work.Settings, work.Names, work.Recordings));
    }

    [Fact]
    public void A_recording_path_cannot_escape_the_folder()
    {
        using var work = NewPc("work");
        using (var zip = ZipFile.Open(File("evil.zip"), ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(zip.CreateEntry("manifest.json").Open()))
            {
                w.Write("""{"Format":"vox-transfer","Version":1,"Created":"2026-10-24T00:00:00Z","Counts":{"Dictionary":0,"Names":0,"History":0,"Recordings":1}}""");
            }

            zip.CreateEntry("recordings/../evil.wav");
            using var index = new StreamWriter(zip.CreateEntry("recordings/recordings.tsv").Open());
            index.Write("file\ttime\tmodel\traw\tfinal\tnote\n../evil.wav\t2026-10-24 00:00:00\tm\tr\tf\t\n");
        }

        var added = DataTransfer.Import(File("evil.zip"), work.Db, work.Settings, work.Names, work.Recordings);

        Assert.Equal(0, added.Recordings);
        Assert.False(System.IO.File.Exists(Path.Combine(_dir, "work", "evil.wav")));
    }

    // ---- A file from someone else (security audit M5, L1, L2) ----------------------------------

    private const string OldManifest =
        """{"Format":"vox-transfer","Version":1,"Created":"2026-01-01T00:00:00Z","Counts":{"Dictionary":0,"Names":0,"History":0,"Recordings":0}}""";

    // A transfer file made by hand: the manifest of 1 January 2026 and the given parts.
    private string Handmade(string name, params (string Part, string Text)[] parts)
    {
        using (var zip = ZipFile.Open(File(name), ZipArchiveMode.Create))
        {
            foreach (var (part, text) in parts.Prepend(("manifest.json", OldManifest)))
            {
                using var w = new StreamWriter(zip.CreateEntry(part).Open());
                w.Write(text);
            }
        }

        return File(name);
    }

    private static string Pairs(params (string? Wrong, string? Correct, double Time)[] pairs) =>
        System.Text.Json.JsonSerializer.Serialize(pairs.Select(p => new { p.Wrong, p.Correct, Lang = "", Source = "auto", CreatedAt = p.Time }));

    [Fact]
    public void A_pair_with_a_line_break_hidden_characters_or_no_text_is_refused()
    {
        using var work = NewPc("work");
        var path = Handmade("evil.zip", ("dictionary.json", Pairs(
            ("кошелек", "0xabc\r\ncalc", 100),
            ("биржа", "bin\u202Eance", 100),
            ("привет", new string('x', 101), 100),
            (null, "Vox", 100),
            ("вокс", null, 100),
            ("гитхаб", "GitHub", 100))));

        using var file = DataTransfer.Open(path);
        var preview = DataTransfer.Preview(file, work.Db, work.Settings);
        var added = DataTransfer.Import(file, work.Db, work.Settings, work.Names, new RecordingStore(work.Recordings));

        Assert.Equal(5, preview.Rejected);
        Assert.Equal(new PairChange("гитхаб", null, "GitHub"), Assert.Single(preview.Pairs));
        Assert.Equal(1, added.Dictionary);
        Assert.Equal("GitHub", Assert.Single(work.Db.Corrections()).Value);
    }

    // A time far ahead would beat every fix the user makes later; no pair is newer than its file.
    [Fact]
    public void A_pair_from_the_future_does_not_beat_a_fix_made_after_the_file()
    {
        using var work = NewPc("work");
        var path = Handmade("future.zip", ("dictionary.json", Pairs(("кошелек", "0xEvil", 1e15))));
        DataTransfer.Import(path, work.Db, work.Settings, work.Names, work.Recordings);
        work.Db.AddDictionaryEntry("кошелек", "кошелёк", "", "manual"); // the user fixes it

        DataTransfer.Import(path, work.Db, work.Settings, work.Names, work.Recordings);

        Assert.Equal("кошелёк", work.Db.Corrections()["кошелек"]);
        Assert.True(work.Db.DictionaryForExport().Single().CreatedAt > 1.7e9);
    }

    [Fact]
    public void The_preview_lists_replaced_pairs_first_then_new_ones_and_not_older_ones()
    {
        using var work = NewPc("work");
        work.Db.ImportDictionaryEntry(new StoredEntry("вокс", "VOX", "", "auto", 100));
        work.Db.ImportDictionaryEntry(new StoredEntry("гитхаб", "GitHub", "", "auto", 300));
        var path = Handmade("t.zip", ("dictionary.json", Pairs(
            ("тест", "Test", 200), ("вокс", "Vox", 200), ("гитхаб", "Github", 200), ("вокс", "Vox", 150))));

        using var file = DataTransfer.Open(path);
        var preview = DataTransfer.Preview(file, work.Db, work.Settings);

        Assert.Equal(new[] { new PairChange("вокс", "VOX", "Vox"), new PairChange("тест", null, "Test") }, preview.Pairs);
        Assert.Equal(0, preview.Rejected);
    }

    [Fact]
    public void An_import_does_not_switch_on_reading_the_screen_or_keeping_audio()
    {
        using var home = NewPc("home");
        home.Settings.ScreenText = true;
        home.Settings.SaveRecordings = true;
        home.Settings.Sounds = false;
        DataTransfer.Export(File("t.zip"), home.Db, home.Settings, home.Names, history: false, recordings: null);

        using var work = NewPc("work");
        using (var file = DataTransfer.Open(File("t.zip")))
        {
            Assert.Equal(["screen_text", "save_recordings"], DataTransfer.Preview(file, work.Db, work.Settings).PrivacyKeptOff);
            DataTransfer.Import(file, work.Db, work.Settings, work.Names, new RecordingStore(work.Recordings));
        }

        Assert.False(work.Settings.ScreenText);
        Assert.False(work.Settings.SaveRecordings);
        Assert.False(work.Settings.Sounds); // an ordinary setting moves
    }

    [Fact]
    public void An_import_may_switch_privacy_settings_off()
    {
        using var home = NewPc("home");
        home.Settings.ScreenText = false;
        DataTransfer.Export(File("t.zip"), home.Db, home.Settings, home.Names, history: false, recordings: null);

        using var work = NewPc("work");
        work.Settings.ScreenText = true;
        DataTransfer.Import(File("t.zip"), work.Db, work.Settings, work.Names, work.Recordings);

        Assert.False(work.Settings.ScreenText);
    }

    [Fact]
    public void Only_known_settings_of_the_right_kind_are_taken()
    {
        using var work = NewPc("work");
        var path = Handmade("t.zip", ("settings.json",
            """{"hotkey":"bogus","sounds":"no","learn_corrections":false,"microphone":"their-mic","active_model":"m\nx","unknown":1}"""));

        DataTransfer.Import(path, work.Db, work.Settings, work.Names, work.Recordings);

        Assert.Equal(Settings.DefaultHotkey, work.Settings.Hotkey);
        Assert.True(work.Settings.Sounds);
        Assert.False(work.Settings.LearnCorrections);
        Assert.Null(work.Settings.Microphone);
        Assert.Null(work.Settings.ActiveModel);
        Assert.DoesNotContain("unknown", work.Settings.ForTransfer(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_damaged_part_stops_the_import_before_anything_is_written()
    {
        using var work = NewPc("work");
        var path = Handmade("t.zip", ("dictionary.json", Pairs(("гитхаб", "GitHub", 100))), ("history.json", "[{not json"));

        Assert.Throws<InvalidDataException>(() => DataTransfer.Import(path, work.Db, work.Settings, work.Names, work.Recordings));
        Assert.Empty(work.Db.Corrections());
    }

    [Fact]
    public void A_dictation_without_text_is_skipped()
    {
        using var work = NewPc("work");
        var path = Handmade("t.zip", ("history.json",
            """[{"Id":1,"RawText":null,"ProcessedText":"x","Model":"m","Lang":"ru","DurationMs":1,"CreatedAt":"2026-01-01T00:00:00+00:00"},""" +
            """{"Id":2,"RawText":"привет","ProcessedText":"Привет.","Model":null,"Lang":null,"DurationMs":1,"CreatedAt":"2026-01-01T00:00:00+00:00"}]"""));

        var added = DataTransfer.Import(path, work.Db, work.Settings, work.Names, work.Recordings);

        Assert.Equal(1, added.History);
        Assert.Equal("Привет.", Assert.Single(work.Db.History()).ProcessedText);
    }

    [Fact]
    public void An_error_inside_the_transaction_leaves_the_database_as_it_was()
    {
        using var work = NewPc("work");

        Assert.Throws<InvalidOperationException>(() => work.Db.InOneTransaction<int>(() =>
        {
            work.Db.ImportDictionaryEntry(new StoredEntry("гитхаб", "GitHub", "", "auto", 100));
            throw new InvalidOperationException("halfway");
        }));
        work.Db.AddDictionaryEntry("вокс", "Vox", "", "manual"); // the database works on

        Assert.Equal("Vox", Assert.Single(work.Db.Corrections()).Value);
    }

    [Fact]
    public void A_part_bigger_than_the_limit_is_refused()
    {
        var path = Handmade("big.zip", ("dictionary.json", Pairs([.. Enumerable.Range(0, 50).Select(i => ($"слово{i}", $"Word{i}", 100.0))])));

        Assert.Throws<InvalidDataException>(() => DataTransfer.Open(path, TransferLimits.Default with { PartBytes = 1000 }).Dispose());
        DataTransfer.Open(path).Dispose();
    }

    [Fact]
    public void A_file_with_more_records_than_the_limit_is_refused()
    {
        var path = Handmade("many.zip", ("dictionary.json", Pairs(("а", "A", 1), ("б", "B", 1), ("в", "C", 1))));

        Assert.Throws<InvalidDataException>(() => DataTransfer.Open(path, TransferLimits.Default with { Entries = 2 }).Dispose());
    }

    [Fact]
    public void Recordings_over_the_limit_are_refused_and_imported_ones_keep_the_store_limits()
    {
        using var home = NewPc("home");
        var store = new RecordingStore(home.Recordings);
        store.Save(new float[1600], 16000, new DateTime(2026, 1, 1, 10, 0, 0), "m", "раз", "Раз.");
        store.Save(new float[1600], 16000, new DateTime(2026, 1, 1, 10, 0, 1), "m", "два", "Два.");
        DataTransfer.Export(File("t.zip"), home.Db, home.Settings, home.Names, history: false, home.Recordings);

        Assert.Throws<InvalidDataException>(() => DataTransfer.Open(File("t.zip"), TransferLimits.Default with { Recordings = 1 }).Dispose());

        using var work = NewPc("work");
        using var file = DataTransfer.Open(File("t.zip"));
        DataTransfer.Import(file, work.Db, work.Settings, work.Names, new RecordingStore(work.Recordings, maxCount: 1));
        Assert.Equal("Два.", Assert.Single(RecordingStore.Read(work.Recordings)).Final);
    }

    [Fact]
    public void Screen_names_from_a_file_are_checked_and_kept_to_the_limit()
    {
        var names = new ScreenMemory(limit: 2);
        var now = new DateTime(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);
        var added = names.Merge(
        [
            new ScreenMemory.Entry("Zorblax", 3, now.AddDays(-1)),
            new ScreenMemory.Entry("Quarnix", 1, now.AddDays(-2)),
            new ScreenMemory.Entry("Oldname", 1, now.AddDays(-30)),
            new ScreenMemory.Entry(new string('a', ScreenWords.MaxRun + 1), 1, now),
            new ScreenMemory.Entry("Evil\u202Ename", 1, now),
            new ScreenMemory.Entry("Qwen3", 1, now),
            new ScreenMemory.Entry("Future", int.MaxValue, now.AddYears(100)),
        ], now);

        Assert.Equal(4, added);
        Assert.Equal(["Future", "Zorblax"], names.Entries.Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Equal(now, names.Entries.Single(e => e.Name == "Future").Last);
    }

    // Merge takes what the memory keeps itself. A name with digits ("Qwen3") or a pair ("Remotion Studio") is used
    // while on screen and never remembered (ScreenMemory.IsRememberable), so it is not in a transfer file either.
    [Fact]
    public void Every_remembered_screen_name_survives_export_and_import()
    {
        using var home = NewPc("home");
        home.Names.See(["Qwen3", "Remotion Studio", "Remotion", "LMArena", "macOS", "DeepL"], DateTime.UtcNow);
        DataTransfer.Export(File("t.zip"), home.Db, home.Settings, home.Names, history: false, recordings: null);

        using var work = NewPc("work");
        DataTransfer.Import(File("t.zip"), work.Db, work.Settings, work.Names, work.Recordings);

        Assert.Equal(["DeepL", "LMArena", "Remotion", "macOS"], home.Names.Entries.Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Equal(home.Names.Entries.Select(e => e.Name).Order(StringComparer.Ordinal), work.Names.Entries.Select(e => e.Name).Order(StringComparer.Ordinal));
    }
}
