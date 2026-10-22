using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Vox.Core.Audio;

// debt: WasapiCapture is marked obsolete in NAudio 3 in favour of WasapiRecorder (new in 3.x,
// untested here). Kept on the long-proven capture path; migrate once WasapiRecorder is verified
// on real Windows machines.
#pragma warning disable CS0618

namespace Vox.App.Services;

/// <summary>
/// Microphone via WASAPI shared mode, kept open so recording starts instantly. Audio is kept as
/// mono float at the device's own rate; sherpa-onnx resamples to 16 kHz itself.
/// </summary>
/// <remarks>
/// Follows the Windows default microphone (headset plugged in, default changed) and reopens the
/// device when it stops or delivers nothing — in Vox 1.x such stalls produced runs of "No speech
/// recognized" until the device came back.
/// </remarks>
internal sealed class AudioCapture : IDisposable
{
    // Separate locks: closing the device waits for the capture thread, which takes the buffer lock
    // in OnData — one shared lock would deadlock.
    private readonly Lock _deviceLock = new();
    private readonly Lock _bufferLock = new();
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDeviceNotificationClient _notifications;
    private string? _deviceId;
    private WasapiCapture? _capture;
    private RecordingBuffer? _recording;
    private int _sampleRate = 16000;
    private bool _disposed;

    public AudioCapture(string? deviceId)
    {
        _deviceId = deviceId;
        _notifications = _enumerator.CreateNotificationClient(false);
        _notifications.DefaultDeviceChanged += (_, e) =>
        {
            if (_deviceId is null && e.Flow == DataFlow.Capture && e.Role == Role.Console)
            {
                Log.Info("[AUDIO] Default microphone changed — reopening");
                ThreadPool.QueueUserWorkItem(_ => Reopen());
            }
        };
        Reopen();
    }

    /// <summary>Peak level (0..1) of each captured buffer — for the level meter.</summary>
    public event Action<float>? Level;

    public string DeviceName { get; private set; } = "";

    /// <summary>Active capture devices as (id, name) for the settings list.</summary>
    public static IReadOnlyList<(string Id, string Name)> Devices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => (d.ID, d.FriendlyName)).ToList();
    }

    /// <summary>Use a specific microphone, or the Windows default when null.</summary>
    public void UseDevice(string? deviceId)
    {
        _deviceId = deviceId;
        Reopen();
    }

    /// <summary>Start collecting audio into a new buffer. Never blocks (called from the hotkey thread).</summary>
    public RecordingBuffer BeginRecording()
    {
        var buffer = new RecordingBuffer();
        lock (_bufferLock)
        {
            buffer.SampleRate = _sampleRate;
            _recording = buffer;
        }

        if (_capture is null)
        {
            ThreadPool.QueueUserWorkItem(_ => Reopen()); // the device may be back
        }

        return buffer;
    }

    /// <summary>
    /// Stop collecting into <paramref name="buffer"/> and return its audio. A newer recording that
    /// already started (quick re-press) keeps collecting into its own buffer.
    /// </summary>
    public (float[] Samples, int SampleRate) EndRecording(RecordingBuffer buffer)
    {
        lock (_bufferLock)
        {
            if (ReferenceEquals(_recording, buffer))
            {
                _recording = null;
            }

            return (buffer.ToArray(), buffer.SampleRate);
        }
    }

    public void Reopen()
    {
        lock (_deviceLock)
        {
            if (_disposed)
            {
                return;
            }

            Close();
            try
            {
                var device = (_deviceId is null ? null : ChosenDevice(_deviceId))
                    ?? _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
                var capture = new WasapiCapture(device, true, 50);
                lock (_bufferLock)
                {
                    _sampleRate = capture.WaveFormat.SampleRate;
                }

                capture.DataAvailable += OnData;
                capture.RecordingStopped += OnStopped;
                capture.StartRecording();
                _capture = capture;
                DeviceName = device.FriendlyName;
                Log.Info($"[AUDIO] Microphone open: {DeviceName} ({capture.WaveFormat})");
            }
            catch (Exception e)
            {
                Log.Error("[AUDIO] Could not open microphone", e);
                _capture = null;
                DeviceName = "";
            }
        }
    }

    public void Dispose()
    {
        lock (_deviceLock)
        {
            _disposed = true;
            Close();
        }

        _notifications.Dispose();
        _enumerator.Dispose();
    }

    /// <summary>The microphone picked in settings, or null when it is unplugged or disabled.</summary>
    private MMDevice? ChosenDevice(string id)
    {
        try
        {
            var device = _enumerator.GetDevice(id);
            if (device.State == DeviceState.Active)
            {
                return device;
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or ArgumentException)
        {
            // gone for good — fall through to the default microphone
        }

        Log.Info("[AUDIO] Chosen microphone is not available — using the Windows default");
        return null;
    }

    private void Close()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.DataAvailable -= OnData;
        _capture.RecordingStopped -= OnStopped;
        try
        {
            _capture.StopRecording();
        }
        catch (Exception e)
        {
            Log.Error("[AUDIO] Stop failed", e);
        }

        _capture.Dispose();
        _capture = null;
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            Log.Error("[AUDIO] Microphone stopped", e.Exception);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(1000);
                Reopen();
            });
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (sender is not WasapiCapture capture)
        {
            return;
        }

        var mono = ToMono(e.Buffer, e.BytesRecorded, capture.WaveFormat);
        var peak = 0f;
        foreach (var s in mono)
        {
            peak = Math.Max(peak, Math.Abs(s));
        }

        lock (_bufferLock)
        {
            _recording?.Append(mono, Stopwatch.GetTimestamp());
        }

        Level?.Invoke(peak);
    }

    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private static float[] ToMono(byte[] buffer, int bytes, WaveFormat format)
    {
        var channels = format.Channels;
        var bytesPerSample = format.BitsPerSample / 8;
        var frames = bytes / (bytesPerSample * channels);
        // WAVE_FORMAT_EXTENSIBLE says float or PCM in SubFormat: 32-bit can be either.
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormat);
        var mono = new float[frames];
        for (var f = 0; f < frames; f++)
        {
            float sum = 0;
            for (var c = 0; c < channels; c++)
            {
                var offset = (f * channels + c) * bytesPerSample;
                sum += bytesPerSample switch
                {
                    4 when isFloat => BitConverter.ToSingle(buffer, offset),
                    4 => BitConverter.ToInt32(buffer, offset) / 2147483648f,
                    3 => ((buffer[offset] << 8) | (buffer[offset + 1] << 16) | (buffer[offset + 2] << 24)) / 2147483648f,
                    2 => BitConverter.ToInt16(buffer, offset) / 32768f,
                    _ => 0,
                };
            }

            mono[f] = sum / channels;
        }

        return mono;
    }
}
