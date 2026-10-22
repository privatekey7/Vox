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
}
