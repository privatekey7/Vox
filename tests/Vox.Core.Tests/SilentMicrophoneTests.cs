using Vox.Core.Audio;

namespace Vox.Core.Tests;

public sealed class SilentMicrophoneTests
{
    [Fact]
    public void A_new_recording_has_no_audio()
    {
        var buffer = new RecordingBuffer { SampleRate = 16000 };

        Assert.False(buffer.HasAudio);
    }

    [Fact]
    public void A_recording_has_audio_once_the_device_sends_a_block()
    {
        var buffer = new RecordingBuffer { SampleRate = 16000 };
        buffer.Append([0f, 0f], timestamp: 100); // silence from a working microphone still counts

        Assert.True(buffer.HasAudio);
    }

    [Fact]
    public void No_audio_after_the_check_time_means_the_microphone_is_silent() =>
        Assert.True(SilentMicrophone.IsSilent(hasAudio: false, SilentMicrophone.CheckAfter));

    [Fact]
    public void The_first_blocks_are_not_waited_for_too_early() =>
        Assert.False(SilentMicrophone.IsSilent(hasAudio: false, TimeSpan.FromMilliseconds(100)));

    [Fact]
    public void A_microphone_that_sends_audio_is_not_silent() =>
        Assert.False(SilentMicrophone.IsSilent(hasAudio: true, TimeSpan.FromSeconds(10)));
}
