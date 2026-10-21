using Vox.Core.Audio;

namespace Vox.Core.Tests;

public sealed class RecordingBufferTests
{
    [Fact]
    public void Collects_audio_until_stopped()
    {
        var buffer = new RecordingBuffer { SampleRate = 16000 };
        buffer.Append([0.1f, 0.2f], timestamp: 100);
        buffer.Append([0.3f], timestamp: 200);

        Assert.Equal([0.1f, 0.2f, 0.3f], buffer.ToArray());
    }

    [Fact]
    public void Ignores_audio_that_arrives_after_the_stop_time()
    {
        // Regression: the recording kept growing until the processing thread got to it, so a
        // dictation queued behind a slow one captured seconds of speech after the key was released.
        var buffer = new RecordingBuffer { SampleRate = 16000 };
        buffer.Append([0.1f], timestamp: 100);
        buffer.StopAt(timestamp: 250);
        buffer.Append([0.2f], timestamp: 250); // the tail still in flight at key-up
        buffer.Append([0.9f], timestamp: 5000); // long after the release

        Assert.Equal([0.1f, 0.2f], buffer.ToArray());
    }
}
