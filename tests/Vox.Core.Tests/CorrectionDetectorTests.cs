using Vox.Core.Text;

namespace Vox.Core.Tests;

public class CorrectionDetectorTests
{
    [Fact]
    public void Replacing_a_misheard_word_with_its_spelling_is_a_correction()
    {
        var result = CorrectionDetector.Detect(
            before: "Задача: Нужно настроить кубернетис на сервере.",
            after: "Задача: Нужно настроить Kubernetes на сервере.",
            inserted: "Нужно настроить кубернетис на сервере.");

        Assert.Equal(("кубернетис", "Kubernetes"), result);
    }

    [Fact]
    public void Same_script_spelling_fix_is_a_correction() =>
        Assert.Equal(("дитхаб", "гитхаб"), CorrectionDetector.Detect("Запушь в дитхаб.", "Запушь в гитхаб.", "Запушь в дитхаб."));

    [Fact]
    public void Punctuation_around_the_word_is_ignored() =>
        Assert.Equal(("вокс", "Vox"), CorrectionDetector.Detect("Открой «вокс», пожалуйста", "Открой «Vox», пожалуйста", "Открой «вокс», пожалуйста"));

    [Fact]
    public void Rewriting_meaning_is_not_a_correction() =>
        Assert.Null(CorrectionDetector.Detect("У меня есть кот.", "У меня есть пёс.", "У меня есть кот."));

    [Fact]
    public void Several_changed_words_are_not_a_correction() =>
        Assert.Null(CorrectionDetector.Detect("Настрой кубернетис и докер.", "Настрой Kubernetes и Docker.", "Настрой кубернетис и докер."));

    [Fact]
    public void Edits_outside_the_dictation_are_ignored() =>
        Assert.Equal(("кубернетис", "Kubernetes"), CorrectionDetector.Detect(
            "Привет. Настрой кубернетис.", "Здравствуйте. Настрой Kubernetes.", "Настрой кубернетис."));

    [Fact]
    public void Untouched_text_is_not_a_correction() =>
        Assert.Null(CorrectionDetector.Detect("Настрой кубернетис.", "Настрой кубернетис. Дальше текст", "Настрой кубернетис."));

    [Fact]
    public void Missing_dictation_gives_nothing() =>
        Assert.Null(CorrectionDetector.Detect("совсем другой текст", "совсем другой текст!", "Настрой кубернетис."));

    [Fact]
    public void Case_only_change_is_not_learned() =>
        Assert.Null(CorrectionDetector.Detect("Привет вокс.", "Привет Вокс.", "Привет вокс."));

    [Fact]
    public void Deleted_word_is_not_a_correction() =>
        Assert.Null(CorrectionDetector.Detect("Ну вот настрой сервер.", "Ну настрой сервер.", "Ну вот настрой сервер."));

    [Fact]
    public void Works_inside_a_long_document()
    {
        var filler = string.Concat(Enumerable.Repeat("Обычный абзац текста без изменений. ", 300));
        var result = CorrectionDetector.Detect(
            filler + "Настрой кубернетис сегодня. " + filler,
            filler + "Настрой Kubernetes сегодня. " + filler,
            "Настрой кубернетис сегодня.");
        Assert.Equal(("кубернетис", "Kubernetes"), result);
    }

    [Theory]
    [InlineData("кубернетис", "Kubernetes", true)]
    [InlineData("джаваскрипт", "JavaScript", true)]
    [InlineData("кот", "пёс", false)]
    [InlineData("сервер", "клиент", false)]
    public void Similarity_bridges_cyrillic_and_latin(string a, string b, bool similar) =>
        Assert.Equal(similar, CorrectionDetector.LookAlike(a, b));

    // Regression: the owner's own grammar fixes were learned as dictionary entries
    // ("тёмные → тёмной", "блоге → логе") and then rewrote every later dictation.
    [Theory]
    [InlineData("тёмные", "тёмной")]
    [InlineData("тёмный", "тёмная")]
    [InlineData("сделал", "сделала")]
    [InlineData("блоге", "логе")]
    public void Ending_or_edge_letter_fixes_are_not_learned(string wrong, string correct) =>
        Assert.Null(CorrectionDetector.Detect($"Это {wrong} тут.", $"Это {correct} тут.", $"Это {wrong} тут."));

    [Theory]
    [InlineData("гитхаб", "GitHub")]
    [InlineData("керил", "Кирилл")]
    public void Misheard_words_are_still_learned(string wrong, string correct) =>
        Assert.Equal((wrong, correct), CorrectionDetector.Detect($"Это {wrong} тут.", $"Это {correct} тут.", $"Это {wrong} тут."));

    // Owner's Windows test (22.10): Vox wrote its own target "Fluxar" and the user fixed it to "Fluxor".
    [Theory]
    [InlineData("Новый Fluxar.", "Новый Fluxor.", "Fluxar", "Fluxor")]
    [InlineData("Открой GitHbu.", "Открой GitHub.", "GitHbu", "GitHub")]
    public void A_typo_fix_in_a_dictionary_target_is_a_correction(string before, string after, string wrong, string correct)
    {
        Assert.Equal((wrong, correct), CorrectionDetector.Detect(before, after, before, w => w == wrong));
        Assert.Null(CorrectionDetector.Detect(before, after, before)); // an ordinary word: a typo or grammar fix
        Assert.Equal($"a grammar or typo edit of an ordinary word; 1 change: \"{wrong}.\" → \"{correct}.\"",
            CorrectionDetector.Describe(before, after, before));
    }

    // Owner's Windows test: "треде" is a case form of "тред"; learning "треде → tray" would break "в треде".
    [Fact]
    public void Unlike_words_are_not_learned_and_the_log_says_why() =>
        Assert.Equal("the words are not alike (0.40); 1 change: \"треде\" → \"tray\"",
            CorrectionDetector.Describe("Свернуть в треде и выйти.", "Свернуть в tray и выйти.", "Свернуть в треде и выйти."));
}
