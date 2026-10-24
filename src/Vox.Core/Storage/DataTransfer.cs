using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vox.Core.Audio;
using Vox.Core.Text;

namespace Vox.Core.Storage;

/// <summary>What a transfer file holds, or what an import added.</summary>
public sealed record TransferCounts(int Dictionary, int Names, int History, int Recordings);

/// <summary>A dictionary pair a transfer file adds or changes, shown to the user before the import.</summary>
/// <param name="Was">The target this PC has now; null for a new pair.</param>
public sealed record PairChange(string Wrong, string? Was, string Correct);

/// <summary>What an import would do to the dictionary and what it leaves out (<see cref="DataTransfer.Preview"/>).</summary>
/// <param name="Pairs">Changed pairs first (they replace the user's own), then new ones.</param>
/// <param name="Rejected">Pairs of the file not taken: too long, or with a line break or an invisible character.</param>
/// <param name="PrivacyKeptOff">Settings the file has on that stay off here (<see cref="Settings.PrivacyKeys"/>).</param>
public sealed record TransferPreview(IReadOnlyList<PairChange> Pairs, int Rejected, IReadOnlyList<string> PrivacyKeptOff);

/// <summary>
/// How much a transfer file may hold (security audit L2). A file Vox made always fits: recordings are cut to
/// <see cref="RecordingStore"/>'s limits there, and the history allows years of dictations.
/// </summary>
internal sealed record TransferLimits(long PartBytes, long HistoryBytes, int Entries, int Dictations, long RecordingBytes, int Recordings)
{
    public static TransferLimits Default { get; } = new(
        PartBytes: 50L * 1024 * 1024,
        HistoryBytes: 256L * 1024 * 1024, // Cyrillic is written as \uXXXX: ~1.5 KB a dictation
        Entries: 50_000,
        Dictations: 1_000_000,
        RecordingBytes: RecordingStore.MaxBytes,
        Recordings: RecordingStore.MaxCount);
}

/// <summary>A transfer file read and checked, nothing applied yet (<see cref="DataTransfer.Open"/>).</summary>
public sealed class TransferFile : IDisposable
{
    internal TransferFile(ZipArchive zip) => Zip = zip;

    internal ZipArchive Zip { get; }

    /// <summary>Checked pairs, one per word and language, their time no later than the file.</summary>
    internal List<StoredEntry> Dictionary { get; set; } = [];

    internal int Rejected { get; set; }

    internal List<StoredTargets> Targets { get; set; } = [];

    internal List<ScreenMemory.Entry> Names { get; set; } = [];

    internal string? Settings { get; set; }

    internal List<Transcription> History { get; set; } = [];

    /// <summary>Lines of recordings.tsv with a plain WAV name, and their WAV in the zip.</summary>
    internal List<(string Line, ZipArchiveEntry Wav)> Recordings { get; set; } = [];

    public void Dispose() => Zip.Dispose();
}

/// <summary>
/// Moving what Vox has learned to another PC of the same user (owner, 24.10): one zip with the dictionary
/// (and its target history), remembered screen names, settings, and optionally the dictation history and
/// saved recordings. Models and logs stay.
/// </summary>
/// <remarks>
/// Import merges: a dictionary entry is replaced only by a newer one, dictations and recordings already here
/// are skipped, so the same file can be imported twice without duplicates. The file is written atomically.
/// A file may also come from someone else ("a ready dictionary" in a chat), so the import is
/// <see cref="Open"/> (read and check everything, write nothing), <see cref="Preview"/> (what the user confirms)
/// and <see cref="Import(TransferFile, VoxDatabase, Settings, ScreenMemory, RecordingStore)"/>.
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

    private const int MaxTargets = 10;           // SelfLearning keeps the last 10 targets of an entry
    private const int MaxTag = 100;              // a language, a source, a model id
    private const int MaxDictation = 100_000;    // characters of one dictation's text
    private const long MaxIndexBytes = 16L * 1024 * 1024;

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

    /// <summary>Merges a transfer file into this PC's data without asking; returns what was added.</summary>
    /// <exception cref="InvalidDataException">Not a Vox transfer file, a newer format, a damaged or too big one.</exception>
    public static TransferCounts Import(string path, VoxDatabase db, Settings settings, ScreenMemory names, string recordings)
    {
        using var file = Open(path);
        return Import(file, db, settings, names, new RecordingStore(recordings));
    }

    /// <summary>Reads and checks every part of a transfer file; changes nothing.</summary>
    /// <exception cref="InvalidDataException">Not a Vox transfer file, a newer format, a damaged or too big one.</exception>
    public static TransferFile Open(string path) => Open(path, TransferLimits.Default);

    internal static TransferFile Open(string path, TransferLimits limits)
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

        var file = new TransferFile(zip);
        try
        {
            Check(file, limits);
            return file;
        }
        catch
        {
            file.Dispose();
            throw;
        }
    }

    /// <summary>The pairs an import would add or change here, for the user to confirm.</summary>
    public static TransferPreview Preview(TransferFile file, VoxDatabase db, Settings settings)
    {
        // The same rule as ImportDictionaryEntry: a new word, or a newer time and another target.
        var here = db.DictionaryForExport().ToDictionary(e => (e.Wrong, e.Lang));
        var pairs = new List<PairChange>();
        foreach (var e in file.Dictionary)
        {
            if (!here.TryGetValue((e.Wrong, e.Lang), out var mine))
            {
                pairs.Add(new PairChange(e.Wrong, null, e.Correct));
            }
            else if (e.CreatedAt > mine.CreatedAt && e.Correct != mine.Correct)
            {
                pairs.Add(new PairChange(e.Wrong, mine.Correct, e.Correct));
            }
        }

        return new TransferPreview(
            [.. pairs.OrderBy(p => p.Was is null).ThenBy(p => p.Wrong, StringComparer.Ordinal)],
            file.Rejected,
            file.Settings is null ? [] : settings.PrivacyKeptOff(file.Settings));
    }

    /// <summary>Merges a checked transfer file into this PC's data; returns what was added.</summary>
    /// <remarks>The database part is one transaction; the recordings come last and keep the store's limits.</remarks>
    public static TransferCounts Import(TransferFile file, VoxDatabase db, Settings settings, ScreenMemory names, RecordingStore recordings)
    {
        var (dictionary, history) = db.InOneTransaction(() =>
        {
            var pairs = 0;
            foreach (var e in file.Dictionary)
            {
                pairs += db.ImportDictionaryEntry(e) ? 1 : 0;
            }

            foreach (var t in file.Targets)
            {
                db.ImportTargetHistory(t);
            }

            var dictations = 0;
            foreach (var t in file.History)
            {
                dictations += db.ImportTranscription(t) ? 1 : 0;
            }

            return (pairs, dictations);
        });

        var added = names.Merge(file.Names);
        if (file.Settings is not null)
        {
            settings.Merge(file.Settings);
        }

        var saved = ImportRecordings(file, recordings.Directory);
        recordings.Trim();
        return new TransferCounts(dictionary, added, history, saved);
    }

    // Everything is read here, so a damaged or oversized part stops the import before anything is written.
    private static void Check(TransferFile file, TransferLimits limits)
    {
        var zip = file.Zip;
        var manifest = Read<ManifestData>(zip, Manifest, limits.PartBytes);
        if (manifest?.Format != Format)
        {
            throw new InvalidDataException("not a Vox transfer file");
        }

        if (manifest.Version > Version)
        {
            throw new InvalidDataException("made by a newer Vox");
        }

        // No pair is newer than the file: a time far ahead would beat every fix the user makes later (audit M5).
        var created = manifest.Created.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(manifest.Created, DateTimeKind.Utc) : manifest.Created;
        var latest = Math.Max(0, Math.Min(UnixSeconds(DateTimeOffset.UtcNow), UnixSeconds(new DateTimeOffset(created.ToUniversalTime()))));

        var entries = Read<List<StoredEntry?>>(zip, Dictionary, limits.PartBytes) ?? [];
        Limit(entries.Count, limits.Entries, Dictionary);
        var good = entries.Select(e => Checked(e, latest)).OfType<StoredEntry>().ToList();
        file.Rejected = entries.Count - good.Count;
        file.Dictionary = [.. good.GroupBy(e => (e.Wrong, e.Lang)).Select(g => g.MaxBy(e => e.CreatedAt)!)];

        var targets = Read<List<StoredTargets?>>(zip, Targets, limits.PartBytes) ?? [];
        Limit(targets.Count, limits.Entries, Targets);
        file.Targets = [.. targets.Select(Checked).OfType<StoredTargets>()];

        var names = Read<List<ScreenMemory.Entry?>>(zip, Names, limits.PartBytes) ?? [];
        Limit(names.Count, limits.Entries, Names);
        file.Names = [.. names.OfType<ScreenMemory.Entry>()]; // ScreenMemory.Merge checks the names

        if (ReadPart(zip, SettingsFile, limits.PartBytes) is { } settings)
        {
            file.Settings = Encoding.UTF8.GetString(settings);
            try
            {
                if (JsonNode.Parse(file.Settings) is not JsonObject)
                {
                    throw new InvalidDataException($"damaged part {SettingsFile}");
                }
            }
            catch (JsonException)
            {
                throw new InvalidDataException($"damaged part {SettingsFile}");
            }
        }

        var history = Read<List<Transcription?>>(zip, History, limits.HistoryBytes) ?? [];
        Limit(history.Count, limits.Dictations, History);
        file.History = [.. history.Select(Checked).OfType<Transcription>()];

        file.Recordings = CheckRecordings(zip, limits);
    }

    private static StoredEntry? Checked(StoredEntry? e, double latest) =>
        e is not null && SafeText.IsPairPart(e.Wrong) && SafeText.IsPairPart(e.Correct)
            ? e with
            {
                Lang = Tag(e.Lang, ""),
                Source = Tag(e.Source, "auto"),
                CreatedAt = double.IsFinite(e.CreatedAt) ? Math.Clamp(e.CreatedAt, 0, latest) : 0,
            }
            : null;

    // Earlier targets come back as the entry's target (SelfLearning), so they are checked as pairs are.
    private static StoredTargets? Checked(StoredTargets? t) =>
        t is not null && SafeText.IsPairPart(t.Wrong) && t.Targets is not null
        && t.Targets.Split('\n') is { Length: <= MaxTargets } parts && parts.All(SafeText.IsPairPart)
            ? t with { Lang = Tag(t.Lang, ""), Flips = Math.Max(0, t.Flips) }
            : null;

    private static Transcription? Checked(Transcription? t) =>
        t is { RawText.Length: <= MaxDictation, ProcessedText.Length: <= MaxDictation }
            ? t with { Model = Tag(t.Model, ""), Lang = Tag(t.Lang, "") }
            : null;

    private static string Tag(string? value, string fallback) =>
        value is { Length: <= MaxTag } && SafeText.HasNoHiddenChars(value) ? value : fallback;

    private static void Limit(int count, int max, string part)
    {
        if (count > max)
        {
            throw new InvalidDataException($"too many records in {part}");
        }
    }

    private static double UnixSeconds(DateTimeOffset time) => time.ToUnixTimeMilliseconds() / 1000.0;

    // Lines of recordings.tsv with a plain WAV name that the zip has; at most the store's limits in all.
    private static List<(string Line, ZipArchiveEntry Wav)> CheckRecordings(ZipArchive zip, TransferLimits limits)
    {
        if (ReadPart(zip, RecordingsFolder + RecordingStore.IndexName, MaxIndexBytes) is not { } index)
        {
            return [];
        }

        var lines = Encoding.UTF8.GetString(index).Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.TrimEnd('\r'));
        var found = new List<(string, ZipArchiveEntry)>();
        foreach (var line in lines)
        {
            var file = line.Split('\t')[0];
            if (Path.GetFileName(file) == file && file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                && zip.GetEntry(RecordingsFolder + file) is { } wav)
            {
                found.Add((line, wav)); // not a plain file name: a path inside the zip must not escape the folder
            }
        }

        if (found.Count > limits.Recordings || found.Sum(r => r.Item2.Length) > limits.RecordingBytes)
        {
            throw new InvalidDataException("too many recordings");
        }

        return found;
    }

    // WAVs not here yet, with their lines of recordings.tsv.
    private static int ImportRecordings(TransferFile file, string directory)
    {
        if (file.Recordings.Count == 0)
        {
            return 0;
        }

        Directory.CreateDirectory(directory);
        var indexPath = Path.Combine(directory, RecordingStore.IndexName);
        var added = new List<string>();
        foreach (var (line, wav) in file.Recordings)
        {
            var target = Path.Combine(directory, wav.Name);
            if (File.Exists(target))
            {
                continue; // already here
            }

            using (var to = new FileStream(target, FileMode.CreateNew))
            {
                try
                {
                    using var from = wav.Open();
                    CopyAtMost(from, to, wav.Length, wav.FullName); // no more than the size the check counted
                }
                catch
                {
                    to.Dispose();
                    File.Delete(target);
                    throw;
                }
            }

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

    private static T? Read<T>(ZipArchive zip, string name, long maxBytes)
    {
        if (ReadPart(zip, name, maxBytes) is not { } bytes)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(bytes);
        }
        catch (JsonException)
        {
            throw new InvalidDataException($"damaged part {name}");
        }
    }

    private static byte[]? ReadPart(ZipArchive zip, string name, long maxBytes)
    {
        if (zip.GetEntry(name) is not { } entry)
        {
            return null;
        }

        if (entry.Length > maxBytes)
        {
            throw new InvalidDataException($"part {name} is too big");
        }

        using var from = entry.Open();
        using var to = new MemoryStream();
        CopyAtMost(from, to, maxBytes, name);
        return to.ToArray();
    }

    // The size in the zip's header may lie (a zip bomb): what is actually unpacked is counted.
    private static void CopyAtMost(Stream from, Stream to, long maxBytes, string name)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = from.Read(buffer)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException($"part {name} is too big");
            }

            to.Write(buffer, 0, read);
        }
    }
}
