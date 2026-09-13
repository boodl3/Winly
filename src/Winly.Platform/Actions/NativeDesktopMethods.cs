using System.Runtime.InteropServices;

namespace Winly.Platform.Actions;

/// <summary>
/// The user32/kernel32/powrprof surface the desktop actions need — source-generated, no interop
/// package (Constitution Principle III).
/// </summary>
internal static partial class NativeDesktopMethods
{
    public const int ShowMinimized = 6;    // SW_MINIMIZE
    public const int ShowMaximized = 3;    // SW_MAXIMIZE
    public const int ShowRestore = 9;      // SW_RESTORE
    public const uint CloseMessage = 0x0010; // WM_CLOSE
    public const uint NullMessage = 0x0000;  // WM_NULL — does nothing; only the reply matters
    public const uint AbortIfHung = 0x0002;  // SMTO_ABORTIFHUNG
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const uint SettingChangeMessage = 0x001A; // WM_SETTINGCHANGE
    public const nint BroadcastWindow = 0xFFFF;      // HWND_BROADCAST

    public const uint KeyEventKeyUp = 0x0002;
    public const uint KeyEventUnicode = 0x0004;
    public const uint InputKeyboard = 1;

    public const ushort VirtualKeyControl = 0x11;
    public const ushort VirtualKeyC = 0x43;
    public const ushort VirtualKeyD = 0x44;
    public const ushort VirtualKeyLeftWindows = 0x5B;

    public const uint MonitorDefaultToNearest = 2;
    public const uint SetWindowPositionShowWindow = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    /// <summary>The INPUT union, keyboard arm only — mouse and hardware arms are unused and padded.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SyntheticInput
    {
        public uint Type;
        public KeyboardInput Keyboard;
        private readonly ulong _mousePadding0;
        private readonly ulong _mousePadding1;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint window);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "SendNotifyMessageW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SendNotifyMessage(nint window, uint message, nuint wParam, string lParam);

    /// <summary>
    /// Sends a message and gives up rather than blocking when the target is not pumping messages.
    /// Returns zero on timeout, which is what separates "a window exists" from "a window is
    /// answering" — a splash screen satisfies the first and not the second.
    /// </summary>
    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW")]
    public static partial nint SendMessageTimeout(
        nint window,
        uint message,
        nuint wParam,
        nint lParam,
        uint flags,
        uint timeoutMilliseconds,
        out nuint result);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(nint window);

    /// <summary>
    /// Joins two threads' input queues, which is the only supported way to get foreground rights
    /// from a process the user has not just clicked on. Winly is a tray app with no window of its
    /// own, so Windows refuses its bare SetForegroundWindow and flashes the taskbar button instead.
    /// </summary>
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint attachTo, uint attachFrom, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint window, out uint processId);

    /// <summary>
    /// Opened purely to find out whether it can be opened. A window belonging to an elevated
    /// process refuses PROCESS_QUERY_LIMITED_INFORMATION from an unelevated caller, and that is
    /// the only cheap signal that synthetic input will be dropped by UIPI.
    /// </summary>
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint monitor, ref MonitorInfo info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll")]
    public static partial uint SendInput(uint count, [In] SyntheticInput[] inputs, int size);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LockWorkStation();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(nint owner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport("user32.dll")]
    public static partial nint GetClipboardData(uint format);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalLock(nint handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint handle);

    [LibraryImport("powrprof.dll", EntryPoint = "SetSuspendState")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetSuspendState(
        [MarshalAs(UnmanagedType.Bool)] bool hibernate,
        [MarshalAs(UnmanagedType.Bool)] bool force,
        [MarshalAs(UnmanagedType.Bool)] bool wakeupEventsDisabled);
}
