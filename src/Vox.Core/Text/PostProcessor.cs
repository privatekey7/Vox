using Vox.Core.Models;

namespace Vox.Core.Text;

/// <summary>
/// Turns raw model output into the text that is pasted:
/// &lt;unk&gt; → glued "в" → user dictionary → Claude → mixed alphabets → terms → phonetic terms → model versions → file extensions → key shortcuts → fillers → ITN (if the model needs it)
/// → dashes → currency sign spacing → ruble sign → "and" as "и" → question marks → sentence spacing
/// → one leading space.
/// </summary>
public sealed class PostProcessor(SpeechModel model)
{
    // Parakeet is multilingual but Vox users dictate mostly in Russian.
    private readonly string _lang = model.Language == "multi" ? "ru" : model.Language;

    private sealed record NameCache(IReadOnlyDictionary<string, string> From, ScreenName[] Names);

    private NameCache? _dictionaryNames;

    /// <param name="fuzzy">Receives the phonetic replacements (<see cref="FuzzyTerms"/>) for the log.</param>
    /// <param name="screen">Names in open windows and remembered ones (<see cref="ScreenWords"/>).</param>
    public string Process(
        string raw,
        IReadOnlyDictionary<string, string>? corrections = null,
        ICollection<FuzzyTerms.Match>? fuzzy = null,
        IReadOnlyCollection<ScreenName>? screen = null)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var text = UnknownToken.Remove(raw);
        screen = WithDictionaryNames(corrections, screen);
        text = GluedPreposition.Split(text, corrections, screen);
        text = corrections is null ? text : UserCorrections.Apply(text, corrections);
        text = ClaudeNames.Apply(text, latinCloud: model.Language == "ru"); // GigaAM hears Claude as "Cloud"

        text = FileExtensions.FixGlued(text);
        text = MixedScript.Fix(text);
        text = Terms.Replace(text);
        text = FuzzyTerms.Apply(text, fuzzy, screen);
        text = corrections is null ? text : UserCorrections.ApplyAfterTerms(text, corrections);
        text = Networks.Fix(text);
        text = ModelVersions.Apply(text);
        text = FileExtensions.Apply(text);
        text = KeyCombos.Apply(text);
        text = Fillers.Remove(text);
        if (model.NeedsItn)
        {
            text = Itn.Apply(text, _lang);
        }

        text = Dashes.Normalize(text);
        text = Currency.Unglue(text);
        if (_lang == "ru")
        {
            text = Currency.SpellOut(text);
            text = Frequency.Fix(text);
            text = RussianConjunction.Fix(text);
            text = QuestionMarks.Fix(text);
        }

        text = SentenceSpacing.Normalize(text);
        return Spacing.WithLeadingSpace(text);
    }

    /// <summary>
    /// Latin names from the user's dictionary ("люмо лапс → Lumalabs") are searched by sound like remembered screen
    /// names: the model writes them in ways the dictionary lacks ("люмалабс", owner, 01.11). A target in lower case is a
    /// word, not a name ("miniapp"), and one that is a term misspelt is not searched: as a name, "Playwrite" from
    /// "playrite → Playwrite" would no longer become Playwright.
    /// </summary>
    private IReadOnlyCollection<ScreenName>? WithDictionaryNames(IReadOnlyDictionary<string, string>? corrections, IReadOnlyCollection<ScreenName>? screen)
    {
        if (corrections is null || corrections.Count == 0)
        {
            return screen;
        }

        if (_dictionaryNames is not { } cache || !ReferenceEquals(cache.From, corrections))
        {
            cache = new NameCache(corrections, [.. corrections.Values.Distinct(StringComparer.Ordinal)
                .Where(n => n.Length > 0 && char.IsAsciiLetterUpper(n[0]) && n.All(char.IsAsciiLetter) && FuzzyTerms.Apply(n) == n)
                .Select(n => new ScreenName(n, OnScreen: false))]);
            _dictionaryNames = cache;
        }

        return cache.Names.Length == 0 ? screen : [.. screen ?? [], .. cache.Names];
    }
}
