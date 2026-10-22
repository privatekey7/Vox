using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

public sealed class KeyCombosTests
{
    // Regression: "Control C" / "контрол" came out as "Contr+ C", "Contrl", "Control B".
    [Theory]
    [InlineData("Нажми контрол си.", "Нажми Ctrl+C.")]
    [InlineData("Нажми Control C.", "Нажми Ctrl+C.")]
    [InlineData("Нажми Contr+ C.", "Нажми Ctrl+C.")]
    [InlineData("Потом Contrl V", "Потом Ctrl+V")]
    [InlineData("контрол плюс вэ", "Ctrl+V")]
    [InlineData("контроль плюс зет", "Ctrl+Z")]
    [InlineData("Контрол шифт ти", "Ctrl+Shift+T")]
    [InlineData("альт эф четыре", "Alt+F4")]
    [InlineData("Alt F4", "Alt+F4")]
    [InlineData("контрол энтер", "Ctrl+Enter")]
    [InlineData("шифт таб", "Shift+Tab")]
    [InlineData("контрол, икс", "Ctrl+X")]
    [InlineData("Ctrl+S", "Ctrl+S")]
    [InlineData("контрол плюс 1", "Ctrl+1")]
    [InlineData("Contrl C", "Ctrl+C")]
    [InlineData("contral V", "Ctrl+V")]
    [InlineData("Контрол вэ", "Ctrl+V")]
    [InlineData("контрол зет", "Ctrl+Z")]
    [InlineData("Contrl A", "Ctrl+A")]
    [InlineData("контрол 1", "Ctrl+1")]
    [InlineData("контрол эф пять", "Ctrl+F5")]
    [InlineData("Нажми Control B.", "Нажми Ctrl+B.")] // B stays B: a real Ctrl+B is possible
    public void Spoken_shortcuts_become_key_names(string spoken, string expected) =>
        Assert.Equal(expected, KeyCombos.Apply(spoken));

    [Theory]
    [InlineData("Нужен контроль качества.")]
    [InlineData("Нужен контроль, а не доверие.")]
    [InlineData("We need quality control a lot.")]
    [InlineData("Он делит пирог.")]
    [InlineData("Нужен контроль 2 этапов.")]
    [InlineData("Играет на альте.")]
    public void Ordinary_speech_is_left_alone(string text) =>
        Assert.Equal(text, KeyCombos.Apply(text));

    [Fact]
    public void Works_in_the_pipeline() =>
        Assert.Equal(" Нажми Ctrl+C, потом Ctrl+V.", new PostProcessor(ModelCatalog.GigaAm).Process("Нажми контрол си, потом контрол ви."));

    [Fact]
    public void Lone_key_names_are_spelled_as_keys() =>
        Assert.Equal(" Нажми Enter, потом Shift.", new PostProcessor(ModelCatalog.GigaAm).Process("Нажми энтер, потом шифт."));
}
