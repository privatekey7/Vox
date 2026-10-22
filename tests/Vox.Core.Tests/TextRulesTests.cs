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
    [InlineData("обычный текст без терминов", "обычный текст без терминов")]
    public void Replace_maps_phonetic_terms_to_latin(string text, string expected) =>
        Assert.Equal(expected, Terms.Replace(text));
}

public class SpacingTests
{
    [Theory]
    [InlineData("Привет.Что ты умеешь?Как дела?", "Привет. Что ты умеешь? Как дела?")]
    [InlineData("т.е. так", "т.е. так")]
    [InlineData("версия 3.14 и v2.0", "версия 3.14 и v2.0")]
    [InlineData("раз,два", "раз, два")]
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
