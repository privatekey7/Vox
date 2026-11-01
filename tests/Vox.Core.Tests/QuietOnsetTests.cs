using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Owner, 01.11: "вставь" said with a soft "ф" ("фставь") comes out as "Ставь сюда" in about one take of five.</summary>
public sealed class QuietOnsetTests
{
    [Theory]
    [InlineData("Ставь сюда.")]
    [InlineData("Ставьте текст поля.")]
    [InlineData("Не ставь сюда точку.")]
    [InlineData("Ставить пробел между знаком.")]
    [InlineData("Ставлю на паузу.")]
    [InlineData("Ставим задачу на завтра.")]
    public void A_word_that_may_have_lost_its_first_sound_asks_for_a_second_pass(string raw) =>
        Assert.True(QuietOnset.NeedsSecondPass(raw));

    [Theory]
    [InlineData("Вставь.")]
    [InlineData("Сначала скопируй, потом вставь сюда.")]
    [InlineData("Поставь будильник.")]
    [InlineData("Расставь стулья.")]
    [InlineData("Ставка выросла.")]
    [InlineData("")]
    public void Other_text_needs_no_second_pass(string raw) =>
        Assert.False(QuietOnset.NeedsSecondPass(raw));

    [Theory]
    [InlineData("Ставь сюда.", "Вставь сюда.", "Вставь сюда.")]
    [InlineData("Ставьте текст поля.", "Вставьте текст поля.", "Вставьте текст поля.")]
    [InlineData("Ставь.", "Вставь.", "Вставь.")]
    [InlineData("Сначала скопируй, потом ставь сюда.", "Сначала скопируй, потом вставь сюда.", "Сначала скопируй, потом вставь сюда.")]
    // the case of the first pass is kept
    [InlineData("Ставь сюда.", "вставь сюда.", "Вставь сюда.")]
    [InlineData("потом ставь", "потом Вставь", "потом вставь")]
    public void The_second_pass_brings_back_the_lost_sound(string first, string second, string expected) =>
        Assert.Equal(expected, QuietOnset.Merge(first, second));

    [Theory]
    // the second pass agrees with the first
    [InlineData("Ставь чайник.", "Ставь чайник.", "Ставь чайник.")]
    // only the lost sound is taken, other words stay as the first pass heard them
    [InlineData("Ставь сюда.", "Вставь туда.", "Вставь сюда.")]
    [InlineData("Ставь сюда.", "Вставь сюда!", "Вставь сюда.")]
    // a different word count: the words cannot be lined up
    [InlineData("Я вставлю ссылку, а ты ставь лайк.", "Я вставлю ссылку, а ты вс", "Я вставлю ссылку, а ты ставь лайк.")]
    [InlineData("Ставь сюда.", "Вставь это сюда.", "Ставь сюда.")]
    // another word in its place is not the lost sound
    [InlineData("Ставь сюда.", "Доставь сюда.", "Ставь сюда.")]
    [InlineData("Ставь сюда.", "Вставьте сюда.", "Ставь сюда.")]
    [InlineData("Ставь сюда.", "", "Ставь сюда.")]
    public void Anything_else_from_the_second_pass_is_ignored(string first, string second, string expected) =>
        Assert.Equal(expected, QuietOnset.Merge(first, second));

    [Fact]
    public void The_hint_holds_every_restored_form_in_both_cases()
    {
        Assert.Contains("вставь", QuietOnset.Hotwords);
        Assert.Contains("Вставь", QuietOnset.Hotwords);
        Assert.Contains("Вставьте", QuietOnset.Hotwords);
        Assert.DoesNotContain("ставь", QuietOnset.Hotwords);
    }
}
