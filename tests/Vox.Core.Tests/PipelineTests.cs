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
