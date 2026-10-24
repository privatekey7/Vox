namespace Vox.Core.Clipboard;

/// <summary>
/// Which clipboard formats Vox can save and put back, and the markers that keep an item out of
/// Win+V history and the cloud clipboard (learn.microsoft.com, "Clipboard Formats" → "Cloud Clipboard
/// and Clipboard History Formats").
/// </summary>
public static class ClipboardFormats
{
    public const string ExcludeFromMonitors = "ExcludeClipboardContentFromMonitorProcessing";
    public const string CanIncludeInHistory = "CanIncludeInClipboardHistory";
    public const string CanUploadToCloud = "CanUploadToCloudClipboard";

    private const uint FirstRegistered = 0xC000;

    // Standard formats whose handle is a plain memory block (HGLOBAL) Vox may copy byte for byte.
    // Left out: GDI handles (CF_BITMAP, CF_PALETTE, CF_ENHMETAFILE; Windows rebuilds the bitmap from
    // CF_DIB), CF_METAFILEPICT (memory that holds a metafile handle), owner-display formats and the
    // private/GDI-object ranges, whose data only the app that put it there understands.
    private static readonly HashSet<uint> MemoryFormats =
    [
        1,  // CF_TEXT
        4,  // CF_SYLK
        5,  // CF_DIF
        6,  // CF_TIFF
        7,  // CF_OEMTEXT
        8,  // CF_DIB
        10, // CF_PENDATA
        11, // CF_RIFF
        12, // CF_WAVE
        13, // CF_UNICODETEXT
        15, // CF_HDROP
        16, // CF_LOCALE
        17, // CF_DIBV5
    ];

    // They describe the OLE object of the app that copied, which is gone once Vox replaces the
    // clipboard; put back without OleSetClipboard they can mislead OLE readers.
    private static readonly HashSet<string> OleBookkeeping = new(StringComparer.Ordinal) { "DataObject", "Ole Private Data" };

    /// <summary>Markers Vox puts next to a copy that exists only to be pasted.</summary>
    public static IReadOnlyList<ClipboardItem> PrivateMarkers { get; } =
    [
        new(0, ExcludeFromMonitors, BitConverter.GetBytes(0u)),
        new(0, CanIncludeInHistory, BitConverter.GetBytes(0u)),
        new(0, CanUploadToCloud, BitConverter.GetBytes(0u)),
    ];

    public static bool IsMarker(string? name) => name is ExcludeFromMonitors or CanIncludeInHistory or CanUploadToCloud;

    /// <summary>Can the format be saved as bytes and put back later?</summary>
    public static bool CanCopy(uint format, string? name) =>
        format >= FirstRegistered ? name is null || !OleBookkeeping.Contains(name) : MemoryFormats.Contains(format);

    /// <summary>
    /// Does this marker ask to keep the item out of history or the cloud? Any data in the "exclude"
    /// format does; the other two do unless they hold a DWORD other than zero (1 = "may include").
    /// </summary>
    public static bool AsksPrivacy(string? name, byte[]? data) => name switch
    {
        ExcludeFromMonitors => true,
        CanIncludeInHistory or CanUploadToCloud => data is not { Length: >= 4 } || BitConverter.ToUInt32(data, 0) == 0,
        _ => false,
    };
}
