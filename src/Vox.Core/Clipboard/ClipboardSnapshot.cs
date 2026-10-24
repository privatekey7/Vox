namespace Vox.Core.Clipboard;

/// <summary>One clipboard format: its id, its registered name (null for standard formats) and its bytes.</summary>
public sealed record ClipboardItem(uint Format, string? Name, byte[] Data);

/// <summary>
/// The user's clipboard saved before a dictation is pasted, to be put back afterwards. Formats are kept
/// in the order the clipboard listed them: a reader takes the first one it understands.
/// </summary>
public sealed class ClipboardSnapshot
{
    /// <summary>A 4K screenshot is ~33 MB as a DIB; bigger content is not worth holding in memory.</summary>
    public const long MaxBytes = 64L * 1024 * 1024;

    /// <summary>A marker holds a DWORD or a few bytes; anything bigger is not a real one.</summary>
    public const long MaxMarkerBytes = 1024;

    private readonly List<ClipboardItem> _items = [];
    private long _bytes;
    private bool _lostMarker;

    public IReadOnlyList<ClipboardItem> Items => _items;

    /// <summary>The item was marked to stay out of Win+V history or the cloud (e.g. a password manager).</summary>
    public bool IsPrivate { get; private set; }

    /// <summary>Formats Vox could not save (not plain memory, unreadable or over the size limit).</summary>
    public int Skipped { get; private set; }

    /// <summary>
    /// Is there room for a format of this size? Markers do not count against the limit: they must come
    /// back with the content even when part of it does not.
    /// </summary>
    public bool HasRoomFor(string? name, long size) =>
        ClipboardFormats.IsMarker(name) ? size <= MaxMarkerBytes : _bytes + size <= MaxBytes;

    public void Add(uint format, string? name, byte[] data)
    {
        IsPrivate |= ClipboardFormats.AsksPrivacy(name, data);
        _items.Add(new ClipboardItem(format, name, data));
        _bytes += data.Length;
    }

    /// <summary>A format that was on the clipboard but is not saved.</summary>
    public void Skip(string? name)
    {
        Skipped++;
        if (ClipboardFormats.IsMarker(name))
        {
            // Its value is unknown: assume it asked for privacy rather than restore a password without it.
            IsPrivate = true;
            _lostMarker = true;
        }
    }

    /// <summary>What to put back: the saved formats, plus Vox's own markers if a marker could not be saved.</summary>
    public IEnumerable<ClipboardItem> ItemsToRestore() =>
        _lostMarker ? _items.Concat(ClipboardFormats.PrivateMarkers) : _items;
}
