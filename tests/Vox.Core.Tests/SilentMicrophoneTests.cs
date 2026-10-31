using Vox.Core.Audio;

namespace Vox.Core.Tests;

public sealed class SilentMicrophoneTests
{
    [Fact]
    public void A_new_recording_has_no_audio()
    {
        var buffer = new RecordingBuffer { SampleRate = 16000 };

        Assert.False(buffer.HasAudio);
        Assert.False(buffer.HasSignal);
    }

    [Fact]
    public void A_recording_has_audio_once_the_device_sends_a_block()
    {
        var buffer = new RecordingBuffer { SampleRate = 16000 };
        buffer.Append([0f, 0f], timestamp: 100);

        Assert.True(buffer.HasAudio);
    }

    [Fact]
    public void An_empty_block_is_no_audio()
    {
        var buffer = new RecordingBuffer { SampleRate = 16000 };
        buffer.Append([], timestamp: 100);

        Assert.False(buffer.HasAudio);
    }

    [Fact]
    public void Audio_stays_once_it_came()
    {
        var buffer = new RecordingBuffer { SampleRate = 16000 };
        buffer.Append([0.1f], timestamp: 100);
        buffer.Append([0f], timestamp: 150);

        Assert.True(buffer.HasAudio);
        Assert.True(buffer.HasSignal);
    }

    [Fact]
    public void Digital_zeros_are_audio_without_a_signal()
    {
        // 30.10 14:24: the headset sent 1.9 s of exact zeros, the pill said "Слушаю" all along.
        var buffer = new RecordingBuffer { SampleRate = 16000 };
        buffer.Append([0f, 0f], timestamp: 100);

        Assert.False(buffer.HasSignal);
    }

    [Fact]
    public void Any_sound_of_a_working_microphone_is_a_signal()
    {
        var buffer = new RecordingBuffer { SampleRate = 16000 };
        buffer.Append([0f, 0.00003f], timestamp: 100); // the noise floor of a quiet room

        Assert.True(buffer.HasSignal);
    }

    [Fact]
    public void No_audio_after_the_check_time_means_the_microphone_is_silent() =>
        Assert.Equal(Silence.NoAudio, SilentMicrophone.Check(hasAudio: false, hasSignal: false, SilentMicrophone.CheckAfter));

    [Fact]
    public void Only_zeros_after_the_check_time_means_the_microphone_is_silent_too() =>
        Assert.Equal(Silence.OnlyZeros, SilentMicrophone.Check(hasAudio: true, hasSignal: false, SilentMicrophone.CheckAfter));

    [Fact]
    public void The_first_blocks_are_not_waited_for_too_early() =>
        Assert.Equal(Silence.None, SilentMicrophone.Check(hasAudio: false, hasSignal: false, TimeSpan.FromMilliseconds(100)));

    [Fact]
    public void A_microphone_that_sends_sound_is_not_silent() =>
        Assert.Equal(Silence.None, SilentMicrophone.Check(hasAudio: true, hasSignal: true, TimeSpan.FromSeconds(10)));

    [Theory]
    [InlineData(300, false, false)]  // an accidental tap: nothing, as before
    [InlineData(499, false, false)]
    [InlineData(500, true, false)]
    [InlineData(700, true, false)]   // 30.10: five such presses vanished without a word
    [InlineData(999, true, false)]
    [InlineData(1000, true, true)]
    [InlineData(1500, true, true)]
    public void An_empty_dictation_says_so_from_half_a_second(int pressMs, bool show, bool sound)
    {
        var feedback = EmptyDictation.For(TimeSpan.FromMilliseconds(pressMs));

        Assert.Equal(show, feedback.Show);
        Assert.Equal(sound, feedback.Sound);
    }
}
