using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Owner report: "чтобы Вот, например": a long recording is cut at a pause and the model starts each piece
/// with a capital.</summary>
public sealed class PieceJoinerTests
{
    [Theory]
    // a piece with no sentence end: the next one goes on in lower case
    [InlineData("как сделать тёмную тему, чтобы", "Вот, например, жёлтый не сочетается.", "как сделать тёмную тему, чтобы вот, например, жёлтый не сочетается.")]
    [InlineData("но хотя бы", "В целом результат лучше.", "но хотя бы в целом результат лучше.")]
    [InlineData("мы зашли в магазин", "И купили хлеб.", "мы зашли в магазин и купили хлеб.")]
    [InlineData("подумаем,", "Как сделать лучше.", "подумаем, как сделать лучше.")]
    [InlineData("ну", "А-а, потом дошло.", "ну а-а, потом дошло.")]
    // a stop after a word no sentence ends with is the model's
    [InlineData("письмо ушло через.", "Почту, а не чат.", "письмо ушло через почту, а не чат.")]
    [InlineData("трафик шёл через...", "...Сервер в Европе.", "трафик шёл через сервер в Европе.")]
    [InlineData("сделать так, чтобы.", "Вот, например.", "сделать так, чтобы вот, например.")]
    [InlineData("сделать так, чтобы…", "Вот, например.", "сделать так, чтобы вот, например.")]
    // a real sentence end stays, also "с." after a number (seconds)
    [InlineData("Закрой окно.", "Не стоит его держать.", "Закрой окно. Не стоит его держать.")]
    [InlineData("за 5 с.", "Потом готово.", "за 5 с. Потом готово.")]
    [InlineData("Ты где?", "Я на месте.", "Ты где? Я на месте.")]
    // names, abbreviations, mixed case, terms and Latin keep their capital
    [InlineData("я позвоню", "Кириллу завтра.", "я позвоню Кириллу завтра.")]
    [InlineData("поедем в", "Москву летом.", "поедем в Москву летом.")]
    [InlineData("ответ пришёл от", "API сразу.", "ответ пришёл от API сразу.")]
    [InlineData("код лежит на", "GitHub давно.", "код лежит на GitHub давно.")]
    [InlineData("запусти", "Docker сначала.", "запусти Docker сначала.")]
    [InlineData("and then", "John came.", "and then John came.")]
    public void A_cut_mid_sentence_does_not_show(string first, string second, string expected)
    {
        Assert.Equal(expected, PieceJoiner.Join([first, second]));
    }

    [Fact]
    public void Empty_pieces_are_skipped()
    {
        Assert.Equal("раз два", PieceJoiner.Join(["  ", " раз ", "", "Два"]));
        Assert.Equal("", PieceJoiner.Join([]));
    }

    [Fact]
    public void One_piece_stays_as_the_model_wrote_it()
    {
        Assert.Equal("...и чтобы всё работало.", PieceJoiner.Join([" ...и чтобы всё работало. "]));
    }

    [Theory]
    [InlineData("Кирилла", true)]
    [InlineData("Москве", true)]
    [InlineData("Вера", false)] // also "вера"
    [InlineData("вот", false)]
    [InlineData("крузаде", false)]
    public void Names_are_told_from_words(string word, bool name)
    {
        Assert.Equal(name, RussianWords.IsName(word));
    }

    [Fact]
    public void GigaAm_is_cut_rarely()
    {
        // D030: with 12 s pieces all 12 of the owner's dictations of 13-27 s had a broken joint, with 30 s none.
        Assert.Equal(30, ModelCatalog.GigaAm.MaxPieceSeconds);
        Assert.Equal(12, ModelCatalog.Parakeet.MaxPieceSeconds);
    }
}
