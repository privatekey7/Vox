using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Owner's log: Notepad++ exposes no UIA text, so nothing was ever learned there.</summary>
public sealed class FieldReadPolicyTests
{
    [Theory]
    [InlineData(true, false, -1, FieldSource.Uia)]
    [InlineData(true, false, 500, FieldSource.Uia)] // UIA wins when both work
    [InlineData(false, false, 500, FieldSource.WindowText)] // Notepad++ (Scintilla)
    [InlineData(false, true, 500, FieldSource.None)] // a password EDIT is never read
    [InlineData(true, true, 500, FieldSource.None)]
    [InlineData(false, false, -1, FieldSource.None)] // the control did not answer
    [InlineData(false, false, 0, FieldSource.None)]
    [InlineData(false, false, FieldReadPolicy.MaxWindowTextChars + 1, FieldSource.None)] // a huge document
    public void Chooses_how_to_read_the_field(bool uia, bool password, int length, FieldSource expected) =>
        Assert.Equal(expected, FieldReadPolicy.Choose(uia, password, length));

    // Owner's log (22.10, 15:54): the first dictation after Notepad++ started got no answer in time
    // and was never read again ("does not expose the field's text").
    [Theory]
    [InlineData(-1, FieldSource.WindowText)] // busy applying the paste: read again
    [InlineData(0, FieldSource.WindowText)] // a new, still empty buffer
    [InlineData(500, FieldSource.WindowText)]
    [InlineData(FieldReadPolicy.MaxWindowTextChars + 1, FieldSource.None)]
    public void A_text_control_that_did_not_answer_yet_is_read_again(int length, FieldSource expected)
    {
        Assert.Equal(expected, FieldReadPolicy.Choose(false, false, length, isTextControl: true));
        Assert.Equal(FieldSource.None, FieldReadPolicy.Choose(false, true, length, isTextControl: true)); // never a password
    }
}
