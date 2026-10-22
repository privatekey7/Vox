using Vox.Core.Models;

namespace Vox.Core.Text;

/// <summary>
/// Turns raw model output into the text that is pasted:
/// &lt;unk&gt; → user dictionary → Claude → terms → model versions → file extensions → key shortcuts → fillers → ITN (if the model needs it)
/// → dashes → currency → sentence spacing
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

        var text = UnknownToken.Remove(raw);
        text = corrections is null ? text : UserCorrections.Apply(text, corrections);
        text = ClaudeNames.Apply(text, latinCloud: model.Language == "ru"); // GigaAM hears Claude as "Cloud"

        text = Terms.Replace(text);
        text = ModelVersions.Apply(text);
        text = FileExtensions.Apply(text);
        text = KeyCombos.Apply(text);
        text = Fillers.Remove(text);
        if (model.NeedsItn)
        {
            text = Itn.Apply(text, _lang);
        }

        text = Dashes.Normalize(text);
        if (_lang == "ru")
        {
            text = Currency.SpellOut(text);
        }

        text = SentenceSpacing.Normalize(text);
        return Spacing.WithLeadingSpace(text);
    }
}
