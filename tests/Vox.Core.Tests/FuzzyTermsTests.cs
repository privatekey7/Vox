using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Phonetic search (22.10): the model's own spellings no terms.json entry lists.</summary>
public class FuzzyTermsTests
{
    // A mini dictionary without the owner's live-log spellings: they must be found by sound.
    private static readonly (string, string)[] Pairs =
    [
        ("фронтенд", "фронтенд"), ("фронтэнд", "фронтенд"), ("фронт энд", "фронтенд"), ("бэкенд", "бэкенд"), ("бекенд", "бэкенд"),
        ("бэк энд", "бэкенд"), ("хром", "Chrome"), ("сишарп", "C#"), ("пайтон", "Python"), ("анриал энджин", "Unreal Engine"),
        ("саунд-дизайн", "саунд-дизайн"), ("саунд дизайн", "саунд-дизайн"), ("воутер", "Wouter"), ("корс", "CORS"),
    ];

    private static readonly (string, string)[] Spelling = Pairs.Where(p => !p.Item2.Any(char.IsAsciiLetter)).ToArray();

    private static readonly FuzzyTerms Cyrillic = new(Pairs, Spelling, latin: false);
    private static readonly FuzzyTerms WithLatin = new(Pairs, Spelling, latin: true);

    [Theory]
    [InlineData("это фронтент", "это фронтенд")]
    [InlineData("Это вэкенд.", "Это бэкенд.")]
    [InlineData("в фронтанде", "в фронтенде")] // the case form carries over
    [InlineData("Фронтанд готов.", "Фронтенд готов.")] // a capital at a sentence start is not a name
    public void Cyrillic_spellings_are_found(string text, string expected) =>
        Assert.Equal(expected, Cyrillic.Replace(text));

    [Theory]
    [InlineData("Это Fruentand.", "Это фронтенд.")]
    [InlineData("Это Frount End.", "Это фронтенд.")]
    [InlineData("Это Bek End.", "Это бэкенд.")]
    [InlineData("Открой Hrome.", "Открой Chrome.")]
    [InlineData("Пишу на Cisharp.", "Пишу на C#.")]
    [InlineData("Движок Andreal Enjane.", "Движок Unreal Engine.")]
    [InlineData("Sounddesign трека.", "Саунд-дизайн трека.")]
    public void Latin_spellings_are_found_when_switched_on(string text, string expected)
    {
        Assert.Equal(expected, WithLatin.Replace(text));
        Assert.Equal(text, Cyrillic.Replace(text)); // off by default
    }

    [Theory]
    [InlineData("фронт")]
    [InlineData("бекон")]
    [InlineData("тренд")]
    [InlineData("протон")]
    [InlineData("питомец")]
    [InlineData("шарф")]
    [InlineData("хромой")]
    [InlineData("Андрей пришёл")]
    [InlineData("звук")]
    [InlineData("Мама мыла раму и пошла гулять.")]
    [InlineData("У меня Фронтанд.")] // a capital mid-sentence may be a name: only an exact match
    [InlineData("нейросети и фронтенде")] // other forms of terms stay
    public void Ordinary_words_stay(string text) =>
        Assert.Equal(text, Cyrillic.Replace(text));

    // Why Latin search is off: without an English word list ordinary words come as close as brands.
    [Theory]
    [InlineData("water", "Wouter")]
    [InlineData("course", "CORS")]
    public void Latin_search_confuses_english_words(string word, string brand) =>
        Assert.Equal(brand, WithLatin.Replace(word));

    [Fact]
    public void Replacements_are_reported_for_the_log()
    {
        var log = new List<FuzzyTerms.Match>();
        Cyrillic.Replace("это фронтент", log);
        Assert.Equal([new FuzzyTerms.Match("фронтент", "фронтенд", 0.94)], log);
    }

    [Fact]
    public void Pipeline_finds_a_spelling_the_dictionary_lacks()
    {
        var fuzzy = new List<FuzzyTerms.Match>();
        Assert.Equal(" Пишу во фронтенде.", new PostProcessor(ModelCatalog.GigaAm).Process("Пишу во фронтинде.", null, fuzzy));
        Assert.Equal("фронтинде", Assert.Single(fuzzy).Spoken);
    }
}
