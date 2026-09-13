using System.Runtime.InteropServices;
using Winly.Core.Pointing;

namespace Winly.Platform.Overlay;

/// <summary>
/// Makes an overlay window click-through, non-activating and absent from Alt+Tab, and pins it
/// over one monitor in physical pixels (FR-023, FR-024, research.md §4).
/// </summary>
public static partial class OverlayWindowStyleApplier
{
    private const int ExtendedStyleIndex = -20;          // GWL_EXSTYLE
    private const nint TransparentStyle = 0x00000020;    // WS_EX_TRANSPARENT: mouse input passes through
    private const nint ToolWindowStyle = 0x00000080;     // WS_EX_TOOLWINDOW: hidden from Alt+Tab
    private const nint LayeredStyle = 0x00080000;        // WS_EX_LAYERED
    private const nint NoActivateStyle = 0x08000000;     // WS_EX_NOACTIVATE: never takes focus
    private const nint TopmostInsertAfter = -1;          // HWND_TOPMOST
    private const uint NoActivateFlag = 0x0010;          // SWP_NOACTIVATE

    public static void MakeClickThroughAndNonActivating(nint windowHandle)
    {
        var style = GetWindowLongPtrW(windowHandle, ExtendedStyleIndex);
        SetWindowLongPtrW(windowHandle, ExtendedStyleIndex, style | NoActivateStyle | LayeredStyle | TransparentStyle | ToolWindowStyle);
    }

    public static void PlaceOverMonitor(nint windowHandle, MonitorGeometry monitor) =>
        SetWindowPos(windowHandle, TopmostInsertAfter, monitor.OriginXPx, monitor.OriginYPx, monitor.WidthPx, monitor.HeightPx, NoActivateFlag);

    [LibraryImport("user32.dll")]
    private static partial nint GetWindowLongPtrW(nint windowHandle, int index);

    [LibraryImport("user32.dll")]
    private static partial nint SetWindowLongPtrW(nint windowHandle, int index, nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint windowHandle, nint insertAfter, int x, int y, int width, int height, uint flags);
}
