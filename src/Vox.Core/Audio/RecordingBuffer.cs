namespace Vox.Core.Audio;

/// <summary>Audio of one push-to-talk press. The caller serialises Append and ToArray.</summary>
public sealed class RecordingBuffer
{
    private readonly List<float> _samples = new(48000 * 30);
    private long _stopAt = long.MaxValue;

    public int SampleRate { get; set; }

    /// <summary>
    /// Audio arriving after <paramref name="timestamp"/> (a <see cref="System.Diagnostics.Stopwatch"/>
    /// timestamp) is ignored, however late the recording is collected.
    /// </summary>
    public void StopAt(long timestamp) => Interlocked.Exchange(ref _stopAt, timestamp);

    public void Append(float[] samples, long timestamp)
    {
        if (timestamp <= Interlocked.Read(ref _stopAt))
        {
            _samples.AddRange(samples);
        }
    }

    public float[] ToArray() => [.. _samples];
}
