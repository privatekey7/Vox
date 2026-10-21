namespace Vox.Core.Tests;

public sealed class HotkeysTests
{
    [Fact]
    public void Pause_is_not_offered()
    {
        // Regression: Pause sends press and release at once, so it cannot be held to dictate.
        Assert.False(Hotkeys.IsKnown("pause"));
    }

}
