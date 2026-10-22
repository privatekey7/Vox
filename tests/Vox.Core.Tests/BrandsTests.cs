using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Brands from brands.txt (rule 1) and the sound search around them: Cyrillic to Latin (rule 2), Latin against Cyrillic terms (rule 3), 23.10.</summary>
public class BrandsTests
{
    private static readonly (string, int)[] English = [.. FuzzyTerms.LoadEnglish()];

    private static string Fix(string text) => FuzzyTerms.Apply(text);

    [Fact]
    public void Brands_are_single_unknown_names_from_the_lists()
    {
        var brands = FuzzyTerms.LoadBrands().ToList();
        var english = English.Select(e => e.Item1).ToHashSet();
        var targets = Terms.LoadPairs().Select(p => p[1].ToLowerInvariant()).ToHashSet();
        Assert.True(brands.Count >= 300, $"{brands.Count} brands");
        Assert.Equal(brands.Count, brands.Select(b => b.ToLowerInvariant()).Distinct().Count());
        foreach (var brand in brands)
        {
            Assert.True(brand.Length >= 5 && brand.All(char.IsAsciiLetter), $"\"{brand}\": five or more Latin letters");
            Assert.False(english.Contains(brand.ToLowerInvariant()), $"\"{brand}\" is an English word");
            Assert.False(targets.Contains(brand.ToLowerInvariant()), $"\"{brand}\" is a term already");
        }
    }

    // Rule 1: a brand no entry lists is found by its garbled Latin spelling.
    [Theory]
    [InlineData("Poloniks", "Poloniex")]
    [InlineData("Открой Alakritty", "Открой Alacritty")]
    [InlineData("Pionyx и Poloniks", "Pionex и Poloniex")]
    public void Garbled_brands_are_found(string text, string expected) => Assert.Equal(expected, Fix(text));

    // A brand's own spelling is not touched, in any case: it is only a target ("config" must not become "Config").
    [Theory]
    [InlineData("tuist и zotero")]
    [InlineData("Zotero, Alacritty и Pionex")]
    [InlineData("ALACRITTY")]
    public void Brands_in_their_own_spelling_stay(string text) => Assert.Equal(text, Fix(text));

    // Rule 2: a Cyrillic word that is no Russian word and sounds like one name only.
    [Theory]
    [InlineData("Посмотри на полониекс", "Посмотри на Poloniex")]
    [InlineData("Открой пионекс", "Открой Pionex")]
    [InlineData("Это кубернейтис упал", "Это Kubernetes упал")]
    [InlineData("Запусти алакритти", "Запусти Alacritty")]
    public void A_Cyrillic_name_becomes_the_brand(string text, string expected) => Assert.Equal(expected, Fix(text));

    [Fact]
    public void A_case_form_of_a_term_becomes_the_term_through_the_pipeline() =>
        Assert.Equal("Попроси Midjourney.", new PostProcessor(ModelCatalog.GigaAm).Process("Попроси мидджорнея.").Trim());

    // Real Russian words and English loanwords come as close to names by sound; they stay.
    [Theory]
    [InlineData("Хочу купить биткоин")] // Bitcoin is an English word, BitKan only sounds like it
    [InlineData("Рассказ про Версаль")] // Vercel
    [InlineData("Это призма")] // Prisma
    [InlineData("Вон там лесина")] // Lucene
    [InlineData("Он удаляет файлы")] // a verb ending
    [InlineData("Тестовые данные")] // a plural ending
    [InlineData("Посмотри конфиг")]
    [InlineData("Леонов пришёл")] // a surname is a name: a capital is exact only
    [InlineData("Я знаю Полониекс")]
    [InlineData("Запусти алакрити")] // alacrity is an English word as close as Alacritty
    public void Russian_words_stay(string text) => Assert.Equal(text, Fix(text));

    [Fact]
    public void A_Cyrillic_name_needs_a_clear_lead_over_the_second_name()
    {
        var brands = new FuzzyTerms([], [], latin: true, English, ["Pionex", "Pionix"]);
        Assert.Equal("Открой пионекс", brands.Replace("Открой пионекс"));
        Assert.Equal("Открой Pionex", new FuzzyTerms([], [], latin: true, English, ["Pionex"]).Replace("Открой пионекс"));
    }

    // Rule 3: "Cabaron" sounds like the term Kuberon and like the Cyrillic term "кабарон" alike: no clear winner, no change.
    [Fact]
    public void A_Latin_word_as_close_to_a_Cyrillic_term_as_to_a_name_stays()
    {
        (string, string)[] pairs = [("куберон", "Kuberon")];
        (string, string)[] exact = [("кабороны", "кабарон")];
        Assert.Equal("Kuberon", new FuzzyTerms(pairs, [], latin: true, English).Replace("Cabaron"));
        Assert.Equal("Cabaron", new FuzzyTerms(pairs, [], latin: true, English, null, exact).Replace("Cabaron"));
        // The Cyrillic term's own sources do not stand in the way: they are what the model hears wrong.
        (string, string)[] sources = [("кабарон", "Cabarona")];
        Assert.Equal("Kuberon", new FuzzyTerms(pairs, [], latin: true, English, null, sources).Replace("Cabaron"));
    }

    // A winner as close in letters as "DeepL" ~ "Deeple" is not blocked by "деплой".
    [Fact]
    public void A_close_spelling_of_a_term_is_not_blocked_by_a_Cyrillic_term() => Assert.Equal("Переведи в DeepL.", Fix("Переведи в Deepll."));
}
