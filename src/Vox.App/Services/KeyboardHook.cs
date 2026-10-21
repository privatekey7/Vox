using System.Runtime.InteropServices;

namespace Vox.App.Services;

/// <summary>
/// Global push-to-talk key via a low-level keyboard hook on its own thread.
/// </summary>
/// <remarks>
/// The hook callback only compares a key code and raises an event, so it never comes close to
/// Windows' LowLevelHooksTimeout (a slow hook is silently removed). Key presses Vox injects itself
/// (the Ctrl+V paste) carry LLKHF_INJECTED and are ignored, so they cannot re-trigger recording.
/// </remarks>
internal sealed class KeyboardHook : IDisposable
{
    private readonly Thread _thread;
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
            if ((info.flags & Native.LLKHF_INJECTED) == 0)
            {
                var down = wParam is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                var up = wParam is Native.WM_KEYUP or Native.WM_SYSKEYUP;
                if (info.vkCode == _hotkeyVk)
                {
                    if (down && !_hotkeyDown)
                    {
                        _hotkeyDown = true;
                        _chord = false;
                        Raise(() => Pressed?.Invoke());
                    }
                    else if (up && _hotkeyDown)
                    {
                        _hotkeyDown = false;
                        var chord = _chord;
                        Raise(() => Released?.Invoke(chord));
                    }
                }
                else if (down && _hotkeyDown)
                {
                    _chord = true; // a shortcut, not a dictation
                }
            }
        }

        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <summary>
    /// Handlers run on the hook thread, in order (press is always handled before its release), and
    /// must return quickly — they only flip state and hand work to other threads.
    /// </summary>
    private static void Raise(Action handler)
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
