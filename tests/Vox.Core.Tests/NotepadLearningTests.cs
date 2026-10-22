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
        Assert.Equal(("ppfi", "Pipify"), CorrectionDetector.Detect(before, after, Inserted));

    [Fact]
    public void Describe_lists_the_word_changes_for_the_log() =>
        Assert.Equal("1 change: \"сервис PPFI.\" → \"сайт Pipify.\"",
            CorrectionDetector.Describe("Задачи:\r\n Открой сервис PPFI.", "Задачи:\r\n Открой сайт Pipify.", Inserted));
}
