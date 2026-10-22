using Vox.Core.Models;

namespace Vox.Engine;

/// <summary>
/// The one place a model becomes a recognizer: a model of another kind (Qwen3-ASR, sherpa-onnx
/// <c>OfflineQwen3AsrModelConfig</c>) gets its own <see cref="ISpeechRecognizer"/> here.
/// </summary>
public static class Recognizers
{
    /// <param name="hotwords">Qwen3-ASR only: words for the model's prompt ("Claude Code, PostgreSQL").</param>
    public static ISpeechRecognizer Create(SpeechModel model, string modelsRoot, int? threads = null, string? hotwords = null) =>
        new SpeechRecognizer(model, modelsRoot, threads, hotwords);
}
