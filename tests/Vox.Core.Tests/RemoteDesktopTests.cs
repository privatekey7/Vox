using Vox.Core.Clipboard;

namespace Vox.Core.Tests;

/// <summary>Dictating from a laptop into a RustDesk window, to an office computer that runs Vox too.</summary>
public sealed class RemoteDesktopTests
{
    [Fact]
    public void A_hotkey_replayed_by_RustDesk_does_not_start_a_recording()
    {
        // The office Vox recorded silence (peak 0) whenever the hotkey was held in the laptop's RustDesk window.
        Assert.True(Hotkeys.IsFromRemoteControl(Hotkeys.RustDeskInputMarker));
    }

    [Theory]
    [InlineData(0)]          // a real key press
    [InlineData(0x564F58)]   // Vox's own Ctrl+V (Native.VoxInputMarker), filtered on its own
    [InlineData(0x41484B)]   // any other tool's marker: a remapped key still counts
    public void Other_keys_still_count(long extraInfo) =>
        Assert.False(Hotkeys.IsFromRemoteControl((nint)extraInfo));

    [Theory]
    [InlineData("rustdesk")]
    [InlineData("RustDesk")]
    public void A_paste_into_RustDesk_waits_for_its_clipboard_sync(string process)
    {
        // RustDesk sends the clipboard every 333 ms (CLIPBOARD_INTERVAL) but Ctrl+V at once: Ctrl+V reached
        // the office computer before the dictation did, and the restore came before the sync.
        var delays = PasteTiming.For(process);

        Assert.Equal(TimeSpan.FromMilliseconds(700), delays.BeforePaste);
        Assert.Equal(TimeSpan.FromMilliseconds(1500), delays.BeforeRestore);
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("chrome")]
    [InlineData("notepad")]
    [InlineData("?")]
    public void Other_apps_keep_the_quick_paste(string process)
    {
        var delays = PasteTiming.For(process);

        Assert.Equal(TimeSpan.FromMilliseconds(50), delays.BeforePaste);
        Assert.Equal(TimeSpan.FromMilliseconds(500), delays.BeforeRestore);
    }
}
