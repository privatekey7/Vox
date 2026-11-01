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

    // GigaAM's tokens for "Я вставлю ссылку, а ты ставь лайк." (owner's take 01.11 15:37:01)
    private static readonly string[] Tokens =
        [" Я", " в", "ста", "в", "лю", " с", "сы", "л", "ку", ",", " а", " ты", " ", "ста", "в", "ь", " ", "ла", "й", "к", "."];

    private static readonly float[] Starts =
        [0.2f, 0.32f, 0.44f, 0.56f, 0.64f, 0.76f, 0.84f, 0.92f, 0.96f, 1.0f, 1.08f, 1.2f, 1.36f, 1.4f, 1.48f, 1.48f, 1.6f, 1.68f, 1.76f, 1.84f, 1.96f];

    [Fact]
    public void Tokens_become_words_with_the_time_they_start()
    {
        var words = QuietOnset.Words(Tokens, Starts);

        Assert.Equal(["Я", "вставлю", "ссылку", "а", "ты", "ставь", "лайк"], words.Select(w => w.Text));
        Assert.Equal(1.36f, words[5].Start);
    }

    [Theory]
    [InlineData("Не ставь сюда, ставь туда.", 2)]
    [InlineData("Я вставлю ссылку, а ты ставь лайк.", 1)]
    [InlineData("Поставь будильник.", 0)]
    public void Suspect_words_are_counted_in_the_text(string text, int count) =>
        Assert.Equal(count, QuietOnset.CountSuspects(text));

    [Fact]
    public void Only_words_that_may_have_lost_their_first_sound_are_heard_again()
    {
        var suspects = QuietOnset.Suspects(QuietOnset.Words(Tokens, Starts));

        var suspect = Assert.Single(suspects);
        Assert.Equal(("ставь", 0, 1.36f), (suspect.Word, suspect.Occurrence, suspect.Start));
    }

    [Fact]
    public void The_window_holds_the_soft_sound_before_the_word_and_the_word_itself()
    {
        Assert.Equal((5_760, 45_760), QuietOnset.Window(1.36f, 16_000, 48_000));
        // clipped to the recording
        Assert.Equal((0, 27_840), QuietOnset.Window(0.24f, 16_000, 48_000));
        Assert.Equal((5_760, 30_000), QuietOnset.Window(1.36f, 16_000, 30_000));
    }

    [Theory]
    // the first pass put "ставь" at 0.6 s into the window
    [InlineData("ставь", "ссылку", 0.1f, "вставь", 0.55f, "вставь")]
    [InlineData("ставь", "ссылку", 0.1f, "Вставь", 0.7f, "вставь")]
    [InlineData("ставьте", "а", 0.3f, "вставьте", 0.6f, "вставьте")]
    // the window heard the same word, a word too far off, or another word in its place
    [InlineData("ставь", "ссылку", 0.1f, "ставь", 0.6f, null)]
    [InlineData("ставь", "ссылку", 0.1f, "вставь", 1.0f, null)]
    [InlineData("ставь", "ссылку", 0.1f, "доставь", 0.6f, null)]
    [InlineData("ставь", "ссылку", 0.1f, "вставьте", 0.6f, null)]
    public void The_full_word_is_taken_only_where_the_window_heard_it(
        string suspect, string before, float beforeAt, string heard, float heardAt, string? expected) =>
        Assert.Equal(expected, QuietOnset.HeardFull(suspect, [new(before, beforeAt), new(heard, heardAt)], 0.6f));

    [Fact]
    public void An_empty_window_restores_nothing() =>
        Assert.Null(QuietOnset.HeardFull("ставь", [], 0.6f));

    [Theory]
    [InlineData("Ставь сюда.", 0, "вставь", "Вставь сюда.")]
    [InlineData("Ставьте текст поля.", 0, "вставьте", "Вставьте текст поля.")]
    [InlineData("Не ставь сюда, ставь туда.", 1, "вставь", "Не ставь сюда, вставь туда.")]
    [InlineData("Сначала скопируй, потом ставь сюда.", 0, "вставь", "Сначала скопируй, потом вставь сюда.")]
    // the occurrence counts only the words that may have lost their sound
    [InlineData("Я вставлю ссылку, а ты ставь лайк.", 0, "вставь", "Я вставлю ссылку, а ты вставь лайк.")]
    // an occurrence the text does not have changes nothing
    [InlineData("Ставь сюда.", 1, "вставь", "Ставь сюда.")]
    public void The_lost_sound_is_put_back_in_the_text_keeping_its_case(string text, int occurrence, string full, string expected) =>
        Assert.Equal(expected, QuietOnset.Restore(text, occurrence, full));

    [Fact]
    public void The_hint_holds_every_restored_form_in_both_cases()
    {
        Assert.Contains("вставь", QuietOnset.Hotwords);
        Assert.Contains("Вставь", QuietOnset.Hotwords);
        Assert.Contains("Вставьте", QuietOnset.Hotwords);
        Assert.DoesNotContain("ставь", QuietOnset.Hotwords);
    }
}
