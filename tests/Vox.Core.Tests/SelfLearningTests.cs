using Vox.Core.Storage;
using Vox.Core.Text;

namespace Vox.Core.Tests;

/// <summary>Owner (22.10): learn a fix at once, undo the last one from the tray.</summary>
public sealed class SelfLearningTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("vox-learn-").FullName;
    private readonly VoxDatabase _db;
    private readonly SelfLearning _learning;

    public SelfLearningTests()
    {
        _db = new VoxDatabase(Path.Combine(_dir, "vox_data.db"));
        _learning = new SelfLearning(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void The_first_fix_goes_into_the_dictionary()
    {
        Assert.Equal((LearnResult.Learned, "Лебовски", "Lebowski"), _learning.Learn("Лебовски", "Lebowski"));
        Assert.Equal("Lebowski", _db.Corrections()["лебовски"]);
    }

    [Fact]
    public void A_common_russian_word_is_never_learned()
    {
        // Regression (owner's log): "рак" → "RAG" turned every "рак" into RAG.
        Assert.True(SelfLearning.IsCommonWord("рак"));
        Assert.Equal(LearnResult.CommonWord, _learning.Learn("рак", "RAG").Result);
        Assert.Empty(_db.Corrections());
    }

    [Fact]
    public void Undo_removes_exactly_the_last_learned_pair()
    {
        _learning.Learn("гитхап", "GitHub");
        _learning.Learn("кубернетис", "Kubernetes");

        Assert.Equal(("кубернетис", "Kubernetes"), _learning.UndoLast());
        Assert.Equal(["гитхап"], _db.Corrections().Keys);
        Assert.Null(_learning.UndoLast()); // only the last one
    }

    [Fact]
    public void Undo_restores_a_manual_entry_the_fix_overwrote()
    {
        _db.AddDictionaryEntry("вокс", "VOX", "", "manual");
        _learning.Learn("вокс", "Vox");

        _learning.UndoLast();

        Assert.Equal(("вокс", "VOX", "manual"), _db.DictionaryEntries().Select(e => (e.Wrong, e.Correct, e.Source)).Single());
    }

    [Fact]
    public void Nothing_to_undo_at_start() => Assert.False(_learning.CanUndo);

    [Fact]
    public void A_typo_in_a_learned_target_is_fixed_in_place()
    {
        // Owner: "гитхап → GitHbu" learned with a typo; Vox then writes "GitHbu" and the user fixes it.
        _learning.Learn("гитхап", "GitHbu");

        Assert.Equal((LearnResult.TargetFixed, "гитхап", "GitHub"), _learning.Learn("GitHbu", "GitHub"));
        Assert.Equal(new Dictionary<string, string> { ["гитхап"] = "GitHub" }, _db.Corrections()); // no "githbu" chain
    }

    [Fact]
    public void Undo_brings_back_the_previous_target()
    {
        _db.AddDictionaryEntry("кубер", "Kubernets", "", "manual");
        _learning.Learn("Kubernets", "Kubernetes");

        _learning.UndoLast();

        Assert.Equal(("кубер", "Kubernets", "manual"), _db.DictionaryEntries().Select(e => (e.Wrong, e.Correct, e.Source)).Single());
    }

    [Fact]
    public void A_change_without_the_user_typing_is_not_learned()
    {
        // Autocorrect (Word, Grammarly, a messenger) changed the text, not the user.
        Assert.Equal(LearnResult.NotTyped, _learning.Learn("гитхап", "GitHub", userTyped: false).Result);
        Assert.Empty(_db.Corrections());
    }

    [Theory]
    [InlineData("ик", "IK")]
    [InlineData("в а", "VA")] // a merged phrase counts its letters
    public void Too_short_to_learn_automatically(string wrong, string correct)
    {
        Assert.Equal(LearnResult.TooShort, _learning.Learn(wrong, correct).Result);
        Assert.Empty(_db.Corrections());
    }

    [Fact]
    public void An_entry_that_flips_back_and_forth_gets_pinned()
    {
        _learning.Learn("гитхап", "GitHub");
        Assert.Equal(LearnResult.TargetFixed, _learning.Learn("GitHub", "Github").Result); // A → B
        Assert.Equal(LearnResult.TargetFixed, _learning.Learn("Github", "GitHub").Result); // back: flip 1
        Assert.Equal(LearnResult.TargetFixed, _learning.Learn("GitHub", "Github").Result); // back: flip 2, pinned

        Assert.Equal(LearnResult.Pinned, _learning.Learn("Github", "GitHub").Result);
        Assert.Equal(LearnResult.Pinned, _learning.Learn("гитхап", "GitHab").Result); // a new pair for the same word too
        Assert.Equal("Github", _db.Corrections()["гитхап"]);
    }
}
