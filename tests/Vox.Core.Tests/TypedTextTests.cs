using Vox.Core.Clipboard;

namespace Vox.Core.Tests;

public sealed class TypedTextTests
{
    [Theory]
    [InlineData(" Запушь в GitHub.", " Запушь в GitHub.")]   // the leading space is typed too
    [InlineData("первая\r\nвторая", "первая вторая")]         // no Enter: it would send a message or run a command
    [InlineData("первая\nвторая\n", "первая вторая")]
    [InlineData("слово \n\tслово", "слово слово")]
    [InlineData("до\u2028после", "до после")]                 // line separator
    [InlineData("смайлик 😀 остался", "смайлик 😀 остался")]   // a surrogate pair is one character
    [InlineData("", "")]
    public void Line_breaks_become_spaces_and_the_rest_is_typed_as_is(string text, string expected) =>
        Assert.Equal(expected, TypedText.ForTyping(text));

    [Fact]
    public void A_lone_surrogate_is_dropped() =>
        Assert.Equal("сломанный", TypedText.ForTyping("сломан" + (char)0xD800 + "ный"));
}
