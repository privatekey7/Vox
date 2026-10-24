using Vox.Core.Clipboard;

namespace Vox.Core.Tests;

public sealed class ClipboardSnapshotTests
{
    private static readonly byte[] Zero = BitConverter.GetBytes(0u);
    private static readonly byte[] One = BitConverter.GetBytes(1u);

    [Fact]
    public void Private_markers_are_the_three_documented_formats_with_dword_zero()
    {
        var markers = ClipboardFormats.PrivateMarkers;

        Assert.Equal(
            ["ExcludeClipboardContentFromMonitorProcessing", "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard"],
            markers.Select(m => m.Name));
        Assert.All(markers, m => Assert.Equal(Zero, m.Data));
    }

    [Theory]
    [InlineData(13u, null, true)]      // CF_UNICODETEXT
    [InlineData(1u, null, true)]       // CF_TEXT
    [InlineData(8u, null, true)]       // CF_DIB: images survive
    [InlineData(17u, null, true)]      // CF_DIBV5
    [InlineData(15u, null, true)]      // CF_HDROP: copied files survive
    [InlineData(2u, null, false)]      // CF_BITMAP: a GDI handle, rebuilt from CF_DIB
    [InlineData(3u, null, false)]      // CF_METAFILEPICT: holds a metafile handle
    [InlineData(14u, null, false)]     // CF_ENHMETAFILE
    [InlineData(0x80u, null, false)]   // CF_OWNERDISPLAY
    [InlineData(0x200u, null, false)]  // private range
    [InlineData(0x300u, null, false)]  // GDI-object range
    [InlineData(0xC100u, "HTML Format", true)]
    [InlineData(0xC101u, "Preferred DropEffect", true)]
    [InlineData(0xC102u, "Ole Private Data", false)]
    [InlineData(0xC103u, "DataObject", false)]
    [InlineData(0xC104u, null, true)]  // registered, name unknown
    public void Only_plain_memory_formats_are_copied(uint format, string? name, bool expected) =>
        Assert.Equal(expected, ClipboardFormats.CanCopy(format, name));

    [Fact]
    public void A_plain_copy_is_not_private()
    {
        var snapshot = new ClipboardSnapshot();
        snapshot.Add(13, null, [0x41, 0, 0, 0]);

        Assert.False(snapshot.IsPrivate);
        Assert.Single(snapshot.ItemsToRestore());
    }

    [Theory]
    [InlineData("ExcludeClipboardContentFromMonitorProcessing")]
    [InlineData("CanIncludeInClipboardHistory")]
    [InlineData("CanUploadToCloudClipboard")]
    public void A_password_manager_marker_makes_the_snapshot_private(string marker)
    {
        var snapshot = new ClipboardSnapshot();
        snapshot.Add(13, null, [0x41, 0, 0, 0]);
        snapshot.Add(0xC200, marker, Zero);

        Assert.True(snapshot.IsPrivate);
    }

    [Fact]
    public void Markers_come_back_with_the_content_in_the_original_order()
    {
        var snapshot = new ClipboardSnapshot();
        snapshot.Add(0xC200, "ExcludeClipboardContentFromMonitorProcessing", [1]);
        snapshot.Add(13, null, [0x41, 0, 0, 0]);
        snapshot.Add(0xC201, "CanIncludeInClipboardHistory", Zero);

        Assert.Equal(
            ["ExcludeClipboardContentFromMonitorProcessing", null, "CanIncludeInClipboardHistory"],
            snapshot.ItemsToRestore().Select(i => i.Name));
    }

    [Theory]
    [InlineData("CanIncludeInClipboardHistory")]
    [InlineData("CanUploadToCloudClipboard")]
    public void A_dword_one_asks_to_be_included_not_excluded(string marker)
    {
        var snapshot = new ClipboardSnapshot();
        snapshot.Add(0xC200, marker, One);

        Assert.False(snapshot.IsPrivate);
    }

    [Fact]
    public void A_malformed_dword_counts_as_private()
    {
        var snapshot = new ClipboardSnapshot();
        snapshot.Add(0xC200, "CanIncludeInClipboardHistory", [1]);

        Assert.True(snapshot.IsPrivate);
    }

    [Fact]
    public void A_marker_that_could_not_be_saved_is_replaced_by_vox_markers()
    {
        var snapshot = new ClipboardSnapshot();
        snapshot.Add(13, null, [0x41, 0, 0, 0]);
        snapshot.Skip("ExcludeClipboardContentFromMonitorProcessing");

        var restored = snapshot.ItemsToRestore().ToList();

        Assert.True(snapshot.IsPrivate);
        Assert.Equal(1, snapshot.Skipped);
        Assert.Equal(13u, restored[0].Format);
        Assert.Equal(ClipboardFormats.PrivateMarkers, restored.Skip(1));
    }

    [Fact]
    public void Skipping_an_ordinary_format_adds_no_markers()
    {
        var snapshot = new ClipboardSnapshot();
        snapshot.Skip(null);

        Assert.False(snapshot.IsPrivate);
        Assert.Empty(snapshot.ItemsToRestore());
    }

    [Fact]
    public void Content_over_the_limit_is_skipped_but_markers_still_fit()
    {
        var snapshot = new ClipboardSnapshot();
        snapshot.Add(8, null, new byte[ClipboardSnapshot.MaxBytes - 10]);

        Assert.True(snapshot.HasRoomFor(null, 10));
        Assert.False(snapshot.HasRoomFor(null, 11));
        Assert.True(snapshot.HasRoomFor("CanUploadToCloudClipboard", 4));
    }

    [Fact]
    public void An_oversized_marker_is_not_a_real_one()
    {
        var snapshot = new ClipboardSnapshot();

        Assert.False(snapshot.HasRoomFor("ExcludeClipboardContentFromMonitorProcessing", ClipboardSnapshot.MaxMarkerBytes + 1));
    }
}
