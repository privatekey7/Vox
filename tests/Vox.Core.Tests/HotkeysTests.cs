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

    // Security audit L5 (25.10): only keys that edit text count as the user's own fix.
    [Theory]
    [InlineData(0x4B, false, false, false, false, true)] // K
    [InlineData(0x4B, false, true, false, false, true)] // Shift+K
    [InlineData(0x34, false, false, false, false, true)] // 4
    [InlineData(0xBE, false, false, false, false, true)] // "." (ю on the Russian layout)
    [InlineData(0x45, true, false, true, false, true)] // AltGr+E: "€"
    [InlineData(0x08, false, false, false, false, true)] // Backspace
    [InlineData(0x08, true, false, false, false, true)] // Ctrl+Backspace: a word
    [InlineData(0x2E, false, false, false, false, true)] // Delete
    [InlineData(0x56, true, false, false, false, true)] // Ctrl+V
    [InlineData(0x5A, true, false, false, false, true)] // Ctrl+Z
    [InlineData(0x2D, false, true, false, false, true)] // Shift+Insert
    [InlineData(0xE7, false, false, false, false, true)] // VK_PACKET: AutoHotkey, on-screen keyboard
    [InlineData(0xE5, false, false, false, false, true)] // VK_PROCESSKEY: an IME
    [InlineData(0x20, false, false, false, false, false)] // Space
    [InlineData(0x0D, false, false, false, false, false)] // Enter
    [InlineData(0x09, false, false, false, false, false)] // Tab
    [InlineData(0x1B, false, false, false, false, false)] // Esc
    [InlineData(0x25, false, false, false, false, false)] // Left
    [InlineData(0x23, false, true, false, false, false)] // Shift+End
    [InlineData(0x74, false, false, false, false, false)] // F5
    [InlineData(0x41, true, false, false, false, false)] // Ctrl+A
    [InlineData(0x43, true, false, false, false, false)] // Ctrl+C
    [InlineData(0x46, false, false, true, false, false)] // Alt+F
    [InlineData(0x56, false, false, false, true, false)] // Win+V
    public void Only_keys_that_edit_text_count_as_typing(int vk, bool ctrl, bool shift, bool alt, bool win, bool editing) =>
        Assert.Equal(editing, Hotkeys.IsEditing(vk, ctrl, shift, alt, win));
}
