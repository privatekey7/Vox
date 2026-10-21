using SherpaOnnx;
using Vox.Core.Audio;
using Vox.Core.Models;

namespace Vox.Engine;

/// <summary>Offline speech recognition with one loaded model.</summary>
public interface ISpeechRecognizer : IDisposable
{
    SpeechModel Model { get; }

    /// <summary>Recognise mono audio (float samples in [-1, 1]).</summary>
    string Transcribe(float[] samples, int sampleRate = SpeechRecognizer.SampleRate);
}

/// <summary>sherpa-onnx NeMo transducer (GigaAM v3 / Parakeet v3).</summary>
public sealed class SpeechRecognizer : ISpeechRecognizer
{
    public const int SampleRate = 16000;

    /// <summary>Longest piece sent to the model at once; longer audio is split at pauses.</summary>
    public const double MaxSegmentSeconds = 12;

    private readonly OfflineRecognizer _recognizer;
    private readonly Lock _lock = new();

    public SpeechRecognizer(SpeechModel model, string modelsRoot, int? threads = null)
    {
        Model = model;
        var dir = ModelCatalog.DirectoryOf(model, modelsRoot);
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = 80; // GigaAM's 64 is read from the model metadata
        config.ModelConfig.Transducer.Encoder = Path.Combine(dir, model.Encoder);
        config.ModelConfig.Transducer.Decoder = Path.Combine(dir, model.Decoder);
        config.ModelConfig.Transducer.Joiner = Path.Combine(dir, model.Joiner);
        config.ModelConfig.Tokens = Path.Combine(dir, model.Tokens);
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.NumThreads = threads ?? Math.Max(1, Environment.ProcessorCount / 2);
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";

        foreach (var file in new[] { config.ModelConfig.Transducer.Encoder, config.ModelConfig.Tokens })
        {
            if (!File.Exists(file))
            {
                throw new FileNotFoundException("Model file is missing", file);
            }
        }

        _recognizer = new OfflineRecognizer(config);
    }

    public SpeechModel Model { get; }

    public string Transcribe(float[] samples, int sampleRate = SampleRate)
    {
        // Long input degrades both models (empty text, dropped words): decode pause-separated
        // pieces in one batch and join them.
        var pieces = Segmenter.SplitOnPauses(samples, sampleRate, MaxSegmentSeconds);
        if (pieces.Count == 0)
        {
            return "";
        }

        lock (_lock)
        {
            var streams = pieces.Select(piece =>
            {
                var stream = _recognizer.CreateStream();
                stream.AcceptWaveform(sampleRate, piece.ToArray());
                return stream;
            }).ToList();
            try
            {
                _recognizer.Decode(streams);
                var texts = streams.Select(s => (s.Result.Text ?? "").Trim()).Where(t => t.Length > 0);
                return string.Join(' ', texts);
            }
            finally
            {
                streams.ForEach(s => s.Dispose());
            }
        }
    }

    public void Dispose() => _recognizer.Dispose();
}
