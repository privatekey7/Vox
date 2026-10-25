using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Owner request: dictionary replacements understand Russian case forms.</summary>
public sealed class DeclensionTests
{
    [Theory]
    [InlineData("Лежит на гитхабе.", "Лежит на GitHub.")]
    [InlineData("Работаю с гитхабом.", "Работаю с GitHub.")]
    [InlineData("Запусти в докере.", "Запусти в Docker.")]
    [InlineData("Нет докера.", "Нет Docker.")]
    [InlineData("На гитхаб.", "На GitHub.")] // the base form, as before
    // Multi-word sources decline their last word.
    [InlineData("Подними в докер компоузе.", "Подними в docker-compose.")]
    [InlineData("Сборка в гитхаб экшенсах.", "Сборка в GitHub Actions.")]
    [InlineData("Настрой с гитхаб экшенсом.", "Настрой с GitHub Actions.")]
    // Regression (owner's log): a Latin+Cyrillic target lost the ending ("API-ключ,").
    [InlineData("Работаю с апи ключами.", "Работаю с API-ключами.")]
    [InlineData("Нет лора адаптера.", "Нет LoRA-адаптера.")]
    // Regression (owner's log): "клод-код" said with a hyphen.
    [InlineData("Открой клод-код.", "Открой Claude Code.")]
    [InlineData("В клод-коде.", "В Claude Code.")]
    // Owner: after г, к, х the plural ends in "-и" ("веб-хуки"), not "-ы".
    [InlineData("Подключим веб-хуки.", "Подключим webhook.")]
    public void Built_in_terms_match_case_forms(string spoken, string expected) =>
        Assert.Equal(expected, Terms.Replace(spoken));

    [Theory]
    [InlineData("Продам машину.")] // прод → prod
    [InlineData("Я войду позже.")] // войд → void
    [InlineData("Держи на контроле.")] // контрол → Ctrl
    [InlineData("Свет через призму.")] // призма → Prisma
    [InlineData("Позвони Джейсону.")] // джейсон → JSON
    public void Case_forms_that_are_ordinary_words_are_not_replaced(string text) =>
        Assert.Equal(text, Terms.Replace(text));

    [Theory]
    [InlineData("Созвонимся с керилом.", "Созвонимся с Кириллом.")]
    [InlineData("Позови керила.", "Позови Кирилла.")]
    [InlineData("Керилу привет.", "Кириллу привет.")]
    [InlineData("Это керил.", "Это Кирилл.")]
    public void User_corrections_carry_the_ending_over(string spoken, string expected) =>
        Assert.Equal(expected, UserCorrections.Apply(spoken, new Dictionary<string, string> { ["керил"] = "Кирилл" }));

    [Theory]
    [InlineData("Где мои бурундуки?", "Где мои Бурундуки?")] // a velar word to a velar word keeps "-и"
    [InlineData("Позови бурундука.", "Позови Бурундука.")]
    [InlineData("Где мои бурундуки?", "Где мои бурундуки?", "Кирилл")] // "-и" does not fit a non-velar target
    public void Velar_words_take_the_plural_in_i(string spoken, string expected, string? target = null) =>
        Assert.Equal(expected, UserCorrections.Apply(spoken, new Dictionary<string, string> { ["бурундук"] = target ?? "Бурундук" }));

    [Fact]
    public void Latin_targets_drop_the_ending() =>
        Assert.Equal("Пользуюсь Vox.", UserCorrections.Apply("Пользуюсь воксом.", new Dictionary<string, string> { ["вокс"] = "Vox" }));

    [Fact]
    public void Short_words_are_not_declined() =>
        Assert.Equal("Это кота.", UserCorrections.Apply("Это кота.", new Dictionary<string, string> { ["кот"] = "Cat" }));

    [Fact]
    public void A_learned_pair_is_stored_as_the_user_fixed_it()
    {
        // Regression (owner): "Лебовски" → "Lebowski" was stored and shown as "лебовск".
        Assert.Equal(("Лебовски", "Lebowski"), CorrectionDetector.Detect("Пересмотрю Большого Лебовски.", "Пересмотрю Большого Lebowski.", "Пересмотрю Большого Лебовски."));
        Assert.Equal(("керила", "Кирилла"), CorrectionDetector.Detect("Позови керила.", "Позови Кирилла.", "Позови керила."));
        Assert.Equal(("гитхабе", "GitHub"), CorrectionDetector.Detect("Лежит на гитхабе.", "Лежит на GitHub.", "Лежит на гитхабе."));
    }

    [Theory]
    // One entry still covers every case: the base form is worked out when the dictionary is applied.
    [InlineData("керила", "Кирилла", "Созвонимся с керилом.", "Созвонимся с Кириллом.")]
    [InlineData("гитхабе", "GitHub", "Работаю с гитхабом.", "Работаю с GitHub.")]
    [InlineData("лебовски", "Lebowski", "Пересмотрю Большого Лебовски.", "Пересмотрю Большого Lebowski.")]
    public void A_learned_case_form_matches_other_cases(string wrong, string correct, string text, string expected) =>
        Assert.Equal(expected, UserCorrections.Apply(text, new Dictionary<string, string> { [wrong] = correct }));

    [Fact]
    public void Numbers_and_currency_are_untouched() =>
        Assert.Equal(" Встреча в 11:30, бюджет 500 рублей на GitHub.",
            new PostProcessor(ModelCatalog.GigaAm).Process("Встреча в 11:30, бюджет 500 ₽ на гитхабе."));
}
