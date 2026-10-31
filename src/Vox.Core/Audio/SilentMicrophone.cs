namespace Vox.Core.Audio;

/// <summary>Why the microphone counts as silent while the key is held.</summary>
public enum Silence
{
    None,

    /// <summary>Not a single block came: the device is stuck or gone (27.10, two hours of it).</summary>
    NoAudio,

    /// <summary>Blocks of exact zeros (30.10): muted on the headset or a stalled driver. A real microphone always hisses.</summary>
    OnlyZeros,
}

/// <summary>
/// A microphone that delivers nothing, not a quiet room: on 27.10 the office headset stopped sending
/// audio for two hours and every dictation came out empty, with "Не расслышал" after the user had spoken.
/// </summary>
public static class SilentMicrophone
{
    /// <summary>
    /// The device sends a block every 50 ms (AudioCapture), but the first one came up to 218 ms late in 3 of
    /// 3500 working dictations (logs 27.10), and exact zeros at the start lasted at most 0.1 s in 500 recordings:
    /// nothing after this long means the microphone is silent.
    /// </summary>
    public static TimeSpan CheckAfter { get; } = TimeSpan.FromMilliseconds(500);

    public static Silence Check(bool hasAudio, bool hasSignal, TimeSpan sincePress) => throw new NotImplementedException();
}

/// <summary>What the pill and the sounds say when a dictation comes out empty.</summary>
public static class EmptyDictation
{
    public readonly record struct Feedback(bool Show, bool Sound);

    public static Feedback For(TimeSpan press) => throw new NotImplementedException();
}
