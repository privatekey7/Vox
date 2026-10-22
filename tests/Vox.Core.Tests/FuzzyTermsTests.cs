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
        ("фронтенд-дизайн", "фронтенд-дизайн"), ("оттер", "Otter.ai"), ("хилт", "Hilt"), ("хейген", "HeyGen"),
    ];

    private static readonly (string, int)[] English = [.. FuzzyTerms.LoadEnglish()];

    private static readonly (string, string)[] Spelling = Pairs.Where(p => !p.Item2.Any(char.IsAsciiLetter)).ToArray();

    private static readonly FuzzyTerms Cyrillic = new(Pairs, Spelling, latin: false);
    private static readonly FuzzyTerms WithLatin = new(Pairs, Spelling, latin: true, English);

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
    [InlineData("Это Frountent.", "Это фронтенд.")]
    [InlineData("Это Bekend.", "Это бэкенд.")] // as close as "beckoned": a term goes first
    public void Latin_spellings_are_found(string text, string expected)
    {
        Assert.Equal(expected, WithLatin.Replace(text));
        Assert.Equal(text, Cyrillic.Replace(text)); // Latin search off
    }

    [Theory]
    [InlineData("Фронтент-дизайн готов.", "Фронтенд-дизайн готов.")]
    [InlineData("Фрунтенд-дизайн готов.", "Фронтенд-дизайн готов.")]
    [InlineData("Frountent-design готов.", "Фронтенд-дизайн готов.")]
    [InlineData("это фронтент-разработка", "это фронтенд-разработка")] // no such term: part by part
    [InlineData("это Fruentand-разработка", "это фронтенд-разработка")]
    [InlineData("это бэкенд-фронтанд", "это бэкенд-фронтенд")]
    public void Hyphenated_words_are_found_whole_or_by_parts(string text, string expected) =>
        Assert.Equal(expected, WithLatin.Replace(text));

    // The blocker of Latin search: ordinary English words came as close as brands.
    [Theory]
    [InlineData("water")]
    [InlineData("course")]
    [InlineData("other")]
    [InlineData("health")]
    [InlineData("Это well-known факт.")]
    [InlineData("Это Water.")]
    [InlineData("TextRules и APPDATA")] // code and acronyms
    [InlineData("Пишу на Pigon.")] // "pigeon" is closest, but an English word must be close and common; nothing else is
    [InlineData("это segmenter")] // "segmented" 0.89: an English word only from 0.9
    public void English_words_stay(string text) =>
        Assert.Equal(text, WithLatin.Replace(text));

    [Theory]
    [InlineData("Beautifull day.", "Beautiful day.")] // not "Beautifull day" as one term
    [InlineData("это promis", "это promise")]
    [InlineData("это Beautifull", "это beautiful")] // lower case mid-sentence
    public void Garbled_english_words_become_the_word(string text, string expected)
    {
        var log = new List<FuzzyTerms.Match>();
        Assert.Equal(expected, WithLatin.Replace(text, log));
        Assert.True(Assert.Single(log).English);
    }

    // "Pigon": Python and HeyGen are about as close; the term that is a common English word goes first.
    [Fact]
    public void A_more_frequent_term_wins_a_tie()
    {
        (string, string)[] pairs = [("пайтон", "Python"), ("пайтан", "Paitan")]; // the same sound skeleton
        Assert.Equal("Python", new FuzzyTerms(pairs, [], latin: true, [("python", 20)]).Replace("Paiten"));
        Assert.Equal("Paiten", new FuzzyTerms(pairs, [], latin: true).Replace("Paiten"));
    }

    [Fact]
    public void Frequent_english_words_stay()
    {
        var common = English.Where(e => e.Item2 <= 20).Take(5000).Select(e => e.Item1).ToList();
        Assert.Equal(5000, common.Count);
        var changed = common.Select(w => char.ToUpperInvariant(w[0]) + w[1..]).Concat(common)
            .Select(w => (w, FuzzyTerms.Apply(w))).Where(p => p.w != p.Item2).ToList();
        Assert.Equal([], changed);
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

    // Why Latin search needs the English word list: ordinary words come as close as brands.
    [Theory]
    [InlineData("water", "Wouter")]
    [InlineData("course", "CORS")]
    [InlineData("other", "Otter.ai")]
    public void Without_english_words_they_become_brands(string word, string brand) =>
        Assert.Equal(brand, new FuzzyTerms(Pairs, Spelling, latin: true).Replace(word));

    [Fact]
    public void Replacements_are_reported_for_the_log()
    {
        var log = new List<FuzzyTerms.Match>();
        Cyrillic.Replace("это фронтент", log);
        Assert.Equal([new FuzzyTerms.Match("фронтент", "фронтенд", 0.94)], log);

        log.Clear();
        WithLatin.Replace("Это Fruentand и promis", log);
        Assert.Equal([new FuzzyTerms.Match("Fruentand", "фронтенд", 1), new FuzzyTerms.Match("promis", "promise", 1, English: true)], log);
    }

    [Fact]
    public void Pipeline_finds_a_spelling_the_dictionary_lacks()
    {
        var fuzzy = new List<FuzzyTerms.Match>();
        Assert.Equal(" Пишу во фронтенде.", new PostProcessor(ModelCatalog.GigaAm).Process("Пишу во фронтинде.", null, fuzzy));
        Assert.Equal("фронтинде", Assert.Single(fuzzy).Spoken);
    }
}
