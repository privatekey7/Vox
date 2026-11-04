using Vox.Core.Text;

namespace Vox.Core.Tests;

public class CurrencyTests
{
    [Theory]
    [InlineData("Переведи 500 ₽ на карту.", "Переведи 500 рублей на карту.")]
    [InlineData("1 ₽", "1 рубль")]
    [InlineData("21 ₽", "21 рубль")]
    [InlineData("2 ₽", "2 рубля")]
    [InlineData("34 ₽", "34 рубля")]
    [InlineData("11 ₽", "11 рублей")]
    [InlineData("12 ₽", "12 рублей")]
    [InlineData("111 ₽", "111 рублей")]
    [InlineData("150 000 ₽ до 25-го числа.", "150 000 рублей до 25-го числа.")]
    [InlineData("1 001 ₽", "1 001 рубль")]
    [InlineData("1,5 ₽", "1,5 рубля")]
    [InlineData("500₽", "500 рублей")]
    [InlineData("Стоит 99 ₽, а не 100 ₽.", "Стоит 99 рублей, а не 100 рублей.")]
    [InlineData("цена в ₽", "цена в рублях")]
    public void SpellOut_replaces_ruble_sign_with_agreeing_word(string text, string expected) =>
        Assert.Equal(expected, Currency.SpellOut(text));

    [Theory]
    [InlineData("")]
    [InlineData("без валюты")]
    [InlineData("500 рублей")]
    [InlineData("$5")]
    public void SpellOut_leaves_other_text_unchanged(string text) => Assert.Equal(text, Currency.SpellOut(text));

    // 01.11, owner: GigaAM writes "за 22 доллара" as "за$22" (5 times in the history, always glued to the word before).
    [Theory]
    [InlineData("То есть за$22, который.", "То есть за $22, который.")]
    [InlineData("Сожрал около$99.", "Сожрал около $99.")]
    [InlineData("Подписка€20 в месяц.", "Подписка €20 в месяц.")]
    [InlineData("Стоит $5.", "Стоит $5.")]
    [InlineData("$5", "$5")]
    [InlineData("Выведи echo$PATH.", "Выведи echo$PATH.")] // not a sum
    public void A_currency_sign_is_split_off_the_word_before(string text, string expected) =>
        Assert.Equal(expected, Currency.Unglue(text));

    [Fact]
    public void Gigaam_dictation_gets_the_space() =>
        Assert.Equal(" То есть за $22, который.", new PostProcessor(Vox.Core.Models.ModelCatalog.GigaAm).Process("То есть за$22, который."));
}

public class FillersTests
{
    [Theory]
    [InlineData("Э-э, запусти сборку", "Запусти сборку")]
    [InlineData("ммм давай так", "давай так")]
    [InlineData("Мы, э, решили", "Мы решили")]
    [InlineData("Mmm.", "")]
    [InlineData("ну вот этот файл", "ну вот этот файл")]
    [InlineData("um so hello world", "so hello world")]
    // Regression (owner's log): longer hesitations were left behind or broke the text.
    [InlineData("Надо сказать, э-э-э, вот так", "Надо сказать вот так")]
    [InlineData("Тестовые Ааа, тестируйте", "Тестовые тестируйте")]
    [InlineData("м-м-м, хммм, да", "да")]
    [InlineData("Мама мыла раму", "Мама мыла раму")]
    [InlineData("А мы ам", "А мы ам")]
    // Regression: a capital was lost when a filler started the sentence.
    [InlineData("Да, у меня всегда это стоит. А-а, ещё не пробовал", "Да, у меня всегда это стоит. Ещё не пробовал")]
    // Owner: a drawn-out "а-а" mid-sentence comes out as a capital "А"; it is lowered, not removed.
    [InlineData("как я в данном случае пользователь А говорит на русском", "как я в данном случае пользователь а говорит на русском")]
    [InlineData("Он сказал А, и ушёл", "Он сказал а, и ушёл")]
    // Regression (owner's corpus): the conjunction "а" and a letter-variant were removed.
    [InlineData("старая версия была на Piton А новая на C#.", "старая версия была на Piton а новая на C#.")]
    [InlineData("Да, выбираем А.", "Да, выбираем А.")]
    [InlineData("Да, выбираем А", "Да, выбираем А")]
    [InlineData("про часть А и Б.", "про часть А и Б.")]
    [InlineData("часть А или Б", "часть А или Б")]
    [InlineData("пункт А говорит", "пункт А говорит")]
    [InlineData("вариант А лучше", "вариант А лучше")]
    [InlineData("витамин А полезен", "витамин А полезен")]
    [InlineData("в классе А учатся", "в классе А учатся")]
    [InlineData("корпус 3 А стоит", "корпус 3 А стоит")]
    [InlineData("Ну. А потом", "Ну. А потом")]
    [InlineData("сделал а потом ушёл", "сделал а потом ушёл")]
    [InlineData("план А и план Б", "план А и план Б")]
    // Owner's log: a hesitation between commas leaves no comma behind.
    [InlineData("если мы, а-а, уменьшим в два раза", "если мы уменьшим в два раза")]
    [InlineData("Ну, э-э, давай", "Ну давай")]
    [InlineData("если мы, э-э.", "если мы.")]
    [InlineData("Э-э, запусти, ммм, тесты", "Запусти тесты")]
    // Owner's log: an introductory word after the hesitation keeps its comma; no "...." after a removed filler.
    [InlineData("Если просто добавлять слова в словарь, э-э-э, ну, не факт.", "Если просто добавлять слова в словарь, ну, не факт.")]
    [InlineData("Мы, э-э, в общем, решили", "Мы, в общем, решили")]
    [InlineData("Угу. А-а-а... Как бы", "Угу. Как бы")]
    // Owner: a comma the phrase needs anyway stays (two verbs, an opening "Так", a conjunction next).
    [InlineData("я открыл, а-а, закрыл окно", "я открыл, закрыл окно")]
    [InlineData("Так, э-э, добавь тест", "Так, добавь тест")]
    [InlineData("Мне нравится, э-э, но дорого", "Мне нравится, но дорого")]
    [InlineData("Так, смотри, а-а-а, вышла новая версия", "Так, смотри, вышла новая версия")]
    [InlineData("Возьмём логи, э-э, то есть свежие записи", "Возьмём логи, то есть свежие записи")]
    // Owner's history (04.11): the comma stays before a new part of the phrase and after an introductory word.
    [InlineData("Так, напомню, а-а, у нас шорт", "Так, напомню, у нас шорт")]
    [InlineData("нужно снимать, во-первых, а-а, самому установить.", "нужно снимать, во-первых, самому установить.")]
    [InlineData("А во-вторых, э-э, нужно", "А во-вторых, нужно")]
    [InlineData("Да, всё отлично, а-а, можем.", "Да, всё отлично, можем.")]
    [InlineData("Чтобы был максимальный контекст, в общем, э-э, скорость", "Чтобы был максимальный контекст, в общем, скорость")]
    [InlineData("давай соберём ролик, а-а, я посмотрю.", "давай соберём ролик, я посмотрю.")]
    [InlineData("Если нет, э-э, я скажу.", "Если нет, я скажу.")]
    [InlineData("я зарегался, а-а, а где ввести промокод?", "я зарегался, а где ввести промокод?")]
    [InlineData("Теперь давай подумаем, а-а, как эту песенку", "Теперь давай подумаем, как эту песенку")]
    [InlineData("один из самых красивых, а-а, может, какой-то там", "один из самых красивых, может, какой-то там")]
    [InlineData("ты всю информацию, а-а, допустим, получил", "ты всю информацию, допустим, получил")]
    // ...and goes where the hesitation alone made it.
    [InlineData("я замечаю, что у нас, э-э, в верхнем окошке", "я замечаю, что у нас в верхнем окошке")]
    [InlineData("ты можешь, а-а, использовать Кодекс", "ты можешь использовать Кодекс")]
    [InlineData("я планирую, а-а, хотя бы 200 000", "я планирую хотя бы 200 000")]
    [InlineData("что, а-а, мы сделали", "что мы сделали")]
    [InlineData("А в итоге, э-э, я нажал «продлить».", "А в итоге я нажал «продлить».")]
    [InlineData("То есть, э-э, соберём самые популярные", "То есть соберём самые популярные")]
    [InlineData("То есть, э-э, я прошу", "То есть я прошу")]
    [InlineData("Слушай, ну на самом деле, э-э, как бы не хотелось", "Слушай, ну на самом деле как бы не хотелось")]
    [InlineData("Пока делаем слияние, э-э, я дальше тестирую.", "Пока делаем слияние, я дальше тестирую.")]
    [InlineData("я не знаю, э-э, откуда он взялся", "я не знаю, откуда он взялся")]
    [InlineData("Я сделал, э-э, как бы всё", "Я сделал как бы всё")]
    [InlineData("Я думал и, а-а, я решил", "Я думал и я решил")]
    [InlineData("в каком-то месте, э-э... Конфиденциально", "в каком-то месте... Конфиденциально")]
    // Owner (02.11, 04.11): the "..." of a hesitation the owner went on from is the hesitation's; he deleted it by hand.
    [InlineData("Так, и давай добавим э-э-э...", "Так, и давай добавим")]
    [InlineData("Я замечал, что есть такой момент, э-э-э...", "Я замечал, что есть такой момент")]
    // Owner's check 04.11: the model wrote the hesitation as a sentence of its own, "добавим.." was left.
    [InlineData("Так и добавим. Э-э-э.", "Так и добавим.")]
    [InlineData("Так и добавим. Э-э-э?", "Так и добавим.")]
    [InlineData("Что?! Э-э, ладно", "Что?! Ладно")]
    public void Remove_strips_hesitation_sounds_only(string text, string expected) =>
        Assert.Equal(expected, Fillers.Remove(text));
}

public class TermsTests
{
    [Theory]
    [InlineData("Запушь в гитхаб.", "Запушь в GitHub.")]
    // Regression (owner's log): brand in lower case, and the model's own misspellings.
    [InlineData("Привет, Вокс.", "Привет, Vox.")]
    [InlineData("Залей на гитхап.", "Залей на GitHub.")]
    [InlineData("Пишу на Pithon.", "Пишу на Python.")]
    [InlineData("Ругается Smart Screen.", "Ругается SmartScreen.")]
    [InlineData("Добавь юзстейт для счётчика", "Добавь useState для счётчика")]
    // Owner's log: the model's own "+" tail is swallowed, not doubled.
    [InlineData("Открой Нотпад+.", "Открой Notepad++.")]
    [InlineData("Открой Нот пат++.", "Открой Notepad++.")]
    [InlineData("открой нотпад", "открой Notepad++")]
    [InlineData("в нот паде", "в Notepad++")]
    [InlineData("Правлю в нотпаде.", "Правлю в Notepad++.")]
    [InlineData("обычный текст без терминов", "обычный текст без терминов")]
    public void Replace_maps_phonetic_terms_to_latin(string text, string expected) =>
        Assert.Equal(expected, Terms.Replace(text));

    // Owner's policy: settled borrowings stay Cyrillic, only the spelling is fixed — with case forms.
    [Theory]
    [InlineData("Промт готов.", "Промпт готов.")]
    [InlineData("Напиши промты для агента.", "Напиши промпты для агента.")]
    [InlineData("Ошибка в промте.", "Ошибка в промпте.")]
    public void Cyrillic_spelling_fixes_keep_case_and_ending(string text, string expected) =>
        Assert.Equal(expected, Terms.Replace(text));

    // Regression: a hyphen is part of the word — "пиф-паф" became "пиф-path", "Жан-Клод" "Жан-Claude".
    [Theory]
    [InlineData("Пиф-паф, ой-ой-ой.")]
    [InlineData("Смотрел фильм с Жан-Клодом ван Даммом, Жан-Клод хорош.")]
    public void Part_of_a_hyphenated_word_is_not_a_term(string text) =>
        Assert.Equal(text, Terms.Replace(text));
}

public class SpacingTests
{
    [Theory]
    [InlineData("Привет.Что ты умеешь?Как дела?", "Привет. Что ты умеешь? Как дела?")]
    [InlineData("т.е. так", "т.е. так")]
    [InlineData("версия 3.14 и v2.0", "версия 3.14 и v2.0")]
    [InlineData("раз,два", "раз, два")]
    // Regression (owner's log): a domain became "github. Com".
    [InlineData("открой github.com и vox.log", "открой github.com и vox.log")]
    public void SentenceSpacing_splits_glued_sentences_only(string text, string expected) =>
        Assert.Equal(expected, SentenceSpacing.Normalize(text));

    [Fact]
    public void Leading_space_is_added_once() => Assert.Equal(" Привет", Spacing.WithLeadingSpace(Spacing.WithLeadingSpace("Привет")));

    [Fact]
    public void Separator_is_split_off_for_typing() => Assert.Equal((" ", "Привет"), Spacing.SplitLeadingSeparator(" Привет"));
}

public class UserCorrectionsTests
{
    private static readonly Dictionary<string, string> Corrections = new() { ["кубернетис"] = "Kubernetes", ["вокс"] = "vox" };

    [Theory]
    [InlineData("настрой кубернетис, пожалуйста", "настрой Kubernetes, пожалуйста")]
    [InlineData("Вокс работает", "Vox работает")]
    [InlineData("«кубернетис»", "«Kubernetes»")]
    [InlineData("ничего не меняется", "ничего не меняется")]
    public void Apply_replaces_whole_words_and_keeps_punctuation_and_capital(string text, string expected) =>
        Assert.Equal(expected, UserCorrections.Apply(text, Corrections));

    // Security audit M5: a pair already in the database (from someone else's file) must not paste a command.
    [Theory]
    [InlineData("кошелек", "0xabc\r\ncalc")]
    [InlineData("биржа", "bin\u202Eance")]
    [InlineData("гитхаб", "Git\u200BHub")]
    [InlineData("сайт", "evil\u2028site")]
    [InlineData("мой кошелек", "0xabc\ncalc")]
    public void A_target_with_a_line_break_or_an_invisible_character_is_not_pasted(string wrong, string correct) =>
        Assert.Equal($"вот {wrong} тут", UserCorrections.Apply($"вот {wrong} тут", new Dictionary<string, string> { [wrong] = correct }));
}

public class SafeTextTests
{
    [Theory]
    [InlineData("Kubernetes")]
    [InlineData("Кирилл Петров")]
    [InlineData("C++ / .NET, «Vox»!")]
    [InlineData("ударе\u0301ние")] // a combining accent is a letter's mark, not hidden
    public void Ordinary_targets_pass(string text) => Assert.True(SafeText.IsPairPart(text));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\nb")]
    [InlineData("a\tb")]
    [InlineData("a\u0000b")]
    [InlineData("a\u202Eb")]  // right-to-left override
    [InlineData("a\u2066b")]  // left-to-right isolate
    [InlineData("a\u200Db")]  // zero-width joiner
    [InlineData("a\uFEFFb")]  // byte order mark
    [InlineData("a\u2029b")]  // paragraph separator
    public void A_target_with_hidden_characters_is_refused(string? text) => Assert.False(SafeText.IsPairPart(text));

    [Fact]
    public void A_target_longer_than_a_phrase_or_with_a_lone_surrogate_is_refused()
    {
        Assert.False(SafeText.IsPairPart("a" + '\uD800' + "b"));
        Assert.True(SafeText.IsPairPart("ok \U0001F600"));
        Assert.True(SafeText.IsPairPart(new string('a', SafeText.MaxPairLength)));
        Assert.False(SafeText.IsPairPart(new string('a', SafeText.MaxPairLength + 1)));
    }
}

/// <summary>Owner: GigaAM's long dash reads as machine text; people type a hyphen.</summary>
public class DashesTests
{
    [Theory]
    [InlineData("словарь на полторы тысячи терминов — это маловато", "словарь на полторы тысячи терминов - это маловато")]
    [InlineData("займёт 1—2 минуты, страницы 10 – 12", "займёт 1-2 минуты, страницы 10-12")]
    [InlineData("Москва—Петербург и что–то", "Москва-Петербург и что-то")]
    [InlineData("без тире", "без тире")]
    public void Long_dashes_become_hyphens(string text, string expected) => Assert.Equal(expected, Dashes.Normalize(text));

    [Theory]
    [InlineData("Э-э — ну да.", " Ну да.")] // a filler before the dash: no stray "-" at the start
    [InlineData("Это — тест.", " Это - тест.")]
    public void Pipeline_writes_hyphens(string raw, string expected) =>
        Assert.Equal(expected, new PostProcessor(Vox.Core.Models.ModelCatalog.GigaAm).Process(raw));
}

/// <summary>Owner's log: Parakeet left "&lt;unk&gt;" in the text.</summary>
public class UnknownTokenTests
{
    [Theory]
    [InlineData("команду <unk>Поговорим", "команду Поговорим")]
    [InlineData("<unk> привет <unk>", "привет")]
    // Regression (owner's corpus): Parakeet has no "ё" token and writes <unk> instead.
    [InlineData("Ты ч<unk>, серьёзно?", "Ты чё, серьёзно?")]
    [InlineData("сломался бо<unk>к.", "сломался боёк.")]
    [InlineData("<unk>Поговорим<unk>.", "Поговорим.")]
    [InlineData("вс<unk> готово, ещ<unk> раз", "всё готово, ещё раз")]
    public void Unk_is_removed(string text, string expected) => Assert.Equal(expected, UnknownToken.Remove(text));

    [Fact]
    public void Only_unk_gives_nothing() =>
        Assert.Equal("", new PostProcessor(Vox.Core.Models.ModelCatalog.Parakeet).Process("<unk>"));
}

/// <summary>Owner's corpus: "точка" before a file extension.</summary>
public class FileExtensionsTests
{
    [Theory]
    [InlineData("Сохрани в Точка MP4.", " Сохрани в .mp4.")]
    [InlineData("Открой index точка html", " Открой index.html")]
    [InlineData("Скинь файл точка пдф", " Скинь файл .pdf")]
    [InlineData("Поставь точка и запятая", " Поставь точка и запятая")]
    public void Spoken_dot_before_an_extension(string raw, string expected) =>
        Assert.Equal(expected, new PostProcessor(Vox.Core.Models.ModelCatalog.GigaAm).Process(raw));
}

// 01.11, owner: a long question often ends with a full stop. The model puts "?" by the question words at the start
// (94% of such sentences up to 6 words, 60% from 13 words in the history), and loses them over a long sentence.
public class QuestionMarksTests
{
    [Theory]
    [InlineData("Есть ли доступ к этой видеокарте.", "Есть ли доступ к этой видеокарте?")]
    [InlineData("Ну что там у нас долго так.", "Ну что там у нас долго так?")]
    [InlineData("Хорошо, как мне теперь правильно с теми средами в приложении на компьютере поработать так, чтобы всё у меня это было теперь синхронизировано.",
        "Хорошо, как мне теперь правильно с теми средами в приложении на компьютере поработать так, чтобы всё у меня это было теперь синхронизировано?")]
    [InlineData("Почему не работает. Проверь лог.", "Почему не работает? Проверь лог.")]
    [InlineData("Проверь лог. Слушай, а сколько это стоит.", "Проверь лог. Слушай, а сколько это стоит?")]
    [InlineData("Можно ли так сделать.", "Можно ли так сделать?")]
    [InlineData("Зачем нам второй сервер.", "Зачем нам второй сервер?")]
    [InlineData("Разве это сложно.", "Разве это сложно?")]
    [InlineData("Где тут настройки.", "Где тут настройки?")]
    [InlineData("Куда мне это сохранить.", "Куда мне это сохранить?")]
    [InlineData("Так, что думаешь про этот вариант.", "Так, что думаешь про этот вариант?")]
    [InlineData("Как мне открыть github.com.", "Как мне открыть github.com?")]
    [InlineData("Это ты сделал вчера, не так ли.", "Это ты сделал вчера, не так ли?")]
    [InlineData("Ты же это сделал, так ведь.", "Ты же это сделал, так ведь?")]
    public void A_sentence_that_starts_as_a_question_ends_with_a_question_mark(string text, string expected) =>
        Assert.Equal(expected, QuestionMarks.Fix(text));

    [Theory]
    [InlineData("Так как это связанные видео, я думаю, загрузку сделаем прямо сейчас.")]
    [InlineData("Почему-то замена не сработала.")]
    [InlineData("Сколько угодно агентов привлекай.")]
    [InlineData("Посмотри, нет ли блокировки.")] // an indirect question
    [InlineData("Скажи, как мне это настроить.")]
    [InlineData("Вот как это работает.")]
    [InlineData("Как будто всё хорошо.")]
    [InlineData("Что-то не так.")]
    [InlineData("Ага, Python правильно звучит, Python отдельно, правильно.")] // owner's history: agreeing, not asking
    [InlineData("Да, так ведь.")]
    [InlineData("Почему не работает...")]
    [InlineData("Почему не работает? Проверь лог.")]
    [InlineData("Почему не работает")] // no full stop to change: the end of a cut dictation
    public void Other_sentences_stay(string text) => Assert.Equal(text, QuestionMarks.Fix(text));

    [Fact]
    public void Gigaam_dictation_gets_the_question_mark() =>
        Assert.Equal(" Есть ли доступ к этой видеокарте?", new PostProcessor(Vox.Core.Models.ModelCatalog.GigaAm).Process("Есть ли доступ к этой видеокарте."));
}
