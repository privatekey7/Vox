using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>
/// Owner's log 09.11: the model writes a stutter as heard ("ш-ш-что су-шустрая", "во-во-вот", "как-какое") and a
/// repeated short word ("мне, мне", "что что"). Hyphenated words and repeats said on purpose stay.
/// </summary>
public class StuttersTests
{
    [Theory]
    [InlineData("Бросается в глаза ш-ш-что су-шустрая.", "Бросается в глаза что шустрая.")]
    [InlineData("Ну во-во-вот так.", "Ну вот так.")]
    [InlineData("А как-какое у нас время?", "А какое у нас время?")]
    [InlineData("П-п-привет всем.", "Привет всем.")]
    [InlineData("Су-шустрая модель.", "Шустрая модель.")]
    [InlineData("Это за-жаркий день.", "Это жаркий день.")]
    [InlineData("Сделай э-э-это сейчас.", "Сделай это сейчас.")]
    [InlineData("Мне, мне кажется, да.", "Мне кажется, да.")]
    [InlineData("Это так. Ну, ну вот.", "Это так. Ну вот.")]
    [InlineData("Дай мне мне тоже.", "Дай мне тоже.")]
    [InlineData("Я думаю, что что-то сломалось.", "Я думаю, что что-то сломалось.")]
    [InlineData("Он сказал, что что он придёт.", "Он сказал, что он придёт.")]
    [InlineData("Но, но это не так.", "Но это не так.")]
    [InlineData("Ну ну ладно.", "Ну ладно.")]
    [InlineData("Поговори с с ним.", "Поговори с ним.")]
    [InlineData("Мы мы мы готовы.", "Мы готовы.")]
    public void Stutter_and_repeated_word_go(string raw, string expected) => Assert.Equal(expected, Stutters.Remove(raw));

    [Theory]
    [InlineData("Что-то как-то всё-таки где-то по-моему из-за этого.")]
    [InlineData("Во-первых, во-вторых, по-русски, по-прежнему, кто-то.")]
    [InlineData("Да-да, нет-нет, раз-раз, на-на-на, ку-ку, не-не.")]
    [InlineData("Это вы-вы.")] // a whole word repeated: may be on purpose
    [InlineData("Нужен веб-хук и чек-лист, сан-виспер и ай-адреса.")]
    [InlineData("Это эй-ай и м-да.")]
    [InlineData("Смонтируй в кап-катом.")]
    [InlineData("Фла-флут остаётся.")] // three letters that are not the word's start
    [InlineData("Вот, вот, смотри. Да, да, так, так.")] // repeats said on purpose
    [InlineData("Сделал, сделал и было, было.")]
    [InlineData("Это C плюс плюс.")]
    [InlineData("Подскажи мне, мне кажется, ты прав.")] // mid-sentence a comma parts two clauses
    [InlineData("Нужен аккаунт Про, Про который.")] // a name, not a preposition
    [InlineData("В-ноль и й-базе.")]
    [InlineData("Пол-яблока, кое-что, из-под стола.")]
    public void Other_words_stay(string raw) => Assert.Equal(raw, Stutters.Remove(raw));

    [Fact]
    public void Owner_dictation_goes_through_the_pipeline() =>
        Assert.Equal(
            " Бросается в глаза что шустрая, не надо тогда писать.",
            new PostProcessor(ModelCatalog.GigaAm).Process("А-а, бросается в глаза ш-ш-что су-шустрая, не надо тогда писать."));
}
