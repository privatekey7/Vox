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

    [Fact]
    public void Latin_targets_drop_the_ending() =>
        Assert.Equal("Пользуюсь Vox.", UserCorrections.Apply("Пользуюсь воксом.", new Dictionary<string, string> { ["вокс"] = "Vox" }));

    [Fact]
    public void Short_words_are_not_declined() =>
        Assert.Equal("Это кота.", UserCorrections.Apply("Это кота.", new Dictionary<string, string> { ["кот"] = "Cat" }));

    [Fact]
    public void Learned_case_forms_are_stored_in_their_base_form()
    {
        // Fixing "керила" → "Кирилла" must not add one entry per case.
        Assert.Equal(("керил", "Кирилл"), CorrectionDetector.Detect("Позови керила.", "Позови Кирилла.", "Позови керила."));
        Assert.Equal(("гитхаб", "GitHub"), CorrectionDetector.Detect("Лежит на гитхабе.", "Лежит на GitHub.", "Лежит на гитхабе."));
    }

    [Fact]
    public void Numbers_and_currency_are_untouched() =>
        Assert.Equal(" Встреча в 11:30, бюджет 500 рублей на GitHub.",
            new PostProcessor(ModelCatalog.GigaAm).Process("Встреча в 11:30, бюджет 500 ₽ на гитхабе."));
}
