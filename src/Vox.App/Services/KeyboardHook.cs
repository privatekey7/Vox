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
    // Windows removes a hook that timed out without telling anyone. If no key reached the hook for
    // a while although the user is active, log it and install the hook again.
    private static readonly TimeSpan WatchdogEvery = TimeSpan.FromMinutes(1);
    private const long SilentForMs = 5 * 60 * 1000;
    private const uint UserActiveWithinMs = 60 * 1000;

    private readonly Thread _thread;
    private readonly Thread _dispatcher;
    private readonly BlockingCollection<Action> _events = [];
    private readonly Native.LowLevelKeyboardProc _proc; // kept alive: the OS holds only a pointer
    private uint _threadId;
    private nint _hook;
    private volatile int _hotkeyVk;
    private bool _hotkeyDown;
    private bool _chord;
    private long _lastEventTick = Environment.TickCount64;
    private readonly Timer _watchdog;

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
        _watchdog = new Timer(_ => CheckAlive(), null, WatchdogEvery, WatchdogEvery);
    }

    /// <summary>The hotkey went down (repeats while held are filtered out).</summary>
    public event Action? Pressed;

    /// <summary>The hotkey went up. The flag is true when another key was pressed meanwhile (Ctrl+C…).</summary>
    public event Action<bool>? Released;

    /// <summary>The user pressed Ctrl+S ("remember this fix"). The key still reaches the app: an editor saves as usual.</summary>
    public event Action? SavePressed;

    public void SetHotkey(int virtualKey) => _hotkeyVk = virtualKey;

    public void Dispose()
    {
        _watchdog.Dispose();
        Native.PostThreadMessageW(_threadId, Native.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _events.CompleteAdding();
    }

    private void Run(ManualResetEventSlim started)
    {
        _threadId = Native.GetCurrentThreadId();
        Install();
        started.Set();
        while (Native.GetMessageW(out var msg, 0, 0, 0) > 0)
        {
            // Low-level hooks are dispatched by this message loop; the watchdog may ask to reinstall.
            if (msg.message == Native.WM_APP)
            {
                Native.UnhookWindowsHookEx(_hook);
                Install();
            }
        }

        Native.UnhookWindowsHookEx(_hook);
    }

    private void Install()
    {
        _hook = Native.SetWindowsHookExW(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandleW(null), 0);
        Log.Info(_hook == 0 ? $"[HOOK] SetWindowsHookEx failed: {Marshal.GetLastPInvokeError()}" : "[HOOK] Keyboard hook installed");
    }

    private void CheckAlive()
    {
        var now = Environment.TickCount64;
        if (now - Volatile.Read(ref _lastEventTick) > SilentForMs && Native.MillisecondsSinceLastInput() < UserActiveWithinMs)
        {
            Log.Info("[HOOK] No keys for 5 min while the user is active (or only the mouse is used) — reinstalling the hook");
            Volatile.Write(ref _lastEventTick, now);
            Native.PostThreadMessageW(_threadId, Native.WM_APP, 0, 0);
        }
    }

    private static long _lastUserKeyTick;

    /// <summary>
    /// When the user last pressed a non-modifier key (Enter, arrows, typing…): after that, the end of
    /// Vox's previous paste no longer tells what is before the cursor (CursorContext).
    /// </summary>
    public static long LastUserKeyTick => Volatile.Read(ref _lastUserKeyTick);

    // Last editing key of the user per foreground window: self-learning only learns what the user typed there.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, long> LastKeyByWindow = new();

    /// <summary>
    /// The user pressed an editing key (<see cref="Hotkeys.IsEditing"/>) in this foreground window: self-learning
    /// looks at the field then (<see cref="FieldWatcher.Edited"/>).
    /// </summary>
    public event Action<nint>? Edited;

    /// <summary>Did the user press an editing key (not Vox's own Ctrl+V) in this window after <paramref name="sinceTick"/>?</summary>
    public static bool UserTypedIn(nint window, long sinceTick) =>
        LastKeyByWindow.TryGetValue(window, out var tick) && tick > sinceTick;

    private static bool IsModifier(uint vk) => vk is >= 0x10 and <= 0x12 or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C or 0x14;

    private nint Callback(int nCode, nint wParam, nint lParam)
    {
        Volatile.Write(ref _lastEventTick, Environment.TickCount64);
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<Native.KBDLLHOOKSTRUCT>(lParam);
            if (info.dwExtraInfo != Native.VoxInputMarker)
            {
                var down = wParam is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN;
                var up = wParam is Native.WM_KEYUP or Native.WM_SYSKEYUP;
                var save = down && info.vkCode == Native.VK_S && Hotkeys.IsSave((int)info.vkCode, Native.IsDown(Native.VK_CONTROL),
                    Native.IsDown(Native.VK_SHIFT), Native.IsDown(Native.VK_MENU), Native.IsDown(Native.VK_LWIN) || Native.IsDown(Native.VK_RWIN));
                if (save)
                {
                    // Not typing: autocorrect followed by Ctrl+S must not look like the user's own fix.
                    Raise(() => SavePressed?.Invoke());
                }
                else if (down && info.vkCode != _hotkeyVk && !IsModifier(info.vkCode))
                {
                    var now = Environment.TickCount64;
                    Volatile.Write(ref _lastUserKeyTick, now);
                    if (Hotkeys.IsEditing((int)info.vkCode, Native.IsDown(Native.VK_CONTROL), Native.IsDown(Native.VK_SHIFT),
                            Native.IsDown(Native.VK_MENU), Native.IsDown(Native.VK_LWIN) || Native.IsDown(Native.VK_RWIN)))
                    {
                        var window = Native.GetForegroundWindow();
                        LastKeyByWindow[window] = now;
                        Raise(() => Edited?.Invoke(window));
                    }
                }

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
