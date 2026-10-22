using System.Text.RegularExpressions;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>
/// The built-in IT/AI dictionary (Data/terms.json) and the owner's policy: Latin only for names, brands,
/// models, abbreviations and code; settled borrowings stay Cyrillic; ordinary words change only in a phrase.
/// </summary>
public sealed partial class TermsDictionaryTests
{
    [Theory]
    // ai-companies
    [InlineData("Модели от антропик и опен эй ай.", "Модели от Anthropic и OpenAI.")]
    [InlineData("Карты нвидиа на хаггинг фейс.", "Карты NVIDIA на Hugging Face.")]
    [InlineData("Есть гигачат и яндекс джипити.", "Есть GigaChat и YandexGPT.")]
    [InlineData("Видео в ранвей, музыка в суно, голос в элевен лабс.", "Видео в Runway, музыка в Suno, голос в ElevenLabs.")]
    // ai-models
    [InlineData("Спросил дипсик и квен.", "Спросил DeepSeek и Qwen.")]
    [InlineData("Картинки в миджорни и стейбл диффьюжн.", "Картинки в Midjourney и Stable Diffusion.")]
    [InlineData("Распознаёт виспер.", "Распознаёт Whisper.")]
    // ai-tools
    [InlineData("Пишу в клод коде, а не в виндсерфе.", "Пишу в Claude Code, а не в Windsurf.")]
    [InlineData("Сделал сайт в лавбл, конспект в ноутбук эл эм.", "Сделал сайт в Lovable, конспект в NotebookLM.")]
    [InlineData("Модель в олламе через эл эм студио.", "Модель в Ollama через LM Studio.")]
    [InlineData("Ноды в комфи юай, формат ггуф.", "Ноды в ComfyUI, формат GGUF.")]
    // automation
    [InlineData("Сценарий в эн восемь эн или запиер.", "Сценарий в n8n или Zapier.")]
    [InlineData("Агенты на лангчейн и ланг граф.", "Агенты на LangChain и LangGraph.")]
    // ai-concepts
    [InlineData("Подключи эм си пи к эл эл эм.", "Подключи MCP к LLM.")]
    [InlineData("Нужен рэг и эс ди кей.", "Нужен RAG и SDK.")]
    // general IT
    [InlineData("Подними в докер компоузе на убунту.", "Подними в docker-compose на Ubuntu.")]
    [InlineData("Проверь гитхаб экшенс.", "Проверь GitHub Actions.")]
    // Owner's log candidates (coordinator, 22.10).
    [InlineData("Открой оупен код и опенвид.", "Открой OpenCode и OpenVid.")]
    [InlineData("Через харнес силай и гит пул.", "Через Harness CLI и git pull.")]
    [InlineData("Ролик в ремоушен, анимация из миксамо.", "Ролик в Remotion, анимация из Mixamo.")]
    [InlineData("Кошелёк рэби уоллет на арбитрум.", "Кошелёк Rabby Wallet на Arbitrum.")]
    [InlineData("Лежит в гугл драйв, а не на гугл диске.", "Лежит в Google Drive, а не на Google Диске.")]
    [InlineData("Надо закомитить промп.", "Надо закоммитить промпт.")]
    [InlineData("Мы в гите храним, рендер в ремоушен студио.", "Мы в Git храним, рендер в Remotion Studio.")]
        // social (owner: brands in Latin too); "телеграмма" is a telegram, "зум" alone is a zoom
    [InlineData("Выложил на ютубе и в телеграме, ссылка в инстаграме.", "Выложил на YouTube и в Telegram, ссылка в Instagram.")]
    [InlineData("Пришла телеграмма, сделай зум.", "Пришла телеграмма, сделай зум.")]
    [InlineData("Ввожу в вээсэл команду, а в VSL потом curl.", "Ввожу в WSL команду, а в WSL потом curl.")]
        // stage 2 (docs/dictionary.md)
    [InlineData("Открой фотошоп и премьер про.", "Открой Photoshop и Premiere Pro.")]
    [InlineData("Залей в эс три через хашикорп волт.", "Залей в S3 через HashiCorp Vault.")]
    [InlineData("Скачай модель гемма и нампай.", "Скачай модель Gemma и NumPy.")]
    [InlineData("Проверь кей пи ай и си ти ар.", "Проверь KPI и CTR.")]
    [InlineData("Спроси клод про погоду.", "Спроси Claude про погоду.")] // "про" is not "Claude Pro" here
        public void Each_group_maps_its_terms(string spoken, string expected) =>
        Assert.Equal(expected, Terms.Replace(spoken));

    // Settled borrowings are written in Cyrillic (owner's decision): nothing to replace, only spelling fixes.
    [Theory]
    [InlineData("промпт токен агент нейросеть датасет эмбеддинг файнтюнинг трансформер токенизатор бэкенд фронтенд")]
    [InlineData("закоммить и запушь в ветку, сделай деплой на прод, поправь конфиг и эндпоинт")]
    [InlineData("юзер нажал кнопку, стейт обновился, хук вызвал колбэк, промис зарезолвился")]
    [InlineData("в датасете эмбеддинги, у агента инференс и пайплайн, батч и чекпоинт")]
    public void Settled_borrowings_stay_cyrillic(string text) => Assert.Equal(text, Terms.Replace(text));

    [Theory]
    [InlineData("фронт энд и бек энд", "фронтенд и бэкенд")]
    [InlineData("Файн тюнинг на дата сете.", "Файнтюнинг на датасете.")]
    [InlineData("нейро сети и ии агенты", "нейросети и ИИ-агенты")]
    public void Borrowings_get_their_spelling_fixed(string spoken, string expected) =>
        Assert.Equal(expected, Terms.Replace(spoken));

    // Ordinary Russian words (and names) that are also product names: alone they stay as they are.
    [Theory]
    [InlineData("Наведи курсор на агента, это опус и сонет, соната для Алисы.")]
    [InlineData("Лама и маяк, кодекс и лора, куда идти?")]
    [InlineData("Призма, чакра, кофе мока, болт и пика, мейк ап.")]
    [InlineData("Хром, кафка, андроид, юпитер, обсидиан и автоген.")]
    [InlineData("Кими, Сора и Джулс, крон деревьев, гамма и фокус.")]
    [InlineData("Мне дали три дня, стринги и паф.")]
    [InlineData("Дарт Вейдер, баш на баш, Великий Гэтсби.")]
    public void Ordinary_words_alone_are_not_replaced(string text) => Assert.Equal(text, Terms.Replace(text));

    [Theory]
    [InlineData("клод опус", "Claude Opus")]
    [InlineData("курсор агент", "Cursor Agent")]
    [InlineData("опенэйай кодекс", "OpenAI Codex")]
    [InlineData("апач кафка", "Apache Kafka")]
    [InlineData("мета лама", "Meta Llama")]
    [InlineData("пика лабс", "Pika Labs")]
    public void Ordinary_words_change_in_a_phrase(string spoken, string expected) =>
        Assert.Equal(expected, Terms.Replace(spoken));

    [Fact]
    public void Latin_targets_outside_the_spelling_group()
    {
        foreach (var (group, terms) in Terms.LoadGroups())
        {
            foreach (var target in terms.Keys)
            {
                var cyrillicOnly = target.All(c => !char.IsLetter(c) || c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
                Assert.True(group == "spelling" || !cyrillicOnly, $"{group}: \"{target}\" is Cyrillic — it belongs in \"spelling\"");
            }
        }
    }

    [Fact]
    public void Every_source_is_unique_and_made_of_words()
    {
        var seen = new HashSet<string>();
        foreach (var pair in Terms.LoadPairs())
        {
            var source = pair[0];
            Assert.True(SourceShape().IsMatch(source), $"\"{source}\" → {pair[1]}: words separated by single spaces");
            Assert.True(seen.Add(source.ToLowerInvariant()), $"\"{source}\" appears twice");
        }
    }

    [GeneratedRegex(@"^[\p{L}\p{Nd}_]+(?:-[\p{L}\p{Nd}_]+)*(?: [\p{L}\p{Nd}_]+(?:-[\p{L}\p{Nd}_]+)*)*$")]
    private static partial Regex SourceShape();
}
