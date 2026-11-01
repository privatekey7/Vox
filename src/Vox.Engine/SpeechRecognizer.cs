using System.Globalization;
using SherpaOnnx;
using Vox.Core.Audio;
using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Engine;

/// <summary>Offline speech recognition with one loaded model.</summary>
public interface ISpeechRecognizer : IDisposable
{
    SpeechModel Model { get; }

    /// <summary>Recognise mono audio (float samples in [-1, 1]).</summary>
    string Transcribe(float[] samples, int sampleRate = SpeechRecognizer.SampleRate);
}

/// <summary>sherpa-onnx NeMo transducer (GigaAM v3 / Parakeet v3), or Qwen3-ASR for comparing models.</summary>
public sealed class SpeechRecognizer : ISpeechRecognizer
{
    public const int SampleRate = 16000;

    private readonly OfflineRecognizer _recognizer;

    /// <summary>GigaAM heard again with a hint, for words whose soft first sound it drops (<see cref="QuietOnset"/>).</summary>
    private readonly OfflineRecognizer? _secondPass;
    private readonly Lock _lock = new();
    private bool _disposed;

    /// <param name="hotwords">Qwen3-ASR only: words put in the model's prompt ("Claude Code, PostgreSQL").</param>
    public SpeechRecognizer(SpeechModel model, string modelsRoot, int? threads = null, string? hotwords = null)
    {
        Model = model;
        var dir = ModelCatalog.DirectoryOf(model, modelsRoot);
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = SampleRate;
        config.FeatConfig.FeatureDim = 80; // GigaAM's 64 is read from the model metadata
        if (model.Kind == ModelKind.Qwen3Asr)
        {
            config.ModelConfig.Qwen3Asr.Encoder = Path.Combine(dir, model.Encoder);
            config.ModelConfig.Qwen3Asr.Decoder = Path.Combine(dir, model.Decoder);
            config.ModelConfig.Qwen3Asr.ConvFrontend = Path.Combine(dir, model.Joiner);
            config.ModelConfig.Qwen3Asr.Tokenizer = Path.Combine(dir, model.Tokens);
            config.ModelConfig.Qwen3Asr.Hotwords = hotwords ?? "";
            config.ModelConfig.Debug = Environment.GetEnvironmentVariable("VOX_SHERPA_DEBUG") == "1" ? 1 : 0;
        }
        else
        {
            config.ModelConfig.Transducer.Encoder = Path.Combine(dir, model.Encoder);
            config.ModelConfig.Transducer.Decoder = Path.Combine(dir, model.Decoder);
            config.ModelConfig.Transducer.Joiner = Path.Combine(dir, model.Joiner);
            config.ModelConfig.Tokens = Path.Combine(dir, model.Tokens);
            config.ModelConfig.ModelType = "nemo_transducer";
        }

        config.ModelConfig.NumThreads = threads ?? Math.Max(1, Environment.ProcessorCount / 2);
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";

        // Only files that match their pins are loaded, and they stay locked until sherpa-onnx has read them (D036).
        using (ModelFiles.OpenVerified(model, modelsRoot, rejectLinks: ModelFiles.IsSharedFolder(modelsRoot)))
        {
            _recognizer = new OfflineRecognizer(config);
            if (model == ModelCatalog.GigaAm)
            {
                _secondPass = CreateSecondPass(config, config.ModelConfig.Tokens);
            }
        }
    }

    /// <summary>
    /// The same model with beam search and <see cref="QuietOnset.Hotwords"/>: slower (+40% on a short phrase), so it
    /// hears only short windows around the words that need it. sherpa-onnx reads the hint and the BPE vocabulary (not in the model archive,
    /// built from tokens.txt: piece order is merge priority) while it loads, so the files live only until then.
    /// </summary>
    private static OfflineRecognizer CreateSecondPass(OfflineRecognizerConfig config, string tokens)
    {
        var dir = Directory.CreateTempSubdirectory("vox-hint-").FullName;
        try
        {
            var vocab = Path.Combine(dir, "bpe.vocab");
            File.WriteAllLines(vocab, File.ReadLines(tokens)
                .Select(line => line.Split(' '))
                .Where(parts => parts.Length == 2 && parts[0] != "<unk>")
                .Select(parts => $"{parts[0]} {-int.Parse(parts[1], CultureInfo.InvariantCulture)}"));
            var hotwords = Path.Combine(dir, "hotwords.txt");
            File.WriteAllLines(hotwords, QuietOnset.Hotwords);

            config.DecodingMethod = "modified_beam_search";
            config.MaxActivePaths = 4;
            config.HotwordsFile = hotwords;
            config.HotwordsScore = QuietOnset.HintScore;
            config.ModelConfig.ModelingUnit = "bpe";
            config.ModelConfig.BpeVocab = vocab;
            return new OfflineRecognizer(config);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    public SpeechModel Model { get; }

    public string Transcribe(float[] samples, int sampleRate = SampleRate)
    {
        // Long input degrades both models (empty text, dropped words): decode pause-separated
        // pieces in one batch and join them.
        var pieces = Segmenter.SplitOnPauses(samples, sampleRate, Model.MaxPieceSeconds);
        if (pieces.Count == 0)
        {
            return "";
        }

        lock (_lock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var streams = pieces.Select(piece =>
            {
                var stream = _recognizer.CreateStream();
                stream.AcceptWaveform(sampleRate, piece.ToArray());
                return stream;
            }).ToList();
            try
            {
                _recognizer.Decode(streams);
                var texts = streams.Select(s => s.Result.Text ?? "").ToArray();
                if (_secondPass != null)
                {
                    HearAgain(pieces, sampleRate, streams, texts);
                }

                return PieceJoiner.Join(texts);
            }
            finally
            {
                streams.ForEach(s => s.Dispose());
            }
        }
    }

    /// <summary>
    /// Hears again a window around each word that may have lost its soft first sound and puts the sound back where the
    /// window heard the full word (<see cref="QuietOnset"/>).
    /// </summary>
    private void HearAgain(IReadOnlyList<ArraySegment<float>> pieces, int sampleRate, List<OfflineStream> streams, string[] texts)
    {
        var again = new List<(int Piece, Suspect Suspect, int From)>();
        for (var i = 0; i < texts.Length; i++)
        {
            if (!QuietOnset.NeedsSecondPass(texts[i]))
            {
                continue;
            }

            var result = streams[i].Result;
            var suspects = QuietOnset.Suspects(QuietOnset.Words(result.Tokens, result.Timestamps));
            if (suspects.Count != QuietOnset.CountSuspects(texts[i]))
            {
                continue; // the tokens and the text do not line up
            }

            again.AddRange(suspects.Select(s => (i, s, QuietOnset.Window(s.Start, sampleRate, pieces[i].Count).From)));
        }

        if (again.Count == 0)
        {
            return;
        }

        var windows = again.Select(a =>
        {
            var (from, to) = QuietOnset.Window(a.Suspect.Start, sampleRate, pieces[a.Piece].Count);
            var stream = _secondPass!.CreateStream();
            stream.AcceptWaveform(sampleRate, pieces[a.Piece].Slice(from, to - from).ToArray());
            return stream;
        }).ToList();
        try
        {
            _secondPass!.Decode(windows);
            // Restore never shifts the numbering: a restored word is no longer a suspect, so go from the last one back.
            for (var k = again.Count - 1; k >= 0; k--)
            {
                var (piece, suspect, from) = again[k];
                var result = windows[k].Result;
                var full = QuietOnset.HeardFull(suspect.Word, QuietOnset.Words(result.Tokens, result.Timestamps), suspect.Start - (float)from / sampleRate);
                if (full != null)
                {
                    texts[piece] = QuietOnset.Restore(texts[piece], suspect.Occurrence, full);
                }
            }
        }
        finally
        {
            windows.ForEach(s => s.Dispose());
        }
    }

    /// <summary>Waits for a running <see cref="Transcribe"/> to finish before freeing native memory.</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _recognizer.Dispose();
            _secondPass?.Dispose();
        }
    }
}
