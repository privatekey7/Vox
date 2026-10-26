using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using Vox.Core.Clipboard;

namespace Vox.App.Services;

/// <summary>
/// The Windows clipboard through Win32, byte for byte: WPF's <c>Clipboard</c> can neither save every
/// format nor write the raw DWORD markers that keep an item out of Win+V history and the cloud.
/// Call only on the UI thread (it owns the clipboard window).
/// </summary>
internal static partial class SystemClipboard
{
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private const nint HWND_MESSAGE = -3;
    private const int OpenAttempts = 10;
    private static readonly TimeSpan OpenRetryDelay = TimeSpan.FromMilliseconds(100);

    // EmptyClipboard makes the window passed to OpenClipboard the owner; with no window,
    // SetClipboardData fails. A message-only window, destroyed with the UI thread's dispatcher.
    private static HwndSource? _owner;

    /// <summary>Changes whenever anyone writes the clipboard: tells whether the user copied something since.</summary>
    public static uint SequenceNumber => GetClipboardSequenceNumber();

    /// <summary>
    /// Puts text on the clipboard. <paramref name="keepPrivate"/> marks it to stay out of Win+V
    /// history and the cloud clipboard. Returns the sequence number of this write.
    /// </summary>
    public static uint WriteText(string text, bool keepPrivate)
    {
        var data = new byte[(text.Length + 1) * sizeof(char)]; // UTF-16 with the terminating null
        System.Text.Encoding.Unicode.GetBytes(text, data);
        var items = new List<ClipboardItem> { new(CF_UNICODETEXT, null, data) };
        if (keepPrivate)
        {
            items.AddRange(ClipboardFormats.PasteMarkers);
        }

        return Write(items);
    }

    /// <summary>Puts a saved clipboard back (an empty snapshot leaves the clipboard empty).</summary>
    public static void Restore(ClipboardSnapshot snapshot) => Write(snapshot.ItemsToRestore());

    /// <summary>
    /// Saves every format that is plain memory. Null if it could not be read; throws
    /// <see cref="ClipboardBusyException"/> if another app held the clipboard the whole time.
    /// </summary>
    public static ClipboardSnapshot? TrySave()
    {
        try
        {
            using var _ = Open();
            var snapshot = new ClipboardSnapshot();
            for (var format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                var name = FormatName(format);
                var data = ClipboardFormats.CanCopy(format, name) ? Read(format, name, snapshot) : null;
                if (data is null)
                {
                    snapshot.Skip(name);
                }
                else
                {
                    snapshot.Add(format, name, data);
                }
            }

            return snapshot;
        }
        catch (Win32Exception e) when (e is not ClipboardBusyException)
        {
            Log.Info($"[CLIP] Could not save the clipboard ({e.Message}); it will not be restored");
            return null;
        }
    }

    // GetClipboardData makes an app that copied with delayed rendering produce the format now.
    private static byte[]? Read(uint format, string? name, ClipboardSnapshot snapshot)
    {
        var handle = GetClipboardData(format);
        var size = handle == 0 ? 0 : (long)GlobalSize(handle);
        if (size == 0 || !snapshot.HasRoomFor(name, size)) // size 0: no data or not a memory block
        {
            return null;
        }

        var source = GlobalLock(handle);
        if (source == 0)
        {
            return null;
        }

        try
        {
            var data = new byte[size];
            Marshal.Copy(source, data, 0, data.Length);
            return data;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static uint Write(IEnumerable<ClipboardItem> items)
    {
        using (Open())
        {
            if (!EmptyClipboard())
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            }

            try
            {
                foreach (var item in items)
                {
                    Set(item);
                }
            }
            catch (Win32Exception)
            {
                // Fail closed: half an item could be content that lost its "keep private" markers.
                EmptyClipboard();
                throw;
            }
        }

        return GetClipboardSequenceNumber();
    }

    private static void Set(ClipboardItem item)
    {
        var format = item.Name is null ? item.Format : RegisterClipboardFormatW(item.Name);
        var handle = GlobalAlloc(GMEM_MOVEABLE, (nuint)Math.Max(item.Data.Length, 1));
        if (format == 0 || handle == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            GlobalFree(handle);
            throw new Win32Exception(error);
        }

        var target = GlobalLock(handle);
        if (target != 0)
        {
            Marshal.Copy(item.Data, 0, target, item.Data.Length);
            GlobalUnlock(handle);
        }

        if (target == 0 || SetClipboardData(format, handle) == 0)
        {
            var error = Marshal.GetLastPInvokeError();
            GlobalFree(handle); // the clipboard owns the handle only once SetClipboardData succeeds
            throw new Win32Exception(error);
        }
    }

    /// <summary>
    /// Another app may hold the clipboard open for a moment. One retry loop of about a second (stacked
    /// retries once made the UI wait ~10 s). The clipboard is never held across a wait.
    /// </summary>
    private static ClipboardLock Open()
    {
        _owner ??= new HwndSource(new HwndSourceParameters("Vox clipboard") { ParentWindow = HWND_MESSAGE, WindowStyle = 0 });
        for (var attempt = 1; ; attempt++)
        {
            if (OpenClipboard(_owner.Handle))
            {
                return default;
            }

            var error = Marshal.GetLastPInvokeError();
            if (attempt == OpenAttempts)
            {
                throw new ClipboardBusyException(error, Holder());
            }

            if (attempt == 1)
            {
                Log.Info($"[CLIP] Clipboard busy ({new Win32Exception(error).Message}), held by {Holder()}, retrying");
            }

            Thread.Sleep(OpenRetryDelay);
        }
    }

    // The app whose window has the clipboard open (clipboard sync of remote desktops, clipboard managers).
    private static string Holder() => Native.WindowProcessName(Native.GetOpenClipboardWindow());

    private static unsafe string? FormatName(uint format)
    {
        if (format < 0xC000)
        {
            return null; // standard formats have no registered name
        }

        var buffer = stackalloc char[256];
        var length = GetClipboardFormatNameW(format, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : null;
    }

    /// <summary>Another app kept the clipboard open through every retry.</summary>
    public sealed class ClipboardBusyException(int error, string holder) : Win32Exception(error)
    {
        public string Holder { get; } = holder;
    }

    private readonly struct ClipboardLock : IDisposable
    {
        public void Dispose() => CloseClipboard();
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint EnumClipboardFormats(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial int GetClipboardFormatNameW(uint format, char* lpszFormatName, int cchMaxCount);

    [LibraryImport("user32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormatW(string lpszFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint uFormat);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("user32.dll")]
    private static partial uint GetClipboardSequenceNumber();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint GlobalSize(nint hMem);
}
