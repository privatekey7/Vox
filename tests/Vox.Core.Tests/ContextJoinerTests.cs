using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Owner request: continue the sentence where the cursor is instead of always starting a new one.</summary>
public sealed class ContextJoinerTests
{
    [Theory]
    // unknown context: as before (one leading space)
    [InlineData(" Привет.", null, null, " Привет.")]
    // start of the field / new line: no space, capital stays
    [InlineData(" Привет.", "", null, "Привет.")]
    [InlineData(" Привет.", "ь\n", null, "Привет.")]
    // after a sentence end: space + capital
    [InlineData(" Как дела?", "т.", null, " Как дела?")]
    [InlineData(" Как дела?", "т!", null, " Как дела?")]
    [InlineData(" Как дела?", ". ", null, "Как дела?")]
    // inside a sentence: space + lower case
    [InlineData(" Потом созвонимся.", "ет", null, " потом созвонимся.")]
    [InlineData(" Потом созвонимся.", "т,", null, " потом созвонимся.")]
    [InlineData(" Потом созвонимся.", "т:", null, " потом созвонимся.")]
    [InlineData(" Потом созвонимся.", " —", null, " потом созвонимся.")]
    [InlineData(" Потом созвонимся.", "т ", null, "потом созвонимся.")]
    [InlineData(" Потом созвонимся.", "12", null, " потом созвонимся.")]
    // after an opening bracket or quote: no space
    [InlineData(" Например.", " (", null, "Например.")]
    [InlineData(" Например.", " «", null, "Например.")]
    // abbreviations, mixed case, known terms and "I" keep their case
    [InlineData(" API упал.", "ак", null, " API упал.")]
    [InlineData(" GitHub лежит.", "ак", null, " GitHub лежит.")]
    [InlineData(" Docker запущен.", "ак", null, " Docker запущен.")]
    [InlineData(" I think so.", "nd", null, " I think so.")]
    [InlineData(" And then.", "it", null, " and then.")]
    // inserted in the middle of text: no final dot, space after
    [InlineData(" Очень", "то", "х", " очень ")]
    [InlineData(" Очень.", "то", "х", " очень ")]
    [InlineData(" Очень.", "то", " ", " очень")]
    [InlineData(" Правда?", "то", "х", " правда? ")]
    public void Joins_the_dictation_to_the_text_around_the_cursor(string text, string? before, string? after, string expected) =>
        Assert.Equal(expected, ContextJoiner.Apply(text, before, after));
}
