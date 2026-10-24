namespace Vox.Core.Models;

/// <summary>One speech-recognition model Vox can download and run.</summary>
/// <param name="Id">Stable id, also stored in config.json (<c>active_model</c>).</param>
/// <param name="Language">"ru" or "multi".</param>
/// <param name="ExtractedDir">Folder the archive unpacks to, inside the models directory.</param>
/// <param name="NeedsItn">False when the model already writes numbers as digits.</param>
/// <param name="Kind">How sherpa-onnx runs it: NeMo transducer, or Qwen3-ASR (<see cref="Joiner"/> is its conv
/// front end, <see cref="Tokens"/> its tokenizer folder).</param>
/// <param name="MaxPieceSeconds">Longest audio decoded at once; longer is split at pauses (D030).</param>
/// <param name="Archive">Size and SHA-256 of the download, checked before it is unpacked (D034).</param>
/// <param name="Files">Every file sherpa-onnx loads, checked before each load (D034).</param>
public sealed record SpeechModel(
    string Id,
    string DisplayName,
    string Language,
    int DownloadMb,
    Uri Url,
    PinnedFile Archive,
    IReadOnlyList<PinnedFile> Files,
    string ExtractedDir,
    string Encoder,
    string Decoder,
    string Joiner,
    string Tokens,
    bool NeedsItn,
    ModelKind Kind = ModelKind.NemoTransducer,
    double MaxPieceSeconds = 12);

/// <summary>
/// A file Vox trusts only with exactly this size and SHA-256 (lowercase hex). The values come from the
/// GitHub release digests of k2-fsa/sherpa-onnx <c>asr-models</c>, checked by downloading once (D034).
/// </summary>
/// <param name="Path">The archive's file name, or a model file relative to <see cref="SpeechModel.ExtractedDir"/>.</param>
public sealed record PinnedFile(string Path, long Size, string Sha256);

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
        Archive: new("sherpa-onnx-nemo-transducer-punct-giga-am-v3-russian-2025-12-16.tar.bz2", 170_197_019,
            "f9620a0099019c6afcee26525ef9ed3297fa50dd5691c1902af0c948fc1a470b"),
        Files:
        [
            new("encoder.int8.onnx", 224_570_820, "369f35a71bf288d3b8e0391fabd8dba5f2314088d440bca474056b7b4b6e66bf"),
            new("decoder.onnx", 4_600_132, "38fc7475443ea2a26f63211ca350f73ac50fff824ab7a3876ee2bd610c53bbc4"),
            new("joiner.onnx", 2_712_896, "602ff7017a93311aad34df1437c8d7f49911353c13d6eae7a6ee7b041339465c"),
            new("tokens.txt", 13_354, "39abae20e692998290c574e606f11a9edef2902a1995463fcff63d1490cf22b7"),
        ],
        ExtractedDir: "sherpa-onnx-nemo-transducer-punct-giga-am-v3-russian-2025-12-16",
        Encoder: "encoder.int8.onnx",
        Decoder: "decoder.onnx",
        Joiner: "joiner.onnx",
        Tokens: "tokens.txt",
        NeedsItn: false, // writes "11:30", "150 000 ₽" itself; ITN would only damage it
        // Every cut costs words and a stray capital or full stop; GigaAM loses words only beyond ~45 s (D030).
        MaxPieceSeconds: 30);

    public static readonly SpeechModel Parakeet = new(
        Id: "parakeet-tdt-0.6b-v3",
        DisplayName: "English и другие европейские",
        Language: "multi",
        DownloadMb: 465,
        Url: new Uri(SherpaReleases + "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2"),
        Archive: new("sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8.tar.bz2", 487_170_055,
            "5793d0fd397c5778d2cf2126994d58e9d56b1be7c04d13c7a15bb1b4eafb16bf"),
        Files:
        [
            new("encoder.int8.onnx", 652_184_281, "acfc2b4456377e15d04f0243af540b7fe7c992f8d898d751cf134c3a55fd2247"),
            new("decoder.int8.onnx", 11_845_275, "179e50c43d1a9de79c8a24149a2f9bac6eb5981823f2a2ed88d655b24248db4e"),
            new("joiner.int8.onnx", 6_355_277, "3164c13fc2821009440d20fcb5fdc78bff28b4db2f8d0f0b329101719c0948b3"),
            new("tokens.txt", 93_939, "d58544679ea4bc6ac563d1f545eb7d474bd6cfa467f0a6e2c1dc1c7d37e3c35d"),
        ],
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
        Archive: new("sherpa-onnx-qwen3-asr-0.6B-int8-2026-03-25.tar.bz2", 878_702_423,
            "393f8a14e2f5fb96746aaab342997a40641001fbd5bf9592a080a8329178ee96"),
        Files:
        [
            new("encoder.int8.onnx", 182_491_662, "60748d3e6744a57c9c91e1b17424a6c2990567e8adceb0783940c03ed98fa9d9"),
            new("decoder.int8.onnx", 755_914_231, "4f6885be5959ae26af3089d38ee7972c5fafbeeb1cf8d5e76eab6d8b61ca5771"),
            new("conv_frontend.onnx", 44_148_281, "d22dc4423e0940e49884e903d2ea2f7e5567c14fc1aed97e4e26d6b8f208ef9e"),
            new("tokenizer/merges.txt", 1_671_853, "8831e4f1a044471340f7c0a83d7bd71306a5b867e95fd870f74d0c5308a904d5"),
            new("tokenizer/tokenizer_config.json", 12_487, "4942d005604266809309cabc9f4e9cb89ce855d59b14681fdc0e1cc62ea26c4c"),
            new("tokenizer/vocab.json", 2_776_833, "ca10d7e9fb3ed18575dd1e277a2579c16d108e32f27439684afa0e10b1440910"),
        ],
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
