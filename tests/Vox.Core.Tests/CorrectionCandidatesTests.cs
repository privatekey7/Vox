using Vox.Core.Text;

namespace Vox.Core.Tests;

public sealed class CorrectionCandidatesTests
{
    [Fact]
    public void A_fix_is_learned_only_when_seen_a_second_time()
    {
        // Regression: a single one-off edit was learned at once and rewrote all later dictations.
        var candidates = new CorrectionCandidates();

        Assert.False(candidates.Confirm("кубернетис", "Kubernetes"));
        Assert.False(candidates.Confirm("докер", "Docker"));
        Assert.True(candidates.Confirm("кубернетис", "Kubernetes"));
    }

    [Fact]
    public void A_different_fix_of_the_same_word_starts_over()
    {
        var candidates = new CorrectionCandidates();

        Assert.False(candidates.Confirm("вокс", "Vox"));
        Assert.False(candidates.Confirm("вокс", "Fox"));
        Assert.True(candidates.Confirm("вокс", "Fox"));
    }
}
