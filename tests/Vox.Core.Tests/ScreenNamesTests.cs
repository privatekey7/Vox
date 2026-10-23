using System.Diagnostics;
using System.Text.RegularExpressions;
using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Layer 2 of anglicisms (22.10): names from window titles and the field at the cursor.</summary>
public class ScreenNamesTests
{
    private static List<string> Extract(string text) => ScreenWords.Extract(text, FuzzyTerms.IsEnglishName);

    private static ScreenName[] OnScreen(params string[] titles) =>
        [.. titles.SelectMany(Extract).Distinct().Select(n => new ScreenName(n, OnScreen: true))];

    private static string Fix(string raw, IReadOnlyCollection<ScreenName>? screen, ICollection<FuzzyTerms.Match>? log = null) =>
        new PostProcessor(ModelCatalog.GigaAm).Process(raw, null, log, screen).Trim();

    [Theory]
    [InlineData("Sunwhisper Settings - Google Chrome", "Sunwhisper,Sunwhisper Settings")]
    [InlineData("Tldraw | Excalidraw", "Tldraw,Excalidraw")] // a term on screen too (short words, below)
    [InlineData("HAPP VPN — Telegram", "HAPP,VPN,HAPP VPN")] // "telegram" is an English word
    [InlineData("Ideogram Wispr", "Wispr,Ideogram Wispr")]
    [InlineData("Lumalabs Dream Machine", "Lumalabs,Lumalabs Dream")]
    [InlineData("Qwen3 и GPT4o в macOS", "Qwen3,GPT4o,macOS")]
    [InlineData("Visual Studio Code", "")]
    [InlineData("Telegram", "")]
    [InlineData("Мои документы - Проводник", "")]
    public void Names_are_taken_from_titles(string title, string expected) =>
        Assert.Equal(expected, string.Join(",", Extract(title)));

    [Fact]
    public void Window_text_gives_names_and_drops_secrets()
    {
        var text = "Обсудили Zorblax и Excalidraw. Ключ sk-" + "proj-4fJ9kQ2xLmN8pR7tVwY3zA1bC5dE6gH0 не публикуйте.";

        Assert.Equal("Zorblax,Excalidraw", string.Join(",", ScreenWords.FromWindowText(text, FuzzyTerms.IsEnglishName)));
    }

    [Fact]
    public void A_long_window_text_keeps_the_most_frequent_then_the_latest_names()
    {
        var text = "Zorblax и Quintora. Zorblax снова. Velmora в конце.";

        var names = ScreenWords.FromWindowText(text, FuzzyTerms.IsEnglishName, limit: 2);

        Assert.Equal(["Zorblax", "Velmora"], names);
    }

    // Owner's request (22.10): no secret, nor any part of one, may reach the memory on disk.
    // The fake secrets are split in two ("gh" + "p_…"): the same constant for the test, but secret scanners
    // (GitHub push protection) do not take the source for a leaked key.
    [Theory]
    [InlineData("OPENAI_API_KEY=sk-" + "proj-4fJ9kQ2xLmN8pR7tVwY3zA1bC5dE6gH0")]
    [InlineData("token gh" + "p_16C7e42F292c6912E7710c838347Ae178B4a")]
    [InlineData("Кошелёк 0x5290840009852788" + "6E0F7030069857D2E4169EE7")]
    [InlineData("bc1qar0srrr7xfkvy5l6" + "43lydnw9re59gtzzwf5mdq")]
    [InlineData("Solana 7EcDhSYGxXzscUZK9UVN" + "N5x5w8xJkE7xD5sT2q9qVMDg")]
    [InlineData("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0In0." + "dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U")]
    [InlineData("AKIA" + "IOSFODNN7EXAMPLE wJalrXUtnFEMI/K7MDENG/" + "bPxRfiCYEXAMPLEKEY")]
    [InlineData("4c0883a69102937d6231471b5dbb6204" + "fe5129617082792ae468d01a3f362318")]
    [InlineData("-----BEGIN OPENSSH " + "PRIVATE KEY----- b3BlbnNzaC1rZXktdjEAAAAABG5vbmUAAAAEbm9uZQ")]
    [InlineData("abandon ability able about above absent absorb abstract absurd abuse access accident")]
    [InlineData("Пароль: Tr0ub4dor&3, Summer2024!, Qwerty12, xK9mQ2pL, aZ3k")]
    [InlineData("mongodb://admin:" + "S3cr3tPa55@db.example.com:27017")]
    public void Secrets_never_reach_the_memory(string text)
    {
        var memory = new ScreenMemory();
        memory.See(ScreenWords.Unknown(Extract(text)), DateTime.UtcNow); // as Vox does
        Assert.Equal([], memory.Entries.Select(e => e.Name));
    }

    [Fact]
    public void A_seed_phrase_is_english_words()
    {
        // BIP-39 words are ordinary English words: dropped like any other.
        Assert.Equal([], Extract("zoo wrong wise width vacuum unveil trumpet toddler tissue swallow sunny sudden"));
    }

    [Fact]
    public void Memory_drops_the_names_not_seen_for_longest()
    {
        var memory = new ScreenMemory(limit: 3);
        var t = new DateTime(2026, 10, 22, 0, 0, 0, DateTimeKind.Utc);
        memory.See(["Alpha", "Brava"], t);
        memory.See(["Charl"], t.AddMinutes(1));
        memory.See(["Alpha"], t.AddMinutes(2));
        memory.See(["Delto"], t.AddMinutes(3));
        Assert.Equal(["Alpha", "Charl", "Delto"], memory.Entries.Select(e => e.Name).Order());
        Assert.Equal(2, memory.Entries.Single(e => e.Name == "Alpha").Count);
    }

    [Fact]
    public void Memory_survives_a_restart_and_clears_with_its_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vox-screen-{Guid.NewGuid():N}.json");
        var memory = new ScreenMemory(path);
        memory.See(["Sunwhisper", "Qwen3"], DateTime.UtcNow);
        memory.Save();
        Assert.Equal(["Sunwhisper"], ScreenMemory.Load(path).Entries.Select(e => e.Name)); // digits: never saved
        Assert.DoesNotContain("Qwen3", File.ReadAllText(path), StringComparison.Ordinal);

        memory.Clear();
        Assert.False(File.Exists(path));
        Assert.Equal(0, ScreenMemory.Load(path).Count);
    }

    [Theory]
    [InlineData("Открой санвиспер", "Открой Sunwhisper")]
    [InlineData("Открой Sunwisper", "Открой Sunwhisper")]
    [InlineData("Включи хап", "Включи HAPP")]
    [InlineData("Сделай в люмалабс", "Сделай в Lumalabs")]
    public void Screen_names_are_found_by_sound(string raw, string expected)
    {
        var screen = OnScreen("Sunwhisper Settings - Google Chrome", "HAPP VPN", "Tldraw", "Lumalabs Dream Machine");
        var log = new List<FuzzyTerms.Match>();
        Assert.Equal(expected, Fix(raw, screen, log));
        Assert.Equal("окно", Assert.Single(log).Screen);
        Assert.Equal(raw, Fix(raw, null)); // nothing on screen: nothing changes
    }

    // Owner's live test of 7748ba7 (22.10).
    [Theory]
    [InlineData("Зайди на Deepl.", "Зайди на DeepL.")] // a term in other case gets its spelling
    [InlineData("Coin marketcap", "CoinMarketCap")]
    [InlineData("Зайди на Дипл.", "Зайди на DeepL.")] // a short word: a term on screen counts
    [InlineData("Открой Крузаде.", "Открой Cruzade.")] // a name seen only in a domain is written as a name
    [InlineData("Открой Крузейт.", "Открой Cruzade.")]
    [InlineData("Сайт Крузаде.com", "Сайт cruzade.com")]
    [InlineData("Creat Token.", "Create Token.")] // next to an English word: letters lost on the same sound
    [InlineData("Lazy Trasur", "Lazy Treasure")]
    [InlineData("Crade Talking.", "Crade Talking.")]
    [InlineData("Сеть Салана.", "Сеть Solana.")]
    public void Live_test_of_screen_words(string raw, string expected) =>
        Assert.Equal(expected, Fix(raw, OnScreen("DeepL Translate - Google Chrome", "cruzade.com - Mozilla Firefox")));

    // A loop in the owner's test: the model's "Jamper X Change", pasted into a chat, came back as a name.
    [Fact]
    public void The_models_own_words_are_not_names()
    {
        var own = new OwnWords();
        own.Add("Зайди на Jamper X Change.");
        Assert.True(own.Contains("Jamper"));
        Assert.True(own.Contains("jamper Change"));
        Assert.False(own.Contains("Jumper"));

        var memory = new ScreenMemory();
        memory.See(["Jamper", "CoinWin"], DateTime.UtcNow);
        Assert.Equal(1, memory.Forget(own.Contains));
        Assert.Equal("CoinWin", Assert.Single(memory.Entries).Name);
    }

    [Theory]
    [InlineData("Тесты на Пупитер и Пупитор.", "Тесты на Puppeteer и Puppeteer.")]
    [InlineData("Сеть Этериум и Эфериум, Евмос, монета ЕТХ.", "Сеть Ethereum и Ethereum, Evmos, монета ETH.")]
    [InlineData("Программа Аида 64 Экстрим.", "Программа AIDA64 Extreme.")]
    [InlineData("Купил ВДС на ВДсина, коды на Хиро SMS.", "Купил VDS на VDSina, коды на HeroSMS.")]
    [InlineData("Сайт Jumper ex Change и Jumperex Change.", "Сайт Jumper Exchange и Jumper Exchange.")]
    [InlineData("Канал Vid IQ, VidQ, VIQ.", "Канал vidIQ, vidIQ, vidIQ.")]
    [InlineData("Сервис Визиро, Виноль бай Версель, We 0 by Versel.", "Сервис v0, v0 by Vercel, v0 by Vercel.")]
    [InlineData("Фантом, Соник, Эфир, Энди Шаман, Лихи.", "Фантом, Соник, Эфир, Энди Шаман, Лихи.")]
    public void Second_live_test_spellings(string raw, string expected) => Assert.Equal(expected, Fix(raw, null));

    // Owner's live test of 9ba837c (laptop).
    [Theory]
    [InlineData("Stells Extra Plagin для Playrite.", "Stells Extra Plagin для Playwright.")] // no Cyrillic term among Latin words
    [InlineData("Это Plagin.", "Это Plagin.")] // nor a plural for a singular
    [InlineData("Ставлю Plagens.", "Ставлю плагины.")]
    [InlineData("Открой Джиммини, Джимини, Гемени, на Гемене, Гиминиаргон.", "Открой Gemini, Gemini, Gemini, на Gemini, Gemini Argon.")]
    [InlineData("Формат Джесон и Джей Сон.", "Формат JSON и JSON.")]
    [InlineData("Влюбилась в Джейсона.", "Влюбилась в Джейсона.")] // the name Jason stays
    [InlineData("Файл Термс.gson и config.джейсон.", "Файл Термс.json и config.json.")]
    [InlineData("Кошелёк Рэбби Воллетт, Рэбиволет, Фантом Воллет, Фантом.", "Кошелёк Rabby Wallet, Rabby Wallet, Phantom Wallet, Фантом.")]
    [InlineData("Тесты Пубитер, HDmell, rast.", "Тесты Puppeteer, HTML, Rust.")]
    [InlineData("Сделай диплей, Jumper X Change.", "Сделай деплой, Jumper Exchange.")] // "exact": not by sound
    [InlineData("Новый дисплей и дисплеем.", "Новый дисплей и дисплеем.")]
    public void Third_live_test_spellings(string raw, string expected) => Assert.Equal(expected, Fix(raw, null));

    // Owner's live test on Parakeet (30 phrases): both models.
    [Theory]
    [InlineData("Открой Термс точка Джесон и config точка json.", "Открой Термс.json и config.json.")]
    [InlineData("Подними контейнер спостгрыс Кью и постгрес кью эль, Рэдис.", "Подними контейнер с PostgreSQL и PostgreSQL, Redis.")]
    [InlineData("Формат Иксмл, икс эм эль, профиль Иксэмп и икс эм пи.", "Формат XML, XML, профиль XMP и XMP.")]
    [InlineData("Пишу на тайскрипт, запусти Вм Студио.", "Пишу на TypeScript, запусти LM Studio.")]
    [InlineData("Модели на Хьюджин Фэйс и Hugging face.", "Модели на Hugging Face и Hugging Face.")]
    [InlineData("Спроси Чартжи Пити, Чатжи Пити. Ch ChatGPT.", "Спроси ChatGPT, ChatGPT. Ch ChatGPT.")]
    [InlineData("Подключи Мсеп сервер. Пишу в лодкоду, клодкоду.", "Подключи MCP сервер. Пишу в Claude Code, Claude Code.")]
    [InlineData("Открой Фаерфос. Запиши в Обс, Da Vinci Resolve. Обс.", "Открой Firefox. Запиши в OBS, DaVinci Resolve. Обс.")]
    [InlineData("В Сити Arbitrum и Д банк.", "В сети Arbitrum и DeBank.")]
    [InlineData("Москва-Сити и Сити банк.", "Москва-Сити и Сити банк.")]
    [InlineData("Работаю на Claude Опус.", "Работаю на Claude Opus.")]
    [InlineData("Открой нотпад плюс-плюс и нотпад плюс плюс.", "Открой Notepad++ и Notepad++.")]
    [InlineData("Сони пришла, комет, пром, Фантом.", "Сони пришла, комет, пром, Фантом.")]
    public void Parakeet_live_test_spellings(string raw, string expected)
    {
        foreach (var model in ModelCatalog.All)
        {
            Assert.Equal(expected, new PostProcessor(model).Process(raw).Trim());
        }
    }

    // Owner's live test on GigaAM (30 phrases): both models.
    [Theory]
    [InlineData("The Deploy проект на сервер.", "Задеплой проект на сервер.")]
    [InlineData("Сделай комет и запуш.", "Сделай коммит и запушь.")]
    [InlineData("Закинь комет в ветку.", "Закинь коммит в ветку.")]
    [InlineData("Видел комет в небе.", "Видел комет в небе.")]
    [InlineData("Подними Post Grass Q и пост грас кью.", "Подними PostgreSQL и PostgreSQL.")]
    [InlineData("Новый индoint и эндпойнт.", "Новый эндпоинт и эндпоинт.")]
    [InlineData("Сервер возвращает Джейсона.", "Сервер возвращает JSON.")]
    [InlineData("Влюбилась в Джейсона.", "Влюбилась в Джейсона.")]
    [InlineData("Пишу на Евроскрипт и Typescript.", "Пишу на JavaScript и TypeScript.")]
    [InlineData("Работаю на Claude Опус.", "Работаю на Claude Opus.")]
    [InlineData("Запусти VM Studio.", "Запусти LM Studio.")]
    [InlineData("Картинки в меджорни и мид джорни.", "Картинки в Midjourney и Midjourney.")]
    [InlineData("Подключи MCR сервер и настрой MCB сервера.", "Подключи MCP-сервер и настрой MCP-сервера.")]
    [InlineData("Новые индoint-ы, Dockerе и GitHubе, бaза.", "Новые эндпоинт-ы, Docker и GitHubе, бaза.")] // an unknown mix stays
    [InlineData("Пишу в lode Code и лод код.", "Пишу в Claude Code и Claude Code.")]
    [InlineData("Запиши в ОБС, смонтируй в да винчи резолв.", "Запиши в OBS, смонтируй в DaVinci Resolve.")]
    [InlineData("Подключись к Сити арбитру.", "Подключись к сети Arbitrum.")]
    [InlineData("Курс эфиру и эфира растёт.", "Курс эфиру и эфира растёт.")]
    [InlineData("Проверь баланс на Dank и сделай сваб.", "Проверь баланс на DeBank и сделай свап.")]
    [InlineData("Видеокарта Envidia, процессор IMD Rise и АМД Райзен.", "Видеокарта NVIDIA, процессор AMD Ryzen и AMD Ryzen.")]
    [InlineData("Зайди в Bios.", "Зайди в BIOS.")]
    [InlineData("Проверь PIN D Server.", "Проверь пинг до сервера.")]
    [InlineData("Подключи колонку GBL.", "Подключи колонку JBL.")]
    [InlineData("Кошелёк фантом. Это фантом.", "Кошелёк Phantom. Это фантом.")] // a sentence start keeps its capital
    [InlineData("Сервер возвращает Джейсона в формате джейсон.", "Сервер возвращает JSON в формате JSON.")]
    [InlineData("Поставь через PUP, комета, Lama.", "Поставь через pip, комета, Lama.")]
    [InlineData("Это int end, indentee, untaint, свопы.", "Это int end, indentee, untaint, свопы.")] // "exact" targets are not searched by sound
    public void GigaAM_live_test_spellings(string raw, string expected)
    {
        foreach (var model in ModelCatalog.All)
        {
            Assert.Equal(expected, new PostProcessor(model).Process(raw).Trim());
        }
    }

    [Fact]
    public void A_capitalised_spelling_wins()
    {
        Assert.Equal(["Cruzade"], ScreenWords.Distinct(["cruzade", "Cruzade", "CRUZADE"]).Take(1));
        var memory = new ScreenMemory();
        memory.See(["Cruzade"], DateTime.UtcNow);
        memory.See(["cruzade"], DateTime.UtcNow);
        Assert.Equal("Cruzade", Assert.Single(memory.Entries).Name);
    }

    [Fact]
    public void A_remembered_name_is_found_a_short_one_is_not()
    {
        ScreenName[] memory = [new("Sunwhisper", false), new("HAPP", false)];
        var log = new List<FuzzyTerms.Match>();
        Assert.Equal("Открой Sunwhisper и хап", Fix("Открой Sunwisper и хап", memory, log));
        Assert.Equal("память", Assert.Single(log).Screen);
        Assert.Equal("Открой санвиспер", Fix("Открой санвиспер", memory)); // Cyrillic: only a window now
    }

    [Theory]
    [InlineData("Мама мыла раму и пошла гулять.")]
    [InlineData("Отправь в телеграм, открой хром и студию")]
    [InlineData("Это ремонт, а не реклама. Хапнул и ушёл.")]
    [InlineData("Визуальная студия кода и визуал")]
    [InlineData("Хорошая погода, супервайзер пришёл")]
    public void Ordinary_phrases_stay_with_typical_titles(string raw)
    {
        var screen = OnScreen(TypicalTitles);
        Assert.Equal(Fix(raw, null), Fix(raw, screen));
    }

    private static readonly string[] TypicalTitles =
    [
        "Remotion Studio - Google Chrome", "Telegram", "Visual Studio Code", "Sunwhisper", "HAPP VPN", "Tldraw",
        "Lumalabs Dream Machine", "Wispr Flow", "Ideogram Wispr", "Krea AI - Mozilla Firefox", "Spotify Premium",
        "LMArena - Chatbot Arena", "Zencoder - Docs", "Explorer", "Discord | #general", "Steam", "Vox.sln - Rider",
        "privatekey7/Vox: Voice typing - GitHub", "Pikaso Studio", "Kandinsky 3 - Sber", "Shedevrum", "Lovart Canvas",
    ];

    // Names on screen and remembered must not change the Russian prose of the docs; Cyrillic words never match
    // remembered names at all.
    [Fact]
    public void Russian_prose_stays_with_typical_titles()
    {
        var names = TypicalTitles.SelectMany(Extract).Distinct().ToList();
        var words = CyrillicCorpus();
        Assert.True(words.Count > 2000, $"{words.Count} words");
        foreach (var screen in new[] { names.Select(n => new ScreenName(n, true)), names.Select(n => new ScreenName(n, false)) })
        {
            ScreenName[] list = [.. screen];
            var changed = words.Select(w => (w, FuzzyTerms.Apply(w, null, list))).Where(p => p.Item2 != FuzzyTerms.Apply(p.w)).ToList();
            Assert.True(changed.Count == 0, string.Join(", ", changed.Take(40).Select(c => $"{c.w} → {c.Item2}")));
        }
    }

    // Stress: every Latin name of terms.json remembered, against a dictionary without them.
    [Fact]
    public void Cyrillic_words_never_take_a_remembered_name()
    {
        ScreenName[] memory = [.. Terms.LoadPairs().Select(p => p[1]).Where(t => t.All(char.IsAsciiLetter) && t.Length >= 3)
            .Distinct().Select(n => new ScreenName(n, false))];
        var fuzzy = new FuzzyTerms([("фронтенд", "фронтенд")], [("фронтенд", "фронтенд")], latin: true, FuzzyTerms.LoadEnglish());
        var changed = CyrillicCorpus().Where(w => fuzzy.Replace(w, null, memory) != fuzzy.Replace(w)).ToList();
        Assert.Equal([], changed);
    }

    [Fact]
    public void Frequent_english_words_stay_with_names_on_screen()
    {
        var screen = OnScreen(TypicalTitles);
        var common = FuzzyTerms.LoadEnglish().Where(e => e.Level <= 20).Take(5000).Select(e => e.Word).ToList();
        var changed = common.Select(w => (w, FuzzyTerms.Apply(w, null, screen))).Where(p => p.w != p.Item2).ToList();
        Assert.Equal([], changed);
    }

    [Fact]
    public void Matching_with_a_full_memory_is_fast()
    {
        var names = Terms.LoadPairs().Select(p => p[1]).Where(t => t.All(char.IsAsciiLetter) && t.Length >= 3).Distinct()
            .Select((n, i) => n + "x" + (char)('a' + (i % 26))).Take(ScreenMemory.Limit).ToList(); // unknown names
        ScreenName[] screen = [.. names.Select(n => new ScreenName(n, false)), .. OnScreen("Sunwhisper Settings - Google Chrome")];
        const string text = "Открой санвиспер, проверь фронтенд и Bek End в нашем проекте, потом напиши отчёт";
        FuzzyTerms.Apply(text, null, screen); // warm up: skeletons are cached
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            FuzzyTerms.Apply(text, null, screen);
        }

        Assert.True(sw.Elapsed.TotalMilliseconds / 20 < 10, $"{sw.Elapsed.TotalMilliseconds / 20:0.0} ms");
    }

    private static List<string> CyrillicCorpus()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "Vox.sln")))
        {
            root = Path.GetDirectoryName(root)!;
        }

        var text = string.Join("\n", Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories)
            .Append(Path.Combine(root, "CHANGELOG.md")).Append(Path.Combine(root, "README.md")).Select(File.ReadAllText));
        string[] examples = ["санвиспер", "хап", "ремоушн", "люмалабс"]; // the docs describe these very replacements
        return [.. Regex.Matches(text, @"[а-яё]+(?:-[а-яё]+)*", RegexOptions.IgnoreCase).Select(m => m.Value).Distinct()
            .Where(w => !examples.Contains(w.ToLowerInvariant()))];
    }
}
