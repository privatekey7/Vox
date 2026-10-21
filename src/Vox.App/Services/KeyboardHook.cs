using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Vox.Core;

namespace Vox.App.Services;

/// <summary>
/// Global push-to-talk key via a low-level keyboard hook on its own thread.
/// </summary>
/// <remarks>
/// The hook callback only compares a key code and queues the event; handlers run on their own
/// thread, so slow work (first sound, log I/O, GC) never hits Windows' LowLevelHooksTimeout —
/// a slow hook is silently removed for good. Key presses Vox injects itself
/// (the Ctrl+V paste) carry <see cref="Native.VoxInputMarker"/> and are ignored, so they cannot
/// re-trigger recording; keys remapped by other tools (PowerToys, AutoHotkey) still count.
/// </remarks>
internal sealed class KeyboardHook : IDisposable
{
    private readonly Thread _thread;
    private readonly Thread _dispatcher;
    private readonly BlockingCollection<Action> _events = [];
    private readonly Native.LowLevelKeyboardProc _proc; // kept alive: the OS holds only a pointer
    private uint _threadId;
    private nint _hook;
    private volatile int _hotkeyVk;
    private bool _hotkeyDown;
    private bool _chord;

    public KeyboardHook(int hotkeyVirtualKey)
    {
        _hotkeyVk = hotkeyVirtualKey;
        _proc = Callback;
        var started = new ManualResetEventSlim();
        _thread = new Thread(() => Run(started)) { IsBackground = true, Name = "VoxKeyboardHook" };
        _thread.Start();
        started.Wait();
        _dispatcher = new Thread(Dispatch) { IsBackground = true, Name = "VoxHotkeyEvents" };
        _dispatcher.Start();
    }

    /// <summary>The hotkey went down (repeats while held are filtered out).</summary>
    public event Action? Pressed;

    /// <summary>The hotkey went up. The flag is true when another key was pressed meanwhile (Ctrl+C…).</summary>
    public event Action<bool>? Released;

    public void SetHotkey(int virtualKey) => _hotkeyVk = virtualKey;

    public void Dispose()
    {
        Native.PostThreadMessageW(_threadId, Native.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _events.CompleteAdding();
    }

    private void Run(ManualResetEventSlim started)
    {
        _threadId = Native.GetCurrentThreadId();
        _hook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandleW(null), 0);
        if (_hook == 0)
        {
            Log.Info($"[HOOK] SetWindowsHookEx failed: {Marshal.GetLastPInvokeError()}");
        }

        started.Set();
        while (Native.GetMessageW(out _, 0, 0, 0) > 0)
        {
            // Low-level hooks are dispatched by this message loop; nothing else to do.
        }

        Native.UnhookWindowsHookEx(_hook);
    }

    private nint Callback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            if (info.dwExtraInfo != Native.VoxInputMarker)
            {
                var down = wParam is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                var up = wParam is Native.WM_KEYUP or Native.WM_SYSKEYUP;
                if (info.vkCode == _hotkeyVk)
                {
                    if (down && !_hotkeyDown)
                    {
                        _hotkeyDown = true;
                        _chord = false;
                        MaskAltTap(info.vkCode);
                        Raise(() => Pressed?.Invoke());
                    }
                    else if (up && _hotkeyDown)
                    {
                        _hotkeyDown = false;
                        var chord = _chord;
                        Raise(() => Released?.Invoke(chord));
                    }

                    if (Hotkeys.Swallow((int)info.vkCode))
                    {
                        return 1; // the focused app never sees the key
                    }
                }
                else if (down && _hotkeyDown && info.scanCode != Native.AltGrFakeCtrlScanCode)
                {
                    _chord = true; // a shortcut, not a dictation
                }
            }
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// A lone Alt press-and-release opens the focused app's menu, which then swallows the Ctrl+V
    /// paste. A dummy key while Alt is down makes the release an ordinary one.
    /// </summary>
    private static void MaskAltTap(uint vk)
    {
        if (vk is Native.VK_LMENU or Native.VK_RMENU)
        {
            Native.INPUT[] mask = [Native.Key(Native.VK_MASK, up: false), Native.Key(Native.VK_MASK, up: true)];
            Native.SendInput((uint)mask.Length, mask, Marshal.SizeOf<Native.INPUT>());
        }
    }

    /// <summary>Queue a handler; they run one at a time, in order (a press before its release).</summary>
    private void Raise(Action handler) => _events.TryAdd(handler);

    private void Dispatch()
    {
        foreach (var handler in _events.GetConsumingEnumerable())
        {
            try
            {
                handler();
            }
            catch (Exception e)
            {
                Log.Error("[HOOK] Handler failed", e);
            }
        }
    }
}
