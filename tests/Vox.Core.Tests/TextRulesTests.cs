using Vox.Core.Text;

namespace Vox.Core.Tests;

public class CurrencyTests
{
    [Theory]
    [InlineData("Переведи 500 ₽ на карту.", "Переведи 500 рублей на карту.")]
    [InlineData("1 ₽", "1 рубль")]
    [InlineData("21 ₽", "21 рубль")]
    [InlineData("2 ₽", "2 рубля")]
    [InlineData("34 ₽", "34 рубля")]
    [InlineData("11 ₽", "11 рублей")]
    [InlineData("12 ₽", "12 рублей")]
    [InlineData("111 ₽", "111 рублей")]
    [InlineData("150 000 ₽ до 25-го числа.", "150 000 рублей до 25-го числа.")]
    [InlineData("1 001 ₽", "1 001 рубль")]
    [InlineData("1,5 ₽", "1,5 рубля")]
    [InlineData("500₽", "500 рублей")]
    [InlineData("Стоит 99 ₽, а не 100 ₽.", "Стоит 99 рублей, а не 100 рублей.")]
    [InlineData("цена в ₽", "цена в рублях")]
    public void SpellOut_replaces_ruble_sign_with_agreeing_word(string text, string expected) =>
        Assert.Equal(expected, Currency.SpellOut(text));

    [Theory]
    [InlineData("")]
    [InlineData("без валюты")]
    [InlineData("500 рублей")]
    [InlineData("$5")]
    public void SpellOut_leaves_other_text_unchanged(string text) => Assert.Equal(text, Currency.SpellOut(text));
}

public class FillersTests
{
    [Theory]
    [InlineData("Э-э, запусти сборку", "Запусти сборку")]
    [InlineData("ммм давай так", "давай так")]
    [InlineData("Мы, э, решили", "Мы, решили")]
    [InlineData("Mmm.", "")]
    [InlineData("ну вот этот файл", "ну вот этот файл")]
    [InlineData("um so hello world", "so hello world")]
    // Regression (owner's log): longer hesitations were left behind or broke the text.
    [InlineData("Надо сказать, э-э-э, вот так", "Надо сказать, вот так")]
    [InlineData("Тестовые Ааа, тестируйте", "Тестовые тестируйте")]
    [InlineData("м-м-м, хммм, да", "да")]
    [InlineData("Мама мыла раму", "Мама мыла раму")]
    [InlineData("А мы ам", "А мы ам")]
    // Regression: a capital was lost when a filler started the sentence.
    [InlineData("Да, у меня всегда это стоит. А-а, ещё не пробовал", "Да, у меня всегда это стоит. Ещё не пробовал")]
    // Owner: a drawn-out "а-а" mid-sentence comes out as a capital "А"; it is lowered, not removed.
    [InlineData("как я в данном случае пользователь А говорит на русском", "как я в данном случае пользователь а говорит на русском")]
    [InlineData("Он сказал А, и ушёл", "Он сказал а, и ушёл")]
    // Regression (owner's corpus): the conjunction "а" and a letter-variant were removed.
    [InlineData("старая версия была на Piton А новая на C#.", "старая версия была на Piton а новая на C#.")]
    [InlineData("Да, выбираем А.", "Да, выбираем А.")]
    [InlineData("Да, выбираем А", "Да, выбираем А")]
    [InlineData("про часть А и Б.", "про часть А и Б.")]
    [InlineData("часть А или Б", "часть А или Б")]
    [InlineData("пункт А говорит", "пункт А говорит")]
    [InlineData("вариант А лучше", "вариант А лучше")]
    [InlineData("витамин А полезен", "витамин А полезен")]
    [InlineData("в классе А учатся", "в классе А учатся")]
    [InlineData("корпус 3 А стоит", "корпус 3 А стоит")]
    [InlineData("Ну. А потом", "Ну. А потом")]
    [InlineData("сделал а потом ушёл", "сделал а потом ушёл")]
    [InlineData("план А и план Б", "план А и план Б")]
    public void Remove_strips_hesitation_sounds_only(string text, string expected) =>
        Assert.Equal(expected, Fillers.Remove(text));
}

public class TermsTests
{
    [Theory]
    [InlineData("Запушь в гитхаб.", "Запушь в GitHub.")]
    // Regression (owner's log): brand in lower case, and the model's own misspellings.
    [InlineData("Привет, Вокс.", "Привет, Vox.")]
    [InlineData("Залей на гитхап.", "Залей на GitHub.")]
    [InlineData("Пишу на Pithon.", "Пишу на Python.")]
    [InlineData("Ругается Smart Screen.", "Ругается SmartScreen.")]
    [InlineData("Добавь юзстейт для счётчика", "Добавь useState для счётчика")]
    // Owner's log: the model's own "+" tail is swallowed, not doubled.
    [InlineData("Открой Нотпад+.", "Открой Notepad++.")]
    [InlineData("Открой Нот пат++.", "Открой Notepad++.")]
    [InlineData("открой нотпад", "открой Notepad++")]
    [InlineData("в нот паде", "в Notepad++")]
    [InlineData("Правлю в нотпаде.", "Правлю в Notepad++.")]
    [InlineData("обычный текст без терминов", "обычный текст без терминов")]
    public void Replace_maps_phonetic_terms_to_latin(string text, string expected) =>
        Assert.Equal(expected, Terms.Replace(text));

    // Owner's policy: settled borrowings stay Cyrillic, only the spelling is fixed — with case forms.
    [Theory]
    [InlineData("Промт готов.", "Промпт готов.")]
    [InlineData("Напиши промты для агента.", "Напиши промпты для агента.")]
    [InlineData("Ошибка в промте.", "Ошибка в промпте.")]
    public void Cyrillic_spelling_fixes_keep_case_and_ending(string text, string expected) =>
        Assert.Equal(expected, Terms.Replace(text));

    // Regression: a hyphen is part of the word — "пиф-паф" became "пиф-path", "Жан-Клод" "Жан-Claude".
    [Theory]
    [InlineData("Пиф-паф, ой-ой-ой.")]
    [InlineData("Смотрел фильм с Жан-Клодом ван Даммом, Жан-Клод хорош.")]
    public void Part_of_a_hyphenated_word_is_not_a_term(string text) =>
        Assert.Equal(text, Terms.Replace(text));
}

public class SpacingTests
{
    [Theory]
    [InlineData("Привет.Что ты умеешь?Как дела?", "Привет. Что ты умеешь? Как дела?")]
    [InlineData("т.е. так", "т.е. так")]
    [InlineData("версия 3.14 и v2.0", "версия 3.14 и v2.0")]
    [InlineData("раз,два", "раз, два")]
    // Regression (owner's log): a domain became "github. Com".
    [InlineData("открой github.com и vox.log", "открой github.com и vox.log")]
    public void SentenceSpacing_splits_glued_sentences_only(string text, string expected) =>
        Assert.Equal(expected, SentenceSpacing.Normalize(text));

    [Fact]
    public void Leading_space_is_added_once() => Assert.Equal(" Привет", Spacing.WithLeadingSpace(Spacing.WithLeadingSpace("Привет")));

    [Fact]
    public void Separator_is_split_off_for_typing() => Assert.Equal((" ", "Привет"), Spacing.SplitLeadingSeparator(" Привет"));
}

public class UserCorrectionsTests
{
    private static readonly Dictionary<string, string> Corrections = new() { ["кубернетис"] = "Kubernetes", ["вокс"] = "vox" };

    [Theory]
    [InlineData("настрой кубернетис, пожалуйста", "настрой Kubernetes, пожалуйста")]
    [InlineData("Вокс работает", "Vox работает")]
    [InlineData("«кубернетис»", "«Kubernetes»")]
    [InlineData("ничего не меняется", "ничего не меняется")]
    public void Apply_replaces_whole_words_and_keeps_punctuation_and_capital(string text, string expected) =>
        Assert.Equal(expected, UserCorrections.Apply(text, Corrections));
}

/// <summary>Owner: GigaAM's long dash reads as machine text; people type a hyphen.</summary>
public class DashesTests
{
    [Theory]
    [InlineData("словарь на полторы тысячи терминов — это маловато", "словарь на полторы тысячи терминов - это маловато")]
    [InlineData("займёт 1—2 минуты, страницы 10 – 12", "займёт 1-2 минуты, страницы 10-12")]
    [InlineData("Москва—Петербург и что–то", "Москва-Петербург и что-то")]
    [InlineData("без тире", "без тире")]
    public void Long_dashes_become_hyphens(string text, string expected) => Assert.Equal(expected, Dashes.Normalize(text));

    [Theory]
    [InlineData("Э-э — ну да.", " Ну да.")] // a filler before the dash: no stray "-" at the start
    [InlineData("Это — тест.", " Это - тест.")]
    public void Pipeline_writes_hyphens(string raw, string expected) =>
        Assert.Equal(expected, new PostProcessor(Vox.Core.Models.ModelCatalog.GigaAm).Process(raw));
}

/// <summary>Owner's log: Parakeet left "&lt;unk&gt;" in the text.</summary>
public class UnknownTokenTests
{
    [Theory]
    [InlineData("команду <unk>Поговорим", "команду Поговорим")]
    [InlineData("<unk> привет <unk>", "привет")]
    // Regression (owner's corpus): Parakeet has no "ё" token and writes <unk> instead.
    [InlineData("Ты ч<unk>, серьёзно?", "Ты чё, серьёзно?")]
    [InlineData("сломался бо<unk>к.", "сломался боёк.")]
    [InlineData("<unk>Поговорим<unk>.", "Поговорим.")]
    [InlineData("вс<unk> готово, ещ<unk> раз", "всё готово, ещё раз")]
    public void Unk_is_removed(string text, string expected) => Assert.Equal(expected, UnknownToken.Remove(text));

    [Fact]
    public void Only_unk_gives_nothing() =>
        Assert.Equal("", new PostProcessor(Vox.Core.Models.ModelCatalog.Parakeet).Process("<unk>"));
}

/// <summary>Owner's corpus: "точка" before a file extension.</summary>
public class FileExtensionsTests
{
    [Theory]
    [InlineData("Сохрани в Точка MP4.", " Сохрани в .mp4.")]
    [InlineData("Открой index точка html", " Открой index.html")]
    [InlineData("Скинь файл точка пдф", " Скинь файл .pdf")]
    [InlineData("Поставь точка и запятая", " Поставь точка и запятая")]
    public void Spoken_dot_before_an_extension(string raw, string expected) =>
        Assert.Equal(expected, new PostProcessor(Vox.Core.Models.ModelCatalog.GigaAm).Process(raw));
}
