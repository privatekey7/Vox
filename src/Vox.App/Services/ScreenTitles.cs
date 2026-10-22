using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Vox.App.Services;

/// <summary>
/// Titles of the visible top-level windows, read at a dictation for <see cref="Vox.Core.Text.ScreenWords"/>
/// (a browser's title is its active tab).
/// </summary>
/// <remarks>
/// GetWindowText of another process's window returns the cached caption without a message, so a hung app
/// cannot block it; the whole read still has a timeout. Titles stay in memory and are never logged.
/// Vox's own windows, hidden and cloaked ones (other desktops, suspended UWP) are skipped.
/// </remarks>
internal static class ScreenTitles
{
    private const int MaxWindows = 300;
    private const int MaxTitle = 512;

    public static List<string> Read(TimeSpan timeout)
    {
        var read = Task.Run(ReadAll);
        return read.Wait(timeout) ? read.Result : [];
    }

    private static unsafe List<string> ReadAll()
    {
        var state = new State((uint)Environment.ProcessId);
        var handle = GCHandle.Alloc(state);
        try
        {
            Native.EnumWindows(&OnWindow, GCHandle.ToIntPtr(handle));
        }
        finally
        {
            handle.Free();
        }

        return state.Titles;
    }

    [UnmanagedCallersOnly]
    private static unsafe int OnWindow(nint hwnd, nint param)
    {
        var state = (State)GCHandle.FromIntPtr(param).Target!;
        try
        {
            if (!Native.IsWindowVisible(hwnd)
                || (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0))
            {
                return 1;
            }

            Native.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == state.Self)
            {
                return 1; // our own window would answer by message: never read, never a name
            }

            var buffer = stackalloc char[MaxTitle];
            var length = Native.GetWindowText(hwnd, buffer, MaxTitle);
            if (length > 0)
            {
                state.Titles.Add(new string(buffer, 0, length));
            }
        }
        catch (Exception e)
        {
            Debug.WriteLine(e); // an exception must not cross the native callback
        }

        return state.Titles.Count < MaxWindows ? 1 : 0;
    }

    private sealed record State(uint Self)
    {
        public List<string> Titles { get; } = [];
    }
}
