using Vox.Core.Models;

namespace Vox.Core.Text;

/// <summary>
/// Turns raw model output into the text that is pasted:
/// user dictionary → terms → model versions → key shortcuts → fillers → ITN (if the model needs it) → currency → sentence spacing
/// → one leading space.
/// </summary>
public sealed class PostProcessor(SpeechModel model)
{
    // Parakeet is multilingual but Vox users dictate mostly in Russian.
    private readonly string _lang = model.Language == "multi" ? "ru" : model.Language;

    public string Process(string raw, IReadOnlyDictionary<string, string>? corrections = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var text = corrections is null ? raw : UserCorrections.Apply(raw, corrections);
        text = Terms.Replace(text);
        text = ModelVersions.Apply(text);
        text = KeyCombos.Apply(text);
        text = Fillers.Remove(text);
        if (model.NeedsItn)
        {
            text = Itn.Apply(text, _lang);
        }

        if (_lang == "ru")
        {
            text = Currency.SpellOut(text);
        }

        text = SentenceSpacing.Normalize(text);
        return Spacing.WithLeadingSpace(text);
    }
}
