namespace Vox.Core.Audio;

/// <summary>
/// A microphone that delivers nothing at all, not a quiet room: on 27.10 the office headset stopped sending
/// audio for two hours and every dictation came out empty, with "Не расслышал" after the user had spoken.
/// </summary>
public static class SilentMicrophone
{
    /// <summary>The device sends a block every 50 ms (AudioCapture), so none after this long means it is silent.</summary>
    public static TimeSpan CheckAfter { get; } = TimeSpan.FromMilliseconds(300);

    /// <summary>True when <paramref name="sincePress"/> has passed and the recording still has no audio.</summary>
    public static bool IsSilent(bool hasAudio, TimeSpan sincePress) => !hasAudio && sincePress >= CheckAfter;
}
