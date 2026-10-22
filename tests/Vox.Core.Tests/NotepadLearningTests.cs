using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Owner's Notepad++ test (WM_GETTEXT): multi-line document, CRLF, auto-indent, Latin fix.</summary>
public sealed class NotepadLearningTests
{
    private const string Inserted = "Открой сервис PPFI.";

    [Theory]
    [InlineData("Первая строка.\r\n Открой сервис PPFI.", "Первая строка.\r\n Открой сервис Pipify.")]
    [InlineData("Первая строка.\r\n  Вторая.\r\n   Открой сервис PPFI.", "Первая строка.\r\n  Вторая.\r\n   Открой сервис Pipify.")]
    [InlineData("Задачи:\r\n Открой сервис PPFI.\r\n ", "Задачи:\r\n Открой сервис Pipify.\r\n ")] // auto-indent after Enter
    [InlineData("Задачи:\r\n Открой сервис PPFI.", "Задачи:\r\n Открой сервис Pipify.\r\n Дальше текст")] // the user typed on
    public void Latin_fix_in_a_notepad_document_is_detected(string before, string after) =>
        Assert.Equal(("PPFI", "Pipify"), CorrectionDetector.Detect(before, after, Inserted));

    [Fact]
    public void Describe_lists_the_word_changes_for_the_log() =>
        Assert.Equal("the words are not alike (0.20); 1 change: \"сервис PPFI.\" → \"сайт Pipify.\"",
            CorrectionDetector.Describe("Задачи:\r\n Открой сервис PPFI.", "Задачи:\r\n Открой сайт Pipify.", Inserted));

    // Owner's test: the model split the term in two words, the user typed on after the fix.
    [Fact]
    public void A_split_term_fixed_into_one_word_is_learned() =>
        Assert.Equal(("Zena Koder", "Zencoder"),
            CorrectionDetector.Detect("Заметки:\r\n Открой Zena Koder.", "Заметки:\r\n Открой Zencoder. привет", "Открой Zena Koder."));

    [Fact]
    public void A_learned_phrase_is_applied() =>
        Assert.Equal("Открой Zencoder.", UserCorrections.Apply("Открой Zena Koder.", new Dictionary<string, string> { ["zena koder"] = "Zencoder" }));

    [Fact]
    public void Edits_outside_the_dictation_are_reported_as_such() =>
        Assert.Equal("no change inside the dictation",
            CorrectionDetector.Describe("Открой сервис PPFI.", "Открой сервис PPFI.\r\n Дальше", Inserted));

    [Fact]
    public void Only_the_changed_part_of_a_hyphenated_word_is_the_pair() =>
        Assert.Equal(("ЛМ", "LLM"), CorrectionDetector.Detect("Нужна ЛМ-полировки текста.", "Нужна LLM-полировки текста.", "Нужна ЛМ-полировки текста."));
}
