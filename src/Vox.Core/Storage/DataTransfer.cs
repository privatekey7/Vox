using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Vox.Core.Audio;
using Vox.Core.Text;

namespace Vox.Core.Storage;

/// <summary>What a transfer file holds, or what an import added.</summary>
public sealed record TransferCounts(int Dictionary, int Names, int History, int Recordings);

/// <summary>
/// Moving what Vox has learned to another PC of the same user (owner, 24.10): one zip with the dictionary
/// (and its target history), remembered screen names, settings, and optionally the dictation history and
/// saved recordings. Models and logs stay.
/// </summary>
/// <remarks>
/// Import merges: a dictionary entry is replaced only by a newer one, dictations and recordings already here
/// are skipped, so the same file can be imported twice without duplicates. The file is written atomically.
/// </remarks>
public static class DataTransfer
{
    public const string Format = "vox-transfer";
    public const int Version = 1;

    private const string Manifest = "manifest.json";
    private const string Dictionary = "dictionary.json";
    private const string Targets = "target_history.json";
    private const string Names = "screen_names.json";
    private const string SettingsFile = "settings.json";
    private const string History = "history.json";
    private const string RecordingsFolder = "recordings/";

    private sealed record ManifestData(string Format, int Version, DateTime Created, TransferCounts Counts);

    /// <summary>Are there saved recordings to offer for export?</summary>
    public static bool HasRecordings(string directory) => RecordingStore.Read(directory).Count > 0;

    public static TransferCounts Export(
        string path, VoxDatabase db, Settings settings, ScreenMemory names, bool history, string? recordings)
    {
        var dictionary = db.DictionaryForExport();
        var transcriptions = history ? db.History(int.MaxValue) : [];
        var saved = recordings is not null ? RecordingStore.Read(recordings).Where(r => File.Exists(Path.Combine(recordings, r.File))).ToList() : [];
        var counts = new TransferCounts(dictionary.Count, names.Count, transcriptions.Count, saved.Count);

        var tmp = path + ".tmp";
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            Write(zip, Manifest, new ManifestData(Format, Version, DateTime.UtcNow, counts));
            Write(zip, Dictionary, dictionary);
            Write(zip, Targets, db.TargetHistoryForExport());
            Write(zip, Names, names.Entries);
            WriteText(zip, SettingsFile, settings.ForTransfer());
            if (history)
            {
                Write(zip, History, transcriptions);
            }

            if (recordings is not null && saved.Count > 0)
            {
                foreach (var r in saved)
                {
                    zip.CreateEntryFromFile(Path.Combine(recordings, r.File), RecordingsFolder + r.File, CompressionLevel.Fastest);
                }

                zip.CreateEntryFromFile(Path.Combine(recordings, RecordingStore.IndexName), RecordingsFolder + RecordingStore.IndexName);
            }
        }

        File.Move(tmp, path, overwrite: true);
        return counts;
    }

    /// <summary>Merges a transfer file into this PC's data; returns what was added.</summary>
    /// <exception cref="InvalidDataException">Not a Vox transfer file, a newer format, or a damaged one.</exception>
    public static TransferCounts Import(string path, VoxDatabase db, Settings settings, ScreenMemory names, string recordings)
    {
        ZipArchive zip;
        try
        {
            zip = ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("not a Vox transfer file");
        }

        using (zip)
        {
            var manifest = Read<ManifestData>(zip, Manifest);
            if (manifest?.Format != Format)
            {
                throw new InvalidDataException("not a Vox transfer file");
            }

            if (manifest.Version > Version)
            {
                throw new InvalidDataException("made by a newer Vox");
            }

            var dictionary = 0;
            foreach (var e in Read<List<StoredEntry>>(zip, Dictionary) ?? [])
            {
                dictionary += db.ImportDictionaryEntry(e) ? 1 : 0;
            }

            foreach (var t in Read<List<StoredTargets>>(zip, Targets) ?? [])
            {
                db.ImportTargetHistory(t);
            }

            var added = names.Merge(Read<List<ScreenMemory.Entry>>(zip, Names) ?? []);
            if (zip.GetEntry(SettingsFile) is { } s)
            {
                using var reader = new StreamReader(s.Open(), Encoding.UTF8);
                try
                {
                    settings.Merge(reader.ReadToEnd());
                }
                catch (JsonException)
                {
                    throw new InvalidDataException($"damaged part {SettingsFile}");
                }
            }

            var history = 0;
            foreach (var t in Read<List<Transcription>>(zip, History) ?? [])
            {
                history += db.ImportTranscription(t) ? 1 : 0;
            }

            return new TransferCounts(dictionary, added, history, ImportRecordings(zip, recordings));
        }
    }

    // WAVs not here yet, with their lines of recordings.tsv.
    private static int ImportRecordings(ZipArchive zip, string directory)
    {
        if (zip.GetEntry(RecordingsFolder + RecordingStore.IndexName) is not { } index)
        {
            return 0;
        }

        using var reader = new StreamReader(index.Open(), Encoding.UTF8);
        var lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.TrimEnd('\r')).ToList();
        Directory.CreateDirectory(directory);
        var indexPath = Path.Combine(directory, RecordingStore.IndexName);
        var added = new List<string>();
        foreach (var line in lines)
        {
            var file = line.Split('\t')[0];
            var target = Path.Combine(directory, file);
            if (Path.GetFileName(file) != file || !file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                || File.Exists(target) || zip.GetEntry(RecordingsFolder + file) is not { } wav)
            {
                continue; // already here, or not a plain file name (a path inside the zip must not escape the folder)
            }

            wav.ExtractToFile(target);
            added.Add(line);
        }

        if (added.Count > 0)
        {
            var header = File.Exists(indexPath) ? "" : "file\ttime\tmodel\traw\tfinal\tnote\n";
            File.AppendAllText(indexPath, header + string.Join('\n', added) + "\n", Encoding.UTF8);
        }

        return added.Count;
    }

    private static void Write<T>(ZipArchive zip, string name, T value) => WriteText(zip, name, JsonSerializer.Serialize(value));

    private static void WriteText(ZipArchive zip, string name, string text)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(text);
    }

    private static T? Read<T>(ZipArchive zip, string name)
    {
        if (zip.GetEntry(name) is not { } entry)
        {
            return default;
        }

        try
        {
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<T>(stream);
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"damaged part {name}");
        }
    }
}
