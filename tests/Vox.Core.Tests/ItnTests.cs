using Vox.Core.Text;

namespace Vox.Core.Tests;

public class ItnTests
{
    [Theory]
    // Parakeet sometimes emits the tens as digits and the unit as a word.
    [InlineData("30 шесть", "36")]
    [InlineData("у меня 30 шесть задач", "у меня 36 задач")]
    [InlineData("тридцать 6", "36")]
    [InlineData("100 двадцать три", "123")]
    [InlineData("2 тысячи двадцать пять", "2025")]
    // A digit followed by a multiplier word must not split into two numbers.
    [InlineData("150 тысяч рублей", "150 тысяч рублей")]
    [InlineData("5 миллионов", "5 миллионов")]
    // Two complete numbers in a row stay two numbers (time, scores).
    [InlineData("встреча в одиннадцать тридцать", "встреча в 11 30")]
    [InlineData("двадцать двадцать", "20 20")]
    // "один" alone is usually a pronoun, not a count.
    [InlineData("один из вариантов", "один из вариантов")]
    [InlineData("Одна из задач", "Одна из задач")]
    // Phone-like digit runs are never merged.
    [InlineData("8 900 555 35 35", "8 900 555 35 35")]
    [InlineData("900 55 55", "900 55 55")]
    // Ordinals are left alone rather than half-converted.
    [InlineData("две тысячи двадцать шестой год", "две тысячи двадцать шестой год")]
    // Trailing punctuation does not block conversion and is preserved.
    [InlineData("у меня тридцать шесть.", "у меня 36.")]
    [InlineData("пять, шесть, семь", "5, 6, 7")]
    // Real dictations from vox.log.
    [InlineData("Семнадцать пятьдесят четыре.", "17 54.")]
    [InlineData("Зона сорок четыре.", "Зона 44.")]
    [InlineData("Сто семьдесят пять, двадцать один.", "175, 21.")]
    [InlineData("номер одиннадцать девятьсот два.", "номер 11 902.")]
    [InlineData("при проверке там миллион.", "при проверке там миллион.")]
    [InlineData("около миллиарда", "около миллиарда")]
    [InlineData("на секундах сорок четвертый по сорок шестую", "на секундах сорок четвертый по сорок шестую")]
    public void Apply_ru_handles_mixed_and_ambiguous_numbers(string text, string expected) =>
        Assert.Equal(expected, Itn.Apply(text));

    [Theory]
    [InlineData("сто двадцать три рубля", "123 рубля")]
    [InlineData("двадцать пять серверов", "25 серверов")]
    [InlineData("пять серверов", "5 серверов")]
    [InlineData("сто пятьдесят тысяч рублей", "150000 рублей")]
    [InlineData("две тысячи триста сорок пять", "2345")]
    [InlineData("тысяча рублей", "1000 рублей")]
    [InlineData("минус пять градусов", "минус 5 градусов")]
    [InlineData("пятнадцать процентов", "15 процентов")]
    [InlineData("Тридцать шесть", "36")]
    public void Apply_ru_converts_plain_number_words(string text, string expected) =>
        Assert.Equal(expected, Itn.Apply(text));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("привет мир")]
    [InlineData("Version 2026 released")]
    public void Apply_ru_leaves_non_numeric_text_unchanged(string text) =>
        Assert.Equal(text, Itn.Apply(text));

    [Fact]
    public void Apply_other_language_is_noop() => Assert.Equal("тридцать шесть", Itn.Apply("тридцать шесть", "de"));
}
