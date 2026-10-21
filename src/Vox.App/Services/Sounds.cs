using System.Media;

namespace Vox.App.Services;

/// <summary>Short start / stop / "not heard" beeps, synthesised once and played asynchronously.</summary>
internal sealed class Sounds : IDisposable
{
    private readonly SoundPlayer _start = Tone((800, 80));
    private readonly SoundPlayer _stop = Tone((500, 80));
    private readonly SoundPlayer _error = Tone((300, 120), (250, 160));

    public bool Enabled { get; set; } = true;

    public void Start() => Play(_start);

    public void Stop() => Play(_stop);

    public void Error() => Play(_error);

    public void Dispose()
    {
        _start.Dispose();
        _stop.Dispose();
        _error.Dispose();
    }

    private void Play(SoundPlayer player)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            player.Play();
        }
        catch (InvalidOperationException e)
        {
            Log.Error("[SOUND] Play failed", e);
        }
    }

    /// <summary>16-bit mono WAV of sine tones with short fades (no clicks).</summary>
    private static SoundPlayer Tone(params (int Hz, int Ms)[] parts)
    {
        const int rate = 22050;
        var samples = new List<short>();
        foreach (var (hz, ms) in parts)
        {
            var n = rate * ms / 1000;
            var fade = rate * 5 / 1000;
            for (var i = 0; i < n; i++)
            {
                var envelope = Math.Min(1.0, Math.Min(i, n - i) / (double)fade);
                samples.Add((short)(Math.Sin(2 * Math.PI * hz * i / rate) * envelope * 0.35 * short.MaxValue));
            }
        }

        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            w.Write("RIFF"u8);
            w.Write(36 + samples.Count * 2);
            w.Write("WAVEfmt "u8);
            w.Write(16);
            w.Write((short)1);
            w.Write((short)1);
            w.Write(rate);
            w.Write(rate * 2);
            w.Write((short)2);
            w.Write((short)16);
            w.Write("data"u8);
            w.Write(samples.Count * 2);
            samples.ForEach(w.Write);
        }

        stream.Position = 0;
        var player = new SoundPlayer(stream);
        player.Load();
        return player;
    }
}
