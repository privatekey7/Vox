using Vox.Core.Text;

namespace Vox.Core.Tests;

public class CorrectionDetectorTests
{
    [Fact]
    public void Replacing_a_misheard_word_with_its_spelling_is_a_correction()
    {
        var result = CorrectionDetector.Detect(
            before: "Задача: Нужно настроить кубернетис на сервере.",
            after: "Задача: Нужно настроить Kubernetes на сервере.",
            inserted: "Нужно настроить кубернетис на сервере.");

        Assert.Equal(("кубернетис", "Kubernetes"), result);
    }

    [Fact]
    public void Same_script_spelling_fix_is_a_correction() =>
        Assert.Equal(("дитхаб", "гитхаб"), CorrectionDetector.Detect("Запушь в дитхаб.", "Запушь в гитхаб.", "Запушь в дитхаб."));

    [Fact]
    public void Punctuation_around_the_word_is_ignored() =>
        Assert.Equal(("вокс", "Vox"), CorrectionDetector.Detect("Открой «вокс», пожалуйста", "Открой «Vox», пожалуйста", "Открой «вокс», пожалуйста"));

    [Fact]
    public void Rewriting_meaning_is_not_a_correction() =>
        Assert.Null(CorrectionDetector.Detect("У меня есть кот.", "У меня есть пёс.", "У меня есть кот."));

    [Fact]
    public void Several_changed_words_are_not_a_correction() =>
        Assert.Null(CorrectionDetector.Detect("Настрой кубернетис и докер.", "Настрой Kubernetes и Docker.", "Настрой кубернетис и докер."));

    [Fact]
    public void Edits_outside_the_dictation_are_ignored() =>
        Assert.Equal(("кубернетис", "Kubernetes"), CorrectionDetector.Detect(
            "Привет. Настрой кубернетис.", "Здравствуйте. Настрой Kubernetes.", "Настрой кубернетис."));

    [Fact]
    public void Untouched_text_is_not_a_correction() =>
        Assert.Null(CorrectionDetector.Detect("Настрой кубернетис.", "Настрой кубернетис. Дальше текст", "Настрой кубернетис."));

    [Fact]
    public void Missing_dictation_gives_nothing() =>
        Assert.Null(CorrectionDetector.Detect("совсем другой текст", "совсем другой текст!", "Настрой кубернетис."));

    [Fact]
    public void Case_only_change_is_not_learned() =>
        Assert.Null(CorrectionDetector.Detect("Привет вокс.", "Привет Вокс.", "Привет вокс."));

    [Fact]
    public void Deleted_word_is_not_a_correction() =>
        Assert.Null(CorrectionDetector.Detect("Ну вот настрой сервер.", "Ну настрой сервер.", "Ну вот настрой сервер."));

    [Fact]
    public void Works_inside_a_long_document()
    {
        var filler = string.Concat(Enumerable.Repeat("Обычный абзац текста без изменений. ", 300));
        var result = CorrectionDetector.Detect(
            filler + "Настрой кубернетис сегодня. " + filler,
            filler + "Настрой Kubernetes сегодня. " + filler,
            "Настрой кубернетис сегодня.");
        Assert.Equal(("кубернетис", "Kubernetes"), result);
    }

    [Theory]
    [InlineData("кубернетис", "Kubernetes", true)]
    [InlineData("джаваскрипт", "JavaScript", true)]
    [InlineData("кот", "пёс", false)]
    [InlineData("сервер", "клиент", false)]
    [InlineData("апгрейд", "Upgrade", true)] // letters 0.43 ("apgreid"), the same sound skeleton
    [InlineData("вистер", "Whisper", true)]
    [InlineData("кошка", "cat", false)] // a translation, not a spelling: the skeleton is too far
    [InlineData("собака", "dog", false)]
    public void Similarity_bridges_cyrillic_and_latin(string a, string b, bool similar) =>
        Assert.Equal(similar, CorrectionDetector.LookAlike(a, b));

    // Regression (owner, 23.10): "Апгрейд." → "Upgrade." was not learned, "the words are not alike (0.43)".
    [Fact]
    public void Cross_script_fix_with_a_different_spelling_is_learned_by_sound() =>
        Assert.Equal(("Апгрейд", "Upgrade"), CorrectionDetector.Detect("Это был Апгрейд.", "Это был Upgrade.", "Это был Апгрейд."));

    // Ctrl+S (owner, 23.10): the user confirms the fix, so words that do not look alike are learned too.
    [Fact]
    public void Confirmed_fix_is_learned_even_when_the_words_are_not_alike()
    {
        Assert.Null(CorrectionDetector.Detect("Тут треде.", "Тут tray.", "Тут треде."));
        Assert.Equal(("треде", "tray"), CorrectionDetector.Detect("Тут треде.", "Тут tray.", "Тут треде.", confirmed: true));
    }

    [Fact]
    public void Confirmation_keeps_the_other_guards()
    {
        Assert.Null(CorrectionDetector.Detect("Это тёмные тут.", "Это тёмной тут.", "Это тёмные тут.", confirmed: true));
        Assert.Null(CorrectionDetector.Detect("Настрой кубернетис и докер.", "Настрой Kubernetes и Docker.", "Настрой кубернетис и докер.", confirmed: true));
        Assert.Null(CorrectionDetector.Detect("Привет. Тут треде.", "Пока. Тут треде.", "Тут треде.", confirmed: true));
    }

    // Regression: the owner's own grammar fixes were learned as dictionary entries
    // ("тёмные → тёмной", "блоге → логе") and then rewrote every later dictation.
    [Theory]
    [InlineData("тёмные", "тёмной")]
    [InlineData("тёмный", "тёмная")]
    [InlineData("сделал", "сделала")]
    [InlineData("блоге", "логе")]
    public void Ending_or_edge_letter_fixes_are_not_learned(string wrong, string correct) =>
        Assert.Null(CorrectionDetector.Detect($"Это {wrong} тут.", $"Это {correct} тут.", $"Это {wrong} тут."));

    [Theory]
    [InlineData("гитхаб", "GitHub")]
    [InlineData("керил", "Кирилл")]
    public void Misheard_words_are_still_learned(string wrong, string correct) =>
        Assert.Equal((wrong, correct), CorrectionDetector.Detect($"Это {wrong} тут.", $"Это {correct} тут.", $"Это {wrong} тут."));

    // Owner's Windows test (22.10): Vox wrote its own target "Fluxar" and the user fixed it to "Fluxor".
    [Theory]
    [InlineData("Новый Fluxar.", "Новый Fluxor.", "Fluxar", "Fluxor")]
    [InlineData("Открой GitHbu.", "Открой GitHub.", "GitHbu", "GitHub")]
    public void A_typo_fix_in_a_dictionary_target_is_a_correction(string before, string after, string wrong, string correct)
    {
        Assert.Equal((wrong, correct), CorrectionDetector.Detect(before, after, before, w => w == wrong));
        Assert.Null(CorrectionDetector.Detect(before, after, before)); // an ordinary word: a typo or grammar fix
        Assert.Equal($"a grammar or typo edit of an ordinary word; 1 change: \"{wrong}.\" → \"{correct}.\"",
            CorrectionDetector.Describe(before, after, before));
    }

    // Owner's Windows test: "треде" is a case form of "тред"; learning "треде → tray" would break "в треде".
    [Fact]
    public void Unlike_words_are_not_learned_and_the_log_says_why() =>
        Assert.Equal("the words are not alike (0.40); 1 change: \"треде\" → \"tray\"",
            CorrectionDetector.Describe("Свернуть в треде и выйти.", "Свернуть в tray и выйти.", "Свернуть в треде и выйти."));

    // Regression (owner, 15.11): "фронтенд-дизайн" fixed to "Frontend Design" was learned as "Frontend" alone:
    // the second word was taken for text typed on. A hyphenated word may become as many words as it has parts.
    [Theory]
    [InlineData("Нужен фронтенд-дизайн.", "Нужен Frontend Design.")] // the last word of the dictation
    [InlineData("Нужен фронтенд-дизайн сегодня.", "Нужен Frontend Design сегодня.")] // in the middle
    [InlineData("фронтенд-дизайн готов.", "Frontend Design готов.")] // the first word
    [InlineData("фронтенд-дизайн", "Frontend Design")] // the whole dictation
    public void A_hyphenated_word_fixed_into_two_words_is_learned_whole(string before, string after) =>
        Assert.Equal(("фронтенд-дизайн", "Frontend Design"), CorrectionDetector.Detect(before, after, before));

    [Fact]
    public void A_word_of_three_parts_fixed_into_three_words_is_learned_whole() =>
        Assert.Equal(("фулл-стек-девелопер", "Full Stack Developer"),
            CorrectionDetector.Detect("Нужен фулл-стек-девелопер.", "Нужен Full Stack Developer.", "Нужен фулл-стек-девелопер."));

    [Theory]
    [InlineData("Нужен фронтенд-дизайн.", "Нужен Frontend Design. Дальше текст", false)] // typed on after the fix
    [InlineData("Нужен фронтенд-дизайн", "Нужен Frontend Design и ещё", false)] // the same without a full stop
    [InlineData("фронтенд-дизайн готов.", "Сегодня Frontend Design готов.", false)] // typed before the fix
    [InlineData("Нужен фронтенд-дизайн.", "Нужен Frontend Design. Дальше текст", true)] // Ctrl+S
    public void A_split_fix_does_not_take_the_text_typed_around_it(string before, string after, bool confirmed) =>
        Assert.Equal(("фронтенд-дизайн", "Frontend Design"), CorrectionDetector.Detect(before, after, before, confirmed: confirmed));

    // The guard that must stay: the second word is part of the fix only when it sounds like the second part.
    [Theory]
    [InlineData("Нужен фронтенд-дизайн.", "Нужен Frontend. Дальше текст", false)]
    [InlineData("Нужен фронтенд-дизайн", "Нужен Frontend дальше", false)]
    [InlineData("Нужен фронтенд-дизайн.", "Нужен Frontend. Дальше текст", true)] // Ctrl+S keeps the guard
    [InlineData("Нужен фронтенд-дизайн.", "Нужен Frontend. Дизайн потом", false)] // a new sentence, though the word is the same
    [InlineData("Нужен фронтенд-дизайн.", "Нужен Frontend, дизайн потом", false)]
    [InlineData("фронтенд-дизайн готов.", "Сегодня Frontend готов.", false)] // typed before the fix
    public void Text_typed_next_to_a_fixed_hyphenated_word_is_not_part_of_the_pair(string before, string after, bool confirmed) =>
        Assert.Equal(("фронтенд-дизайн", "Frontend"), CorrectionDetector.Detect(before, after, before, confirmed: confirmed));

    [Theory]
    [InlineData("Настрой кубернетис", "Настрой Kubernetes сервер")] // typed on after the fix
    [InlineData("Настрой кубернетис.", "Настрой Kubernetes. Дальше текст")]
    [InlineData("кубернетис готов.", "Сегодня Kubernetes готов.")] // typed before the fix
    public void A_word_without_a_hyphen_never_takes_the_text_typed_around_it(string before, string after) =>
        Assert.Equal(("кубернетис", "Kubernetes"), CorrectionDetector.Detect(before, after, before));

    [Theory]
    [InlineData("Нужен фронтенд-дизайн сегодня.", "Нужен Frontend сайта сегодня.")] // the second word is not the second part
    [InlineData("Нужен фулл-стек-веб-девелопер.", "Нужен Full Stack Web Developer.")] // four parts: over the limit
    [InlineData("Нужен фулл-стек-девелопер.", "Нужен Full Stack.")] // fewer words than parts
    [InlineData("Нужен фулл-стек-девелопер сегодня.", "Нужен Full Stack сегодня.")]
    [InlineData("фулл-стек-девелопер готов.", "Full Stack готов.")]
    [InlineData("Открой веб-сайт билдер сегодня.", "Открой Web Sitebuilder сегодня.")] // two words into two: not one word split
    public void A_split_that_does_not_match_the_parts_is_not_learned(string before, string after) =>
        Assert.Null(CorrectionDetector.Detect(before, after, before));

    // "Full" stood in the field before the dictation: a word outside the changed place is never part of the pair.
    [Fact]
    public void A_split_does_not_reach_for_a_word_outside_the_change() =>
        Assert.Null(CorrectionDetector.Detect("Full фулл-стек-девелопер готов.", "Full Stack Developer готов.", "фулл-стек-девелопер готов."));

    // Nor is the untouched word after the change, though it sounds like the second part.
    [Fact]
    public void A_split_does_not_reach_for_the_word_after_the_change() =>
        Assert.Equal(("фронтенд-дизайн", "Frontend"),
            CorrectionDetector.Detect("Нужен фронтенд-дизайн дизайна.", "Нужен Frontend дизайна.", "Нужен фронтенд-дизайн дизайна."));

    // The owner's case as it was made: fixed and confirmed with Ctrl+S.
    [Fact]
    public void A_split_fix_confirmed_with_ctrl_s_is_learned_whole() =>
        Assert.Equal(("фронтенд-дизайн", "Frontend Design"),
            CorrectionDetector.Detect("Нужен фронтенд-дизайн.", "Нужен Frontend Design.", "Нужен фронтенд-дизайн.", confirmed: true));

    // A hyphen turned into a space is punctuation, like "по-моему" → "по моему": the user's own wording.
    [Theory]
    [InlineData("Нужен фронтенд-дизайн.", "Нужен фронтенд дизайн.")]
    [InlineData("Нужен фронтенд-дизайн сегодня.", "Нужен фронтенд дизайн сегодня.")]
    [InlineData("Нужен фронтенд-дизайн сегодня.", "Нужен Фронтенд Дизайн сегодня.")]
    public void A_hyphen_replaced_by_a_space_is_not_learned(string before, string after)
    {
        Assert.Null(CorrectionDetector.Detect(before, after, before));
        Assert.Null(CorrectionDetector.Detect(before, after, before, confirmed: true));
    }

    [Fact]
    public void Only_the_changed_part_is_the_pair_when_the_hyphen_became_a_space() =>
        Assert.Equal(("ЛМ", "LLM"),
            CorrectionDetector.Detect("Нужна ЛМ-полировка текста.", "Нужна LLM полировка текста.", "Нужна ЛМ-полировка текста."));

    // Security audit L3: what the user typed over the dictation (a card number, an address) stays out of the log.
    [Fact]
    public void Without_texts_the_log_gets_the_reason_and_the_count_but_no_words()
    {
        const string before = "Карта 1234 и адрес Ленина.";
        const string after = "Карта 4276 5500 и адрес Мира.";

        var line = CorrectionDetector.DescribeWithoutText(before, after, before);

        Assert.Equal("several places changed; 2 changes", line);
        Assert.Equal("no change inside the dictation", CorrectionDetector.DescribeWithoutText(before, before, before));
    }
}
