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
}
