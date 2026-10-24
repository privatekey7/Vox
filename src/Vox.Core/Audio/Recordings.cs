using System.Globalization;
using System.Text;

namespace Vox.Core.Audio;

/// <summary>16-bit PCM mono WAV files.</summary>
public static class Wav
{
    public static void Write(string path, float[] samples, int sampleRate)
    {
        using var writer = new BinaryWriter(File.Create(path));
        var bytes = samples.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + bytes);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write((short)1); // mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(bytes);
        foreach (var s in samples)
        {
            writer.Write((short)Math.Round(Math.Clamp(s, -1f, 1f) * short.MaxValue));
        }
    }

    /// <summary>Mono 16-bit PCM (what <see cref="Write"/> makes) as float samples in [-1, 1].</summary>
    public static (float[] Samples, int SampleRate) Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
        {
            throw new InvalidDataException($"{path}: not a WAV file");
        }

        reader.ReadInt32();
        reader.ReadBytes(4); // WAVE
        int rate = 0, channels = 0, bits = 0;
        while (reader.BaseStream.Position + 8 <= reader.BaseStream.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                var format = reader.ReadInt16();
                channels = reader.ReadInt16();
                rate = reader.ReadInt32();
                reader.ReadBytes(6);
                bits = reader.ReadInt16();
                reader.ReadBytes(size - 16);
                if (format != 1 || channels != 1 || bits != 16)
                {
                    throw new InvalidDataException($"{path}: only 16-bit PCM mono is read");
                }
            }
            else if (id == "data")
            {
                var count = Math.Min(size, (int)(reader.BaseStream.Length - reader.BaseStream.Position)) / 2;
                var samples = new float[count];
                for (var i = 0; i < count; i++)
                {
                    samples[i] = reader.ReadInt16() / 32768f;
                }

                return (samples, rate);
            }
            else
            {
                reader.ReadBytes(size + (size & 1));
            }
        }

        throw new InvalidDataException($"{path}: no audio data");
    }
}

/// <summary>Band-limited resampling (windowed sinc), e.g. the microphone's 48 kHz to the models' 16 kHz.</summary>
public static class Resampler
{
    private const int HalfWidth = 16; // filter taps on each side, in input samples at the lower rate

    public static float[] Resample(float[] samples, int from, int to)
    {
        if (from == to || samples.Length == 0)
        {
            return samples;
        }

        var ratio = (double)to / from;
        var cutoff = Math.Min(1.0, ratio) * 0.95; // below the lower Nyquist
        var width = HalfWidth / Math.Min(1.0, ratio);
        var result = new float[(int)(samples.Length * ratio)];
        for (var i = 0; i < result.Length; i++)
        {
            var center = i / ratio;
            var first = Math.Max(0, (int)Math.Ceiling(center - width));
            var last = Math.Min(samples.Length - 1, (int)Math.Floor(center + width));
            double sum = 0;
            for (var j = first; j <= last; j++)
            {
                var x = j - center;
                var window = 0.5 + 0.5 * Math.Cos(Math.PI * x / width); // Hann
                sum += samples[j] * cutoff * Sinc(cutoff * x) * window;
            }

            result[i] = (float)sum;
        }

        return result;
    }

    private static double Sinc(double x) => x == 0 ? 1 : Math.Sin(Math.PI * x) / (Math.PI * x);
}

/// <summary>One saved dictation: a line of recordings.tsv.</summary>
/// <param name="Note">Why there is no text ("no speech", "too quiet"…); empty for an ordinary dictation.</param>
public sealed record Recording(string File, DateTime Time, string Model, string Raw, string Final, string Note);

/// <summary>
/// Dictations saved for checking recognition (%APPDATA%\vox\recordings): a 16 kHz WAV each and a line in
/// recordings.tsv. Only the newest <see cref="MaxCount"/> recordings and <see cref="MaxBytes"/> are kept.
/// </summary>
public sealed class RecordingStore(string directory, int maxCount = RecordingStore.MaxCount, long maxBytes = RecordingStore.MaxBytes)
{
    public const int MaxCount = 500;
    public const long MaxBytes = 300L * 1024 * 1024;
    public const int SampleRate = 16000;
    public const string IndexName = "recordings.tsv";
    private const string Header = "file\ttime\tmodel\traw\tfinal\tnote";

    private readonly Lock _lock = new();

    public string Directory { get; } = directory;

    private string Index => Path.Combine(Directory, IndexName);

    /// <summary>Saves the audio as 16 kHz WAV and its line; returns the file name.</summary>
    public string Save(float[] samples, int sampleRate, DateTime time, string model, string raw, string final, string note = "")
    {
        lock (_lock)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var name = $"{time:yyyyMMdd-HHmmss-fff}.wav";
            for (var n = 2; File.Exists(Path.Combine(Directory, name)); n++)
            {
                name = $"{time:yyyyMMdd-HHmmss-fff}-{n}.wav";
            }

            Wav.Write(Path.Combine(Directory, name), Resampler.Resample(samples, sampleRate, SampleRate), SampleRate);
            var line = string.Join('\t', name, time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Clean(model), Clean(raw), Clean(final), Clean(note));
            File.AppendAllText(Index, (File.Exists(Index) ? "" : Header + "\n") + line + "\n", Encoding.UTF8);
            Trim();
            return name;
        }
    }

    /// <summary>
    /// Deletes every recording and recordings.tsv (Settings → "Очистить" in the history); returns how many WAVs
    /// went. The folder stays: Explorer may have it open.
    /// </summary>
    public int Clear()
    {
        lock (_lock)
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return 0;
            }

            var wavs = new DirectoryInfo(Directory).GetFiles("*.wav");
            foreach (var file in wavs)
            {
                file.Delete();
            }

            File.Delete(Index);
            File.Delete(Index + ".tmp");
            return wavs.Length;
        }
    }

    /// <summary>The recordings listed in recordings.tsv of <paramref name="directory"/>, oldest first.</summary>
    public static List<Recording> Read(string directory)
    {
        var index = Path.Combine(directory, IndexName);
        if (!File.Exists(index))
        {
            return [];
        }

        return [.. File.ReadLines(index, Encoding.UTF8).Skip(1).Select(l => l.Split('\t')).Where(p => p.Length >= 5).Select(p => new Recording(
            p[0],
            DateTime.TryParse(p[1], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : default,
            p[2], p[3], p[4], p.Length > 5 ? p[5] : ""))];
    }

    // Oldest WAVs go first (names sort by time), their lines with them.
    private void Trim()
    {
        var files = new DirectoryInfo(Directory).GetFiles("*.wav").OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
        var bytes = files.Sum(f => f.Length);
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (files.Count > 0 && (files.Count > maxCount || bytes > maxBytes))
        {
            bytes -= files[0].Length;
            removed.Add(files[0].Name);
            files[0].Delete();
            files.RemoveAt(0);
        }

        if (removed.Count > 0)
        {
            var lines = File.ReadAllLines(Index, Encoding.UTF8).Where((l, i) => i == 0 || !removed.Contains(l.Split('\t')[0]));
            var tmp = Index + ".tmp";
            File.WriteAllLines(tmp, lines, Encoding.UTF8);
            File.Move(tmp, Index, overwrite: true);
        }
    }

    private static string Clean(string s) => s.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
