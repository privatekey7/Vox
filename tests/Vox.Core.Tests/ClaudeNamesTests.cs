using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Owner's log: GigaAM writes Claude as "Cloud", "клауд", "клот" (~30 times).</summary>
public sealed class ClaudeNamesTests
{
    private static string Ru(string raw) => new PostProcessor(ModelCatalog.GigaAm).Process(raw).Trim();

    [Theory]
    [InlineData("Открой Cloud.", "Открой Claude.")]
    [InlineData("Спроси у клауда.", "Спроси у Claude.")]
    [InlineData("Запусти Cloud Code.", "Запусти Claude Code.")]
    [InlineData("Работаю в клоуд коде.", "Работаю в Claude Code.")]
    [InlineData("Есть Cloud and Chrome.", "Есть Claude in Chrome.")]
    [InlineData("Поставь клаудн хром.", "Поставь Claude in Chrome.")]
    [InlineData("Открой клод в дископте.", "Открой Claude в дископте.")]
    [InlineData("Открой клод дископ.", "Открой Claude Desktop.")]
    [InlineData("Это клот.", "Это Claude.")]
    [InlineData("Cloud Opus четыре и пять.", "Claude Opus 4.5.")]
    public void Russian_model_cloud_is_claude(string raw, string expected) => Assert.Equal(expected, Ru(raw));

    [Theory]
    [InlineData("Лежит в Google Cloud.", "Лежит в Google Cloud.")]
    [InlineData("Сервер в Яндекс Клауд.", "Сервер в Yandex Cloud.")]
    [InlineData("Стоит Cloudflare и iCloud.", "Стоит Cloudflare и iCloud.")]
    [InlineData("Деплой в Cloud Run.", "Деплой в Cloud Run.")]
    public void Cloud_products_stay(string raw, string expected) => Assert.Equal(expected, Ru(raw));

    [Fact]
    public void English_model_keeps_the_cloud() =>
        Assert.Equal("Save it to the cloud.", new PostProcessor(ModelCatalog.Parakeet).Process("Save it to the cloud.").Trim());
}
