namespace Vox.Core.Models;

/// <summary>One speech-recognition model Vox can download and run.</summary>
/// <param name="Id">Stable id, also stored in config.json (<c>active_model</c>).</param>
/// <param name="Language">"ru" or "multi".</param>
/// <param name="ExtractedDir">Folder the archive unpacks to, inside the models directory.</param>
/// <param name="NeedsItn">False when the model already writes numbers as digits.</param>
/// <param name="Kind">How sherpa-onnx runs it: NeMo transducer, or Qwen3-ASR (<see cref="Joiner"/> is its conv
/// front end, <see cref="Tokens"/> its tokenizer folder).</param>
public sealed record SpeechModel(
    string Id,
    string DisplayName,
    string Language,
    int DownloadMb,
    Uri Url,
    string ExtractedDir,
    string Encoder,
    string Decoder,
    string Joiner,
    string Tokens,
    bool NeedsItn,
    ModelKind Kind = ModelKind.NemoTransducer);

public enum ModelKind
{
    NemoTransducer,
    Qwen3Asr,
}

/// <summary>The two models of Vox 2.0 (D030): GigaAM v3 for Russian, Parakeet v3 for the rest.</summary>
public static class ModelCatalog
{
    private const string SherpaReleases = "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/";

    public static readonly SpeechModel GigaAm = new(
        Id: "gigaam-v3-rnnt-punct",
        DisplayName: "Русский",
        Language: "ru",
        DownloadMb: 162,
        Url: new Uri(SherpaReleases + "sherpa-onnx-nemo-transducer-punct-giga-am-v3-russian-2025-12-16.tar.bz2"),
        ExtractedDir: "sherpa-onnx-nemo-transducer-punct-giga-am-v3-russian-2025-12-16",
        Encoder: "encoder.int8.onnx",
        Decoder: "decoder.onnx",
        Joiner: "joiner.onnx",
        Tokens: "tokens.txt",
        NeedsItn: false); // writes "11:30", "150 000 ₽" itself; ITN would only damage it

    public static readonly SpeechModel Parakeet = new(
        Id: "parakeet-tdt-0.6b-v3",
        DisplayName: "English и другие европейские",
        Language: "multi",
        DownloadMb: 465,
        Url: new Uri(SherpaReleases + "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2"),
        ExtractedDir: "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8",
        Encoder: "encoder.int8.onnx",
        Decoder: "decoder.int8.onnx",
        Joiner: "joiner.int8.onnx",
        Tokens: "tokens.txt",
        NeedsItn: true);

    public static IReadOnlyList<SpeechModel> All { get; } = [GigaAm, Parakeet];

    /// <summary>Qwen3-ASR 0.6B: only for comparing models in tools/Vox.Replay, not offered in the app.</summary>
    public static readonly SpeechModel Qwen3 = new(
        Id: "qwen3-asr-0.6b",
        DisplayName: "Qwen3-ASR 0.6B",
        Language: "multi",
        DownloadMb: 879,
        Url: new Uri(SherpaReleases + "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2"),
        ExtractedDir: "sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25",
        Encoder: "encoder.int8.onnx",
        Decoder: "decoder.int8.onnx",
        Joiner: "conv_frontend.onnx",
        Tokens: "tokenizer",
        NeedsItn: true, // numbers mostly as words ("одиннадцать тридцать"); digits it writes stay
        Kind: ModelKind.Qwen3Asr);

    /// <summary>Models for tools: the app's ones and those only compared.</summary>
    public static IReadOnlyList<SpeechModel> Experimental { get; } = [.. All, Qwen3];

    public static SpeechModel Default => GigaAm;

    public static SpeechModel? Find(string? id) => All.FirstOrDefault(m => m.Id == id);

    public static string DirectoryOf(SpeechModel model, string modelsRoot) => Path.Combine(modelsRoot, model.ExtractedDir);

    public static bool IsInstalled(SpeechModel model, string modelsRoot) =>
        Path.Exists(Path.Combine(DirectoryOf(model, modelsRoot), model.Tokens)); // Qwen3-ASR: a folder
}
