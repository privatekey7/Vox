using System.Runtime.InteropServices;

namespace Vox.App.Services;

/// <summary>Win32 calls Vox needs: the low-level keyboard hook, synthetic input and window styles.</summary>
internal static partial class Native
{
    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const int WM_QUIT = 0x0012;
    public const int WM_APP = 0x8000;
    public const nint VoxInputMarker = 0x564F58; // "VOX": tags the keys Vox sends itself
    public const uint AltGrFakeCtrlScanCode = 0x21D; // the left Ctrl Windows adds to each AltGr press

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const ushort VK_SPACE = 0x20;
    public const ushort VK_SHIFT = 0x10;
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_MENU = 0x12;
    public const ushort VK_LWIN = 0x5B;
    public const ushort VK_RWIN = 0x5C;
    public const ushort VK_S = 0x53;
    public const ushort VK_V = 0x56;
    public const ushort VK_LMENU = 0xA4;
    public const ushort VK_RMENU = 0xA5;
    public const ushort VK_MASK = 0xE8; // unassigned: tells Windows an Alt press was not a lone tap
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const uint KEYEVENTF_UNICODE = 0x0004;
    public const uint INPUT_KEYBOARD = 1;

    public delegate nint LowLevelKeyboardProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nint dwExtraInfo;
    }

    // INPUT is a union; KEYBDINPUT is padded to the size of MOUSEINPUT (the largest member).
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    public struct INPUT
    {
        [FieldOffset(0)] public uint type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    public static partial nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    /// <summary>Is the key physically down now (works inside the low-level hook)?</summary>
    public static bool IsDown(int vk) => GetAsyncKeyState(vk) < 0;

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? lpModuleName);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint idThread, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLastInputInfo(ref LASTINPUTINFO plii);

    /// <summary>Time since the last keyboard or mouse input anywhere in the session.</summary>
    public static uint MillisecondsSinceLastInput()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.dwTime) : uint.MaxValue;
    }

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint processHandle, uint desiredAccess, out nint tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint tokenHandle, int tokenInformationClass, out int tokenInformation, int tokenInformationLength, out int returnLength);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    /// <summary>
    /// The app in front (process name, for the log) and whether it runs elevated. A process whose
    /// token Vox may not even open is counted as elevated.
    /// </summary>
    public static (string Name, bool Elevated) ForegroundApp()
    {
        const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        const uint TOKEN_QUERY = 0x0008;
        const int TokenElevation = 20;
        const int ERROR_ACCESS_DENIED = 5;

        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        string name;
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            name = process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return ("?", false);
        }

        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == 0)
        {
            return (name, false);
        }

        try
        {
            if (!OpenProcessToken(handle, TOKEN_QUERY, out var token))
            {
                return (name, Marshal.GetLastPInvokeError() == ERROR_ACCESS_DENIED);
            }

            try
            {
                return (name, GetTokenInformation(token, TokenElevation, out var elevated, sizeof(int), out _) && elevated != 0);
            }
            finally
            {
                CloseHandle(token);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = VoxInputMarker },
    };

    /// <summary>One UTF-16 unit typed as a character, whatever the keyboard layout (a surrogate pair is two).</summary>
    public static INPUT Char(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0), dwExtraInfo = VoxInputMarker },
    };

    [LibraryImport("user32.dll")]
    public static partial nint GetOpenClipboardWindow();

    /// <summary>The process that owns a window, for the log; "?" if it is gone or there is no window.</summary>
    public static string WindowProcessName(nint hwnd)
    {
        if (hwnd == 0 || GetWindowThreadProcessId(hwnd, out var pid) == 0)
        {
            return "?";
        }

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            return "?";
        }
    }

    // Reading a focused control's text without UI Automation (FieldWatcher, Notepad++/Scintilla).
    public const uint WM_GETTEXT = 0x000D;
    public const uint WM_GETTEXTLENGTH = 0x000E;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const int GWL_STYLE = -16;
    public const int ES_PASSWORD = 0x0020;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public nint hwndActive, hwndFocus, hwndCapture, hwndMenuOwner, hwndMoveSize, hwndCaret;
        public RECT rcCaret;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO pgui);

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    public static partial nint SendMessageTimeout(nint hWnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    public static unsafe partial int GetClassName(nint hWnd, char* lpClassName, int nMaxCount);

    /// <summary>The focused control of the foreground window (other process included), or 0.</summary>
    public static nint FocusedControl()
    {
        var thread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var info = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return thread != 0 && GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : 0;
    }

    public static unsafe string ClassName(nint hwnd)
    {
        var buffer = stackalloc char[256];
        var length = GetClassName(hwnd, buffer, 256);
        return length > 0 ? new string(buffer, 0, length) : "?";
    }

    /// <summary>WM_GETTEXTLENGTH with a timeout (a hung app answers -1).</summary>
    public static int WindowTextLength(nint hwnd, uint timeoutMs) =>
        SendMessageTimeout(hwnd, WM_GETTEXTLENGTH, 0, 0, SMTO_ABORTIFHUNG, timeoutMs, out var length) == 0 ? -1 : (int)length;

    /// <summary>WM_GETTEXT with a timeout; Windows copies the text across processes.</summary>
    public static unsafe string? WindowText(nint hwnd, int length, uint timeoutMs)
    {
        var buffer = new char[length + 1];
        fixed (char* p = buffer)
        {
            if (SendMessageTimeout(hwnd, WM_GETTEXT, length + 1, (nint)p, SMTO_ABORTIFHUNG, timeoutMs, out var copied) == 0)
            {
                return null;
            }

            return new string(p, 0, (int)Math.Min(copied, length));
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool EnumWindows(delegate* unmanaged<nint, nint, int> callback, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    public static unsafe partial int GetWindowText(nint hWnd, char* lpString, int nMaxCount);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(nint hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    public const int DWMWA_CLOAKED = 14;
}
