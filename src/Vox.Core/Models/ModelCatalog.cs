namespace Vox.Core.Models;

/// <summary>One speech-recognition model Vox can download and run.</summary>
/// <param name="Id">Stable id, also stored in config.json (<c>active_model</c>).</param>
/// <param name="Language">"ru" or "multi".</param>
/// <param name="ExtractedDir">Folder the archive unpacks to, inside the models directory.</param>
/// <param name="NeedsItn">False when the model already writes numbers as digits.</param>
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
    bool NeedsItn);

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

    public static SpeechModel Default => GigaAm;

    public static SpeechModel? Find(string? id) => All.FirstOrDefault(m => m.Id == id);

    public static string DirectoryOf(SpeechModel model, string modelsRoot) => Path.Combine(modelsRoot, model.ExtractedDir);

    public static bool IsInstalled(SpeechModel model, string modelsRoot) =>
        File.Exists(Path.Combine(DirectoryOf(model, modelsRoot), model.Tokens));
}
