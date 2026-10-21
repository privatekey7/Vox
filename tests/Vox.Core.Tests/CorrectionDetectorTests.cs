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
}
