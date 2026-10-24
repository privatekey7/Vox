using Vox.Core.Audio;
using Vox.Core.Models;

namespace Vox.Core.Tests;

public sealed class RecordingsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vox-rec-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static float[] Tone(int rate, double seconds, double hz = 440) =>
        [.. Enumerable.Range(0, (int)(rate * seconds)).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * hz * i / rate)))];

    [Fact]
    public void A_dictation_is_saved_as_16_kHz_wav_and_a_tsv_line()
    {
        var store = new RecordingStore(_dir);
        var time = new DateTime(2026, 10, 22, 19, 30, 5, 123);
        var name = store.Save(Tone(48000, 1), 48000, time, "gigaam-v3-rnnt-punct", "сделай комет\tи", "Сделай коммит\nи");

        Assert.Equal("20261022-193005-123.wav", name);
        var (samples, rate) = Wav.Read(Path.Combine(_dir, name));
        Assert.Equal(16000, rate);
        Assert.InRange(samples.Length, 15990, 16000);
        Assert.InRange(samples.Skip(1000).Take(14000).Max(), 0.47f, 0.53f); // the tone survives resampling

        var lines = File.ReadAllLines(Path.Combine(_dir, RecordingStore.IndexName));
        Assert.Equal("file\ttime\tmodel\traw\tfinal\tnote", lines[0]);
        Assert.Equal("20261022-193005-123.wav\t2026-10-22 19:30:05\tgigaam-v3-rnnt-punct\tсделай комет и\tСделай коммит и\t", lines[1]);
        var read = Assert.Single(RecordingStore.Read(_dir));
        Assert.Equal(new Recording(name, new DateTime(2026, 10, 22, 19, 30, 5), "gigaam-v3-rnnt-punct", "сделай комет и", "Сделай коммит и", ""), read);
    }

    [Fact]
    public void Clearing_deletes_every_recording_and_the_index_and_saving_goes_on()
    {
        var store = new RecordingStore(_dir);
        store.Save(Tone(16000, 0.1), 16000, new DateTime(2026, 10, 22, 12, 0, 0), "m", "раз", "Раз.");
        store.Save(Tone(16000, 0.1), 16000, new DateTime(2026, 10, 22, 12, 0, 1), "m", "два", "Два.");
        File.WriteAllText(Path.Combine(_dir, "notes.txt"), "not ours");

        Assert.Equal(2, store.Clear());

        Assert.Empty(Directory.GetFiles(_dir, "*.wav"));
        Assert.False(File.Exists(Path.Combine(_dir, RecordingStore.IndexName)));
        Assert.True(File.Exists(Path.Combine(_dir, "notes.txt")));
        store.Save(Tone(16000, 0.1), 16000, new DateTime(2026, 10, 22, 12, 0, 2), "m", "три", "Три.");
        Assert.Equal("три", Assert.Single(RecordingStore.Read(_dir)).Raw);
        Assert.Equal(0, new RecordingStore(Path.Combine(_dir, "missing")).Clear());
    }

    [Fact]
    public void An_empty_dictation_is_kept_with_a_note()
    {
        var store = new RecordingStore(_dir);
        store.Save(Tone(16000, 0.5), 16000, DateTime.Now, "parakeet-tdt-0.6b-v3", "", "", "no speech");
        Assert.Equal("no speech", Assert.Single(RecordingStore.Read(_dir)).Note);
    }

    [Fact]
    public void The_oldest_go_beyond_the_count_limit()
    {
        var store = new RecordingStore(_dir, maxCount: 3);
        var start = new DateTime(2026, 10, 22, 12, 0, 0);
        for (var i = 0; i < 5; i++)
        {
            store.Save(Tone(16000, 0.1), 16000, start.AddSeconds(i), "m", $"raw {i}", $"final {i}");
        }

        Assert.Equal(["raw 2", "raw 3", "raw 4"], RecordingStore.Read(_dir).Select(r => r.Raw));
        Assert.Equal(3, Directory.GetFiles(_dir, "*.wav").Length);
    }

    [Fact]
    public void The_oldest_go_beyond_the_size_limit()
    {
        var store = new RecordingStore(_dir, maxBytes: 70_000); // one second at 16 kHz is 32 KB
        var start = new DateTime(2026, 10, 22, 12, 0, 0);
        for (var i = 0; i < 4; i++)
        {
            store.Save(Tone(16000, 1), 16000, start.AddSeconds(i), "m", $"raw {i}", "");
        }

        Assert.Equal(["raw 2", "raw 3"], RecordingStore.Read(_dir).Select(r => r.Raw));
    }

    [Fact]
    public void Two_dictations_in_one_millisecond_keep_both()
    {
        var store = new RecordingStore(_dir);
        var time = new DateTime(2026, 10, 22, 12, 0, 0);
        Assert.NotEqual(store.Save(Tone(16000, 0.1), 16000, time, "m", "a", "a"), store.Save(Tone(16000, 0.1), 16000, time, "m", "b", "b"));
        Assert.Equal(2, RecordingStore.Read(_dir).Count);
    }

    [Fact]
    public void Resampling_removes_what_the_lower_rate_cannot_hold()
    {
        var high = Resampler.Resample(Tone(48000, 1, hz: 12000), 48000, 16000); // above 8 kHz: would alias to 4 kHz
        Assert.True(high.Skip(500).Take(15000).Max(Math.Abs) < 0.05f);
        var same = Tone(16000, 0.1);
        Assert.Same(same, Resampler.Resample(same, 16000, 16000));
    }
}

public sealed class ExperimentalModelsTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("vox-models-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Qwen3_is_only_for_tools_and_installed_by_its_tokenizer_folder()
    {
        Assert.DoesNotContain(ModelCatalog.Qwen3, ModelCatalog.All);
        Assert.Contains(ModelCatalog.Qwen3, ModelCatalog.Experimental);
        Assert.False(ModelCatalog.IsInstalled(ModelCatalog.Qwen3, _root));
        Directory.CreateDirectory(Path.Combine(_root, ModelCatalog.Qwen3.ExtractedDir, "tokenizer"));
        Assert.True(ModelCatalog.IsInstalled(ModelCatalog.Qwen3, _root));
    }
}
