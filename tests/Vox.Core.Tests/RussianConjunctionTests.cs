using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>
/// Owner's log 09.11: next to Latin words GigaAM writes the Russian "и" as "and" ("Handy and Vox"), a drawn-out "и-и-и"
/// as "and and and and", and a lone "и" as a Latin "i".
/// </summary>
public class RussianConjunctionTests
{
    [Theory]
    [InlineData("Я просто сказал Handy and Vox.", "Я просто сказал Handy и Vox.")]
    [InlineData("Сравнение Handy and and and and Vox.", "Сравнение Handy и Vox.")]
    [InlineData("Хотя я просто сказал Handy and and.", "Хотя я просто сказал Handy и.")]
    [InlineData("Вот он снова пишет and and вместо i.", "Вот он снова пишет и вместо и.")]
    [InlineData("Я сказал Python and C#.", "Я сказал Python и C#.")]
    [InlineData("Сравни Python and Rust.", "Сравни Python и Rust.")]
    [InlineData("Надо чётко сказать: Python and& C#.", "Надо чётко сказать: Python и C#.")]
    [InlineData("Поставь Node.js and React на сервер.", "Поставь Node.js и React на сервер.")]
    [InlineData("Это Tom and Jerry.", "Это Tom и Jerry.")]
    [InlineData("Сравни Handy, and, and Vox.", "Сравни Handy и Vox.")]
    [InlineData("Это готово. Handy and Vox работают.", "Это готово. Handy и Vox работают.")]
    [InlineData("Open the door. Сравни Handy and Vox.", "Open the door. Сравни Handy и Vox.")]
    [InlineData("Купи хлеб i молоко.", "Купи хлеб и молоко.")]
    [InlineData("i всё.", "и всё.")]
    public void Russian_and_written_in_Latin_becomes_и(string raw, string expected) =>
        Assert.Equal(expected, RussianConjunction.Fix(raw));

    [Theory]
    [InlineData("Use Python and Rust.")] // no Russian in the sentence
    [InlineData("Handy and and Vox.")]
    [InlineData("Включи Rock and Roll.")] // two ordinary English words: an English phrase
    [InlineData("Это Dungeons and Dragons.")]
    [InlineData("Сделай drag and drop.")] // lower case: English words, not names
    [InlineData("Skills, Frount and design.")]
    [InlineData("Это Proof of Concept and MVP.")] // an English phrase around it
    [InlineData("Скажи: see you and good luck.")]
    [InlineData("Ну, в i Digest было.")] // "i" before a Latin word is a name ("iDigest")
    [InlineData("Это Пётр I.")] // a Roman numeral
    [InlineData("Нужен i18n и i.e.")]
    [InlineData("Это Android, а не iOS.")]
    [InlineData("Набери band and land.")]
    public void Other_and_stays(string raw) => Assert.Equal(raw, RussianConjunction.Fix(raw));

    [Fact]
    public void Owner_dictation_goes_through_the_pipeline() =>
        Assert.Equal(
            " А сделай мне красивую картинку, инфографику сравнение Handy и Vox.",
            new PostProcessor(ModelCatalog.GigaAm).Process("А сделай мне красивую картинку, инфографику, а-а, сравнение Handy and and and and Vox."));
}
