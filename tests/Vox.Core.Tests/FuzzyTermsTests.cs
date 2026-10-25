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
        Assert.Equal("Python", new FuzzyTerms(pairs, [], latin: true, [("python", 20)]).Replace("Paiton"));
        Assert.Equal("Paiton", new FuzzyTerms(pairs, [], latin: true).Replace("Paiton"));
    }

    // False matches seen on the docs and the owner's log, with the full dictionaries.
    [Theory]
    [InlineData("Так, GTA Vender.")] // "Finder": the first sound differs (в/ф)
    [InlineData("это pluggy")] // "Black": п/б
    [InlineData("это rowid")] // "Revit": a short word must match by vowels too
    [InlineData("это middl")] // "Moodle"
    [InlineData("это segmenter")] // "segmented" 0.89: an English word only from 0.9
    [InlineData("это backprop")] // "backdrop"
    public void Look_alikes_stay(string text) =>
        Assert.Equal(text, FuzzyTerms.Apply(text));

    [Theory]
    [InlineData("Открой Hrome.", "Открой Chrome.")] // h/ch at the start
    [InlineData("Пишу на Cishar.", "Пишу на C#.")]
    [InlineData("это Paiton", "это Python")]
    [InlineData("это Gigat", "это GigaAM")]
    public void Live_log_spellings_are_found(string text, string expected) =>
        Assert.Equal(expected, FuzzyTerms.Apply(text));

    // Owner's live test of 9901c96: exact terms.json spellings, "+" sources, units, a term word in a pair.
    [Theory]
    [InlineData("А букенд на Rust или го?", "А бэкенд на Rust или го?")]
    [InlineData("Настроить Webook", "Настроить webhook")]
    [InlineData("Движок Gadot и Unreal Ingine", "Движок Godot и Unreal Engine")]
    [InlineData("Пишу на Си++ и Charb++, а также Елиси плюс плюс", "Пишу на C++ и C++, а также C++")]
    [InlineData("Пишу на Си и Python", "Пишу на Си и Python")] // "си" alone stays
    [InlineData("Firefox от Masila", "Firefox от Mozilla")]
    [InlineData("Smos и Дирам, видеокарта Родеон, 1 600 МГ и 3 ГГ", "CMOS и DRAM, видеокарта Radeon, 1 600 МГц и 3 ГГц")]
    [InlineData("Принял 500 мг лекарства", "Принял 500 мг лекарства")]
    [InlineData("Почта Jmile, Open I и Хьюжен Фейс", "Почта Gmail, OpenAI и Hugging Face")]
    [InlineData("Deep Seak и Kimia 3", "DeepSeek и Kimi 3")] // "Deep" of "Deep Learning" no longer blocks the pair
    [InlineData("Larch Langige Models", "Large Language Models")]
    [InlineData("Машина Msubisi, Альяс", "Машина Mitsubishi, Альяс")]
    [InlineData("Локальные LM-модели", "Локальные LLM-модели")]
    [InlineData("в папку Defolt", "в папку default")]
    [InlineData("Optimising Hub, Хоум, Директори, Паспайдер, Мимо, Бэ дрок", "Optimising Hub, Хоум, Директори, Паспайдер, Мимо, Бэ дрок")]
    [InlineData("Sishar, Orbitrum, Dipsic, Becande, Pyton, Promice", "C#, Arbitrum, DeepSeek, бэкенд, Python, promise")]
    // Owner's live test of d873371 (Final from the chat).
    [InlineData("Сеть орбитру и орбитрум", "Сеть Arbitrum и Arbitrum")]
    [InlineData("Модель Deeps версии 4.1", "Модель DeepSeek 4.1")]
    [InlineData("Клоцонет и клод сонет, Claude Санет 4.5, Санет 4.5", "Claude Sonnet и Claude Sonnet, Claude Sonnet 4.5, Sonnet 4.5")]
    [InlineData("Claude Хайку, Хайку про осень, сонет Шекспира", "Claude Haiku, Хайку про осень, сонет Шекспира")]
    [InlineData("Вибук и вебку", "webhook и вебку")]
    [InlineData("Анрил Энджайн и Гадот", "Unreal Engine и Godot")]
    [InlineData("в сети манат, сеть Manat, курс манат", "в сети Monad, сеть Monad, курс манат")]
    [InlineData("Fugion Face и Яндекс JPT", "Hugging Face и YandexGPT")]
    [InlineData("приват Кей и private Кей", "private key и private key")]
    [InlineData("Это плагенс и Plagens", "Это плагины и плагины")] // a Cyrillic capital is the speaker's and stays
    [InlineData("Гитпуш, кли и кли-версия", "git push, CLI и CLI-версия")]
    [InlineData("Митсубиси Лэнд-Крузер", "Mitsubishi Land Cruiser")]
    [InlineData("пишу на nast и на go, на GO, голу", "пишу на Rust и на Go, на Go, голу")]
    // Owner's live test of d66f396: "Charp" is C#, not "Charb++"; the other ways a model writes C++.
    [InlineData("Сервис на Charp, тесты на Charp.", "Сервис на C#, тесты на C#.")]
    [InlineData("Модуль на Си-++, Si++, C+ и Си-плюс-плюс", "Модуль на C++, C++, C++ и C++")]
    [InlineData("Стандарт C++20 и формула c+d", "Стандарт C++20 и формула c+d")]
    [InlineData("Нужен Beckend Python и Frontand React", "Нужен бэкенд Python и фронтенд React")]
    [InlineData("Внедряем CRM Bitrix 24", "Внедряем CRM Bitrix 24")] // a name next to a term stays
    public void Live_test_phrases(string raw, string expected) =>
        Assert.Equal(expected, new PostProcessor(ModelCatalog.GigaAm).Process(raw).Trim());

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
