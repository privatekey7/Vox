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
        Assert.Equal(LearnResult.Learned, _learning.Learn("Лебовски", "Lebowski"));
        Assert.Equal("Lebowski", _db.Corrections()["лебовски"]);
    }

    [Fact]
    public void A_common_russian_word_is_never_learned()
    {
        // Regression (owner's log): "рак" → "RAG" turned every "рак" into RAG.
        Assert.True(SelfLearning.IsCommonWord("рак"));
        Assert.Equal(LearnResult.CommonWord, _learning.Learn("рак", "RAG"));
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
}
