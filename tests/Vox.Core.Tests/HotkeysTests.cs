namespace Vox.Core.Tests;

public sealed class HotkeysTests
{
    [Fact]
    public void Pause_is_not_offered()
    {
        // Regression: Pause sends press and release at once, so it cannot be held to dictate.
        Assert.False(Hotkeys.IsKnown("pause"));
    }

    [Theory]
    [InlineData("caps lock", true)]
    [InlineData("scroll lock", true)]
    [InlineData("f5", true)]
    [InlineData("f12", true)]
    [InlineData("right ctrl", false)]
    [InlineData("ctrl", false)]
    [InlineData("shift", false)]
    [InlineData("right alt", false)]
    public void Only_keys_without_their_own_shortcuts_are_hidden_from_apps(string name, bool swallowed)
    {
        // Regression: the hotkey reached the focused app too — F5 reloaded the page, Caps Lock toggled.
        Assert.Equal(swallowed, Hotkeys.Swallow(Hotkeys.VirtualKey(name)));
    }

    [Theory]
    [InlineData(0x53, true, false, false, false, true)] // Ctrl+S
    [InlineData(0x53, false, false, false, false, false)] // S typed
    [InlineData(0x53, true, true, false, false, false)] // Ctrl+Shift+S: "save as"
    [InlineData(0x53, true, false, true, false, false)] // Ctrl+Alt+S, AltGr+S
    [InlineData(0x53, true, false, false, true, false)]
    [InlineData(0x56, true, false, false, false, false)] // Ctrl+V
    public void Only_plain_ctrl_s_remembers_a_fix(int vk, bool ctrl, bool shift, bool alt, bool win, bool save) =>
        Assert.Equal(save, Hotkeys.IsSave(vk, ctrl, shift, alt, win));
}
