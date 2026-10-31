using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>
/// 31.10, the owner fixed "Сан Whisper" → "Sunwhisper" four times with Ctrl+S and it still came back every other
/// time: the model writes the name "Сан Виспер" or "Сан-Виспер".
/// </summary>
public sealed class UserDictionaryTests
{
    private static string Fix(string raw, Dictionary<string, string> dictionary) =>
        new PostProcessor(ModelCatalog.GigaAm).Process(raw, dictionary).Trim();

    [Theory]
    [InlineData("Открой Сан Виспер.")]
    [InlineData("Открой Сан-Виспер.")]
    public void A_hyphen_and_a_space_are_the_same_in_a_learned_phrase(string raw)
    {
        Assert.Equal("Открой Sunwhisper.", Fix(raw, new() { ["сан-виспер"] = "Sunwhisper" }));
        Assert.Equal("Открой Sunwhisper.", Fix(raw, new() { ["сан виспер"] = "Sunwhisper" }));
    }

    [Fact]
    public void A_fix_learned_from_the_pasted_text_works_on_the_next_dictation() =>
        // The user fixes what was pasted: "Виспер" had already become "Whisper" by the term list.
        Assert.Equal("Открой Sunwhisper.", Fix("Открой Сан Виспер.", new() { ["сан whisper"] = "Sunwhisper" }));

    [Fact]
    public void An_entry_that_keeps_its_own_word_is_applied_once() =>
        Assert.Equal("Пишу в вокс бот.", Fix("Пишу в вокс.", new() { ["вокс"] = "вокс бот" }));
}
