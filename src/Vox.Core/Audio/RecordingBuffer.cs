namespace Vox.Core.Audio;

/// <summary>Audio of one push-to-talk press. The caller serialises Append and ToArray.</summary>
public sealed class RecordingBuffer
{
    private readonly List<float> _samples = new(48000 * 30);
    private long _stopAt = long.MaxValue;

    /// <summary>Longest recording kept: a key-up lost to the secure desktop must not record forever.</summary>
    public static TimeSpan MaxLength { get; } = TimeSpan.FromMinutes(10);

    public int SampleRate { get; set; }

    /// <summary>
    /// Audio arriving after <paramref name="timestamp"/> (a <see cref="System.Diagnostics.Stopwatch"/>
    /// timestamp) is ignored, however late the recording is collected.
    /// </summary>
    public void StopAt(long timestamp) => Interlocked.Exchange(ref _stopAt, timestamp);

    public void Append(float[] samples, long timestamp)
    {
        var room = (int)(MaxLength.TotalSeconds * SampleRate) - _samples.Count;
        if (timestamp <= Interlocked.Read(ref _stopAt) && room > 0)
        {
            _samples.AddRange(samples.Length <= room ? samples : samples[..room]);
        }
    }

    public float[] ToArray() => [.. _samples];
}
