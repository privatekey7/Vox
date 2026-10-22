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
    // Owner's log: glued versions and GPT Image.
    [InlineData("чат GPT6.1", "ChatGPT 6.1")]
    [InlineData("GPT6.1", "GPT-6.1")]
    [InlineData("джипити имейдж два", "GPT Image 2")]
    [InlineData("джипити имейдж два пять", "GPT Image 2.5")] // owner's corpus: no "и"
    [InlineData("клод опус пять пять", "Claude Opus 5.5")]
    // Owner's live dictation (GigaAM).
    [InlineData("на опусе версии 5.5", "на Opus 5.5")]
    [InlineData("GPT версии 5", "GPT-5")]
    [InlineData("GPT Mage 2.5", "GPT Image 2.5")]
    [InlineData("GTA Шесть", "GTA 6")]
    [InlineData("код-код на опусе версии 5.5", "Claude Code на Opus 5.5")]
    public void Brand_and_version_are_written_as_the_model_name(string spoken, string expected) =>
        Assert.Equal(expected, ModelVersions.Apply(Terms.Replace(spoken)));

    [Theory]
    [InlineData("джемини два и пять про погоду", "Gemini 2.5 про погоду")] // "про" is a preposition here
    [InlineData("сонет 66 Шекспира", "сонет 66 Шекспира")] // a poem, not a model
    [InlineData("опус номер четыре", "опус номер четыре")]
    [InlineData("лама жила в зоопарке", "лама жила в зоопарке")]
    [InlineData("дипсик в 3 раза быстрее", "DeepSeek в 3 раза быстрее")] // "в" is a preposition
    [InlineData("клод восемнадцать плюс", "Claude восемнадцать плюс")] // regression (owner's log): not a version
    [InlineData("клод 4 тоже", "Claude 4 тоже")]
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

    // Owner's live log (22.10): GigaAM's own spellings of languages, engines and Claude models.
    [Theory]
    [InlineData("Пишу на Сишар.", " Пишу на C#.")]
    [InlineData("Сшарп лучше.", " C# лучше.")]
    [InlineData("Sisharp лучше.", " C# лучше.")]
    [InlineData("Пишу Nasisharp.", " Пишу на C#.")]
    [InlineData("язык программирования Sharp.", " язык программирования C#.")]
    [InlineData("Sharp выпустил телевизор.", " Sharp выпустил телевизор.")] // the brand stays without context
    [InlineData("Ева Скрипт и Javascript.", " JavaScript и JavaScript.")]
    [InlineData("Пишу на Гоу.", " Пишу на Go.")]
    [InlineData("гоу гулять", " гоу гулять")] // slang stays
    [InlineData("Andreal Enjane и анриал энджин.", " Unreal Engine и Unreal Engine.")]
    [InlineData("Открой клодкот.", " Открой Claude Code.")]
    [InlineData("Claude опуск пять и пять.", " Claude Opus 5.5.")]
    [InlineData("Клодопус 5,5 лучше.", " Claude Opus 5.5 лучше.")]
    [InlineData("Опуск 5 и пять.", " Opus 5.5.")]
    [InlineData("Цена 5,5 рубля.", " Цена 5,5 рубля.")] // an ordinary number stays
    [InlineData("Фронтент и фронтанд.", " Фронтенд и фронтенд.")]
    [InlineData("Это Frount End.", " Это фронтенд.")] // the model's Latin capital is not kept mid-sentence
    [InlineData("Fruentand готов. Bek End тоже.", " Фронтенд готов. Бэкенд тоже.")] // but is at a sentence start
    [InlineData("Это Вэкенд.", " Это Бэкенд.")] // a Cyrillic capital is the speaker's: kept
    [InlineData("Нужен фронт-энд-инженер.", " Нужен фронтенд-инженер.")]
    [InlineData("Это вэкенд и бэкэнд.", " Это бэкенд и бэкенд.")]
    [InlineData("Нужна Лэм полировка.", " Нужна LLM-полировка.")]
    [InlineData("Это Лэлмэ.", " Это LLM.")]
    [InlineData("Модель LM.", " Модель LM.")] // a bare LM stays
    [InlineData("Piton, Pyton, Pigon.", " Python, Python, Python.")]
    [InlineData("пишу на Питоне", " пишу на Python")]
    [InlineData("Живёт питон в зоопарке.", " Живёт питон в зоопарке.")] // the snake stays
    [InlineData("Cisharp лучше.", " C# лучше.")]
    [InlineData("Открой Hrome.", " Открой Chrome.")]
    [InlineData("Sounddesign трека.", " Саунд-дизайн трека.")]
    public void Live_log_spellings_are_fixed(string raw, string expected) =>
        Assert.Equal(expected, new PostProcessor(ModelCatalog.GigaAm).Process(raw));
}
