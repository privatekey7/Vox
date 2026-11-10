using Vox.Core.Models;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>
/// Owner's test of Parakeet, 10.11: a term written in Latin by the model is found like the spoken ones. The term in
/// another case or with a space for a hyphen ("sherpa onnx", "github"), a two-word term with one word heard as an
/// English word ("clot code"), Parakeet's own spellings ("Sherpa on the Nix").
/// </summary>
public class LatinTermsTests
{
    private static readonly PostProcessor Parakeet = new(ModelCatalog.Parakeet);
    private static readonly PostProcessor GigaAm = new(ModelCatalog.GigaAm);

    [Theory]
    [InlineData("Open the pull request on GitHub and run clot code.", " Open the pull request on GitHub and run Claude Code.")]
    [InlineData("Sherpa on the Nix version two works fine.", " Sherpa-ONNX version two works fine.")]
    [InlineData("The sherpa onnx engine works.", " The Sherpa-ONNX engine works.")]
    [InlineData("Push it to github today.", " Push it to GitHub today.")]
    [InlineData("Run it in docker.", " Run it in Docker.")]
    [InlineData("Then clot code fixes it.", " Then Claude Code fixes it.")]
    public void Parakeet_spelling_becomes_the_term(string raw, string expected) => Assert.Equal(expected, Parakeet.Process(raw));

    [Theory]
    [InlineData("Движок на unity.", " Движок на Unity.")]
    [InlineData("Запусти clot code.", " Запусти Claude Code.")]
    [InlineData("Это sherpa onnx.", " Это Sherpa-ONNX.")]
    public void Latin_term_in_russian_speech(string raw, string expected) => Assert.Equal(expected, GigaAm.Process(raw));

    [Theory]
    [InlineData("Let's go home.")] // a plain English word in English speech stays
    [InlineData("A python ate the mouse.")]
    [InlineData("We need unity in the team.")]
    [InlineData("Open github.com and the docker-compose.yml file.")] // an address and a file name stay
    [InlineData("The code is clean and good code runs.")]
    [InlineData("Cloud code runs on the server.")] // "cloud" is a word of a term itself
    [InlineData("The blood code is red.")] // a sound like "Claude" without its first letter
    public void English_words_stay(string raw) => Assert.Equal(" " + raw, Parakeet.Process(raw));

    // Owner's history: a phrase of English words added on purpose works in any speech; a term written in lower case
    // does not take the speaker's capital.
    [Theory]
    [InlineData("Clod cod app.", " Claude Code app.")]
    [InlineData("Open code.", " OpenCode.")]
    [InlineData("Точка ENV", " Точка ENV")]
    [InlineData("Brandbook общения Private KAI.", " Brandbook общения Private KAI.")]
    [InlineData("Только в Cload Code.", " Только в Claude Code.")]
    public void History_cases(string raw, string expected) => Assert.Equal(expected, GigaAm.Process(raw));
}
