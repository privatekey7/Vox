using Vox.Core.Models;
using Vox.Engine;

namespace Vox.Engine.Tests;

/// <summary>
/// Runs the real models. Needs VOX_TEST_MODELS = folder with the extracted model archives;
/// CI skips these with <c>--filter Category!=Models</c>.
/// </summary>
[Trait("Category", "Models")]
public class RecognizerTests
{
    private static string ModelsRoot =>
        Environment.GetEnvironmentVariable("VOX_TEST_MODELS")
        ?? throw new InvalidOperationException("Set VOX_TEST_MODELS to the folder with extracted models");

    [Fact]
    public void GigaAm_recognises_russian_with_punctuation()
    {
        using var recognizer = new SpeechRecognizer(ModelCatalog.GigaAm, ModelsRoot);
        var (samples, rate) = Wav.Read(Path.Combine(ModelCatalog.DirectoryOf(ModelCatalog.GigaAm, ModelsRoot), "test_wavs", "example.wav"));

        var text = recognizer.Transcribe(samples, rate);

        Assert.StartsWith("Ничьих не требуя похвал,", text, StringComparison.Ordinal);
        Assert.Contains("У лукоморья дуб зелёный", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Parakeet_recognises_english()
    {
        using var recognizer = new SpeechRecognizer(ModelCatalog.Parakeet, ModelsRoot);
        var (samples, rate) = Wav.Read(Path.Combine(ModelCatalog.DirectoryOf(ModelCatalog.Parakeet, ModelsRoot), "test_wavs", "en.wav"));

        Assert.NotEmpty(recognizer.Transcribe(samples, rate));
    }

    [Fact]
    public void Long_recording_is_recognised_in_full()
    {
        // ~45 s: GigaAM alone loses text on long input; segmentation must keep every repeat.
        using var recognizer = new SpeechRecognizer(ModelCatalog.GigaAm, ModelsRoot);
        var (clip, rate) = Wav.Read(Path.Combine(ModelCatalog.DirectoryOf(ModelCatalog.GigaAm, ModelsRoot), "test_wavs", "example.wav"));
        var pause = new float[rate / 2];
        var audio = Enumerable.Repeat(clip.Concat(pause), 4).SelectMany(x => x).ToArray();

        var text = recognizer.Transcribe(audio, rate);

        Assert.Equal(4, text.Split("лукоморья").Length - 1);
    }
}

public class RecognizerWithoutModelsTests
{
    [Fact]
    public void Missing_model_files_fail_fast() =>
        Assert.Throws<ModelIntegrityException>(() => new SpeechRecognizer(ModelCatalog.GigaAm, Path.GetTempPath()));

    [Fact]
    public void Model_file_that_differs_from_its_pin_is_not_loaded()
    {
        // M1: a model planted in the shared folder never reaches sherpa-onnx.
        var root = Directory.CreateTempSubdirectory("vox-models-").FullName;
        try
        {
            var dir = Directory.CreateDirectory(ModelCatalog.DirectoryOf(ModelCatalog.GigaAm, root)).FullName;
            foreach (var pin in ModelCatalog.GigaAm.Files)
            {
                File.WriteAllText(Path.Combine(dir, pin.Path), "planted");
            }

            var error = Assert.Throws<ModelIntegrityException>(() => new SpeechRecognizer(ModelCatalog.GigaAm, root));
            Assert.Contains("изменён", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

internal static class Wav
{
    /// <summary>Minimal reader for 16-bit PCM mono WAV.</summary>
    public static (float[] Samples, int SampleRate) Read(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path));
        reader.ReadBytes(12); // RIFF header
        int rate = 0;
        while (true)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadInt32();
            if (id == "fmt ")
            {
                var fmt = reader.ReadBytes(size);
                rate = BitConverter.ToInt32(fmt, 4);
            }
            else if (id == "data")
            {
                var data = reader.ReadBytes(size);
                var samples = new float[size / 2];
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] = BitConverter.ToInt16(data, i * 2) / 32768f;
                }

                return (samples, rate);
            }
            else
            {
                reader.ReadBytes(size);
            }
        }
    }
}
