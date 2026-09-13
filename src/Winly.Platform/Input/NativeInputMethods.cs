using System.Runtime.InteropServices;

namespace Winly.Platform.Input;

/// <summary>The user32/kernel32 surface the keyboard hook needs — source-generated, no interop package (Constitution Principle III).</summary>
internal static unsafe partial class NativeInputMethods
{
    public const int LowLevelKeyboardHook = 13; // WH_KEYBOARD_LL
    public const int KeyDownMessage = 0x0100;    // WM_KEYDOWN
    public const int KeyUpMessage = 0x0101;      // WM_KEYUP
    public const int SystemKeyDownMessage = 0x0104; // WM_SYSKEYDOWN
    public const int SystemKeyUpMessage = 0x0105;   // WM_SYSKEYUP
    public const uint QuitMessage = 0x0012;      // WM_QUIT

    [StructLayout(LayoutKind.Sequential)]
    public struct KeyboardLowLevelHookData
    {
        public uint VirtualKeyCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeMessage
    {
        public nint WindowHandle;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetWindowsHookExW(int hookType, delegate* unmanaged[Stdcall]<int, nint, nint, nint> hookProcedure, nint moduleHandle, uint threadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hookHandle);

    [LibraryImport("user32.dll")]
    public static partial nint CallNextHookEx(nint hookHandle, int code, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(out NativeMessage message, nint windowHandle, uint filterMinimum, uint filterMaximum);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint threadId, uint message, nuint wParam, nint lParam);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? moduleName);
}
