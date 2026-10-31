using Vox.Core.Audio;
using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

public class PostProcessorTests
{
    [Fact]
    public void GigaAm_keeps_words_that_itn_would_break() =>
        Assert.Equal(" Один из вариантов.", new PostProcessor(ModelCatalog.GigaAm).Process("Один из вариантов."));

    [Fact]
    public void GigaAm_spells_out_ruble_sign() =>
        Assert.Equal(" Переведи 500 рублей на карту.", new PostProcessor(ModelCatalog.GigaAm).Process("Переведи 500 ₽ на карту."));

    [Fact]
    public void GigaAm_maps_cyrillic_tech_terms_to_latin() =>
        Assert.Contains("GitHub", new PostProcessor(ModelCatalog.GigaAm).Process("Запушь в гитхаб."), StringComparison.Ordinal);

    [Fact]
    public void Parakeet_converts_mixed_numbers() =>
        Assert.Equal(" У меня 36 задач.", new PostProcessor(ModelCatalog.Parakeet).Process("У меня 30 шесть задач."));

    [Fact]
    public void User_corrections_apply_before_everything_else() =>
        Assert.Equal(" Настрой Kubernetes.", new PostProcessor(ModelCatalog.GigaAm)
            .Process("Настрой кубернетис.", new Dictionary<string, string> { ["кубернетис"] = "Kubernetes" }));

    // 01.11, owner: "в" before a word on "в", "ф", "v" or "w" is one sound, and the model glues it on, often as "ф".
    [Theory]
    [InlineData("Открой вфиспере.", " Открой в Whisper.")]
    [InlineData("Запиши ввокс.", " Запиши в Vox.")]
    [InlineData("Ввокс запиши.", " В Vox запиши.")]
    [InlineData("Работаю ввоксе.", " Работаю в Vox.")]
    [InlineData("Открой фвиспере.", " Открой в Whisper.")]
    [InlineData("Открой ф виспере.", " Открой в Whisper.")]
    [InlineData("Открой вWhisper.", " Открой в Whisper.")]
    [InlineData("Залей вгитхаб.", " Залей в GitHub.")] // any glued "в", not only before "в" and "ф"
    [InlineData("Настрой висиэс.", " Настрой VCS.")] // a term of its own, not "в ECS"
    public void A_glued_preposition_is_split_off_a_term(string raw, string expected) =>
        Assert.Equal(expected, new PostProcessor(ModelCatalog.GigaAm).Process(raw));

    [Theory]
    [InlineData("Открой вфлюксар.")]
    [InlineData("Работаю вфлюксаре.")] // a case form
    public void A_glued_preposition_is_split_off_a_word_of_the_users_dictionary(string raw) =>
        Assert.Contains("в Fluxar.", new PostProcessor(ModelCatalog.GigaAm)
            .Process(raw, new Dictionary<string, string> { ["флюксар"] = "Fluxar" }), StringComparison.Ordinal);

    [Theory]
    [InlineData("Нажми ввод.")]
    [InlineData("Это вредит делу.")] // a Russian word, not "в Reddit"
    [InlineData("Это вводит в заблуждение.")]
    [InlineData("Держи вфокусе.")]
    [InlineData("Буква ф в слове.")]
    [InlineData("Сохрани в Vox.")]
    [InlineData("Вставь сюда.")]
    public void Ordinary_words_on_в_and_a_lone_ф_stay(string raw) =>
        Assert.Equal(" " + raw, new PostProcessor(ModelCatalog.GigaAm).Process(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_input_gives_empty_output(string raw) => Assert.Equal("", new PostProcessor(ModelCatalog.GigaAm).Process(raw));
}

public class ModelCatalogTests
{
    [Fact]
    public void GigaAm_is_the_default() => Assert.Same(ModelCatalog.GigaAm, ModelCatalog.Default);

    [Fact]
    public void Every_model_url_points_at_its_folder() =>
        Assert.All(ModelCatalog.All, m => Assert.EndsWith(m.ExtractedDir + ".tar.bz2", m.Url.AbsolutePath, StringComparison.Ordinal));

    [Fact]
    public void Find_returns_null_for_unknown_ids() => Assert.Null(ModelCatalog.Find("vosk-ru-small"));
}

public class SegmenterTests
{
    private const int Sr = 16000;

    private static float[] Speech(double seconds)
    {
        var rng = new Random(0);
        return Enumerable.Range(0, (int)(seconds * Sr)).Select(_ => (float)(rng.NextDouble() * 0.2 - 0.1)).ToArray();
    }

    private static float[] Silence(double seconds) => new float[(int)(seconds * Sr)];

    private static float[] Concat(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void Short_audio_is_a_single_piece()
    {
        var audio = Speech(8);
        var pieces = Segmenter.SplitOnPauses(audio, Sr, 12);
        Assert.Single(pieces);
        Assert.Equal(audio.Length, pieces[0].Count);
    }

    [Fact]
    public void Long_audio_is_split_into_pieces_not_longer_than_max()
    {
        var audio = Concat(Enumerable.Range(0, 6).SelectMany(_ => new[] { Speech(7), Silence(0.5) }).ToArray());
        var pieces = Segmenter.SplitOnPauses(audio, Sr, 12);
        Assert.True(pieces.Count >= 4);
        Assert.All(pieces, p => Assert.True(p.Count <= 12 * Sr));
    }

    [Fact]
    public void Split_loses_no_samples()
    {
        var audio = Concat(Speech(9), Silence(0.6), Speech(9), Silence(0.6), Speech(9));
        var pieces = Segmenter.SplitOnPauses(audio, Sr, 12);
        Assert.Equal(audio, pieces.SelectMany(p => p).ToArray());
    }

    [Fact]
    public void Cuts_land_inside_pauses()
    {
        var audio = Concat(Speech(9), Silence(0.6), Speech(9), Silence(0.6), Speech(9));
        var pos = 0;
        foreach (var piece in Segmenter.SplitOnPauses(audio, Sr, 12).SkipLast(1))
        {
            pos += piece.Count;
            Assert.All(audio[(pos - 160)..(pos + 160)], s => Assert.Equal(0f, s));
        }
    }

    [Fact]
    public void Audio_without_pauses_is_still_split()
    {
        var audio = Speech(30);
        var pieces = Segmenter.SplitOnPauses(audio, Sr, 12);
        Assert.All(pieces, p => Assert.True(p.Count <= 12 * Sr));
        Assert.Equal(audio.Length, pieces.Sum(p => p.Count));
    }

    [Fact]
    public void Empty_audio_gives_no_pieces() => Assert.Empty(Segmenter.SplitOnPauses([], Sr, 12));
}
