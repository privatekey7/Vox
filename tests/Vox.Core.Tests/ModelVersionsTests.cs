using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Owner's request: "клод опус четыре и пять" → "Claude Opus 4.5" (brand + version).</summary>
public sealed class ModelVersionsTests
{
    [Theory]
    [InlineData("клод опус четыре и пять", "Claude Opus 4.5")]
    [InlineData("Работаю в клод сонет 4.5.", "Работаю в Claude Sonnet 4.5.")]
    [InlineData("клод хайку 4,5", "Claude Haiku 4.5")]
    [InlineData("клод три и пять сонет", "Claude 3.5 Sonnet")]
    [InlineData("сонет четыре и пять лучше", "Sonnet 4.5 лучше")]
    [InlineData("опус 4 и 1", "Opus 4.1")]
    [InlineData("джипити пять", "GPT-5")]
    [InlineData("джипити четыре о.", "GPT-4o.")]
    [InlineData("чат джипити", "ChatGPT")]
    [InlineData("джипити пять мини", "GPT-5 mini")]
    [InlineData("джемини два и пять про.", "Gemini 2.5 Pro.")]
    [InlineData("джемини 2.5 флэш отвечает", "Gemini 2.5 Flash отвечает")]
    [InlineData("дипсик эр один", "DeepSeek R1")]
    [InlineData("дипсик ви три", "DeepSeek V3")]
    [InlineData("квен три", "Qwen3")]
    [InlineData("лама три точка один", "Llama 3.1")]
    [InlineData("грок четыре", "Grok 4")]
    [InlineData("кими кей два", "Kimi K2")]
    public void Brand_and_version_are_written_as_the_model_name(string spoken, string expected) =>
        Assert.Equal(expected, ModelVersions.Apply(Terms.Replace(spoken)));

    [Theory]
    [InlineData("джемини два и пять про погоду", "Gemini 2.5 про погоду")] // "про" is a preposition here
    [InlineData("сонет 66 Шекспира", "сонет 66 Шекспира")] // a poem, not a model
    [InlineData("опус номер четыре", "опус номер четыре")]
    [InlineData("лама жила в зоопарке", "лама жила в зоопарке")]
    [InlineData("дипсик в 3 раза быстрее", "DeepSeek в 3 раза быстрее")] // "в" is a preposition
    public void Ordinary_words_and_prepositions_stay(string spoken, string expected) =>
        Assert.Equal(expected, ModelVersions.Apply(Terms.Replace(spoken)));

    [Theory]
    [InlineData("parakeet")]
    [InlineData("gigaam")]
    public void Whole_pipeline_keeps_the_version_for_both_models(string id)
    {
        var model = ModelCatalog.All.First(m => m.Id.StartsWith(id, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(" Пишу в Claude Opus 4.5 через Claude Code.",
            new PostProcessor(model).Process("Пишу в клод опус четыре и пять через клод код."));
    }
}
