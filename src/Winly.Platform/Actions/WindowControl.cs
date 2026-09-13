using System.Diagnostics;
using Serilog;
using Winly.Core.Actions;
using static Winly.Platform.Actions.NativeDesktopMethods;

namespace Winly.Platform.Actions;

/// <summary>Focus, size and place other applications' windows. Nothing here creates a window.</summary>
internal static class WindowControl
{
    public static void Apply(string appName, string command)
    {
        using var process = AppMatcher.FindWindowed(appName)
            ?? throw new DesktopActionFailedException($"I couldn't find an open {appName} window.");
        var window = process.MainWindowHandle;

        switch (command.ToLowerInvariant())
        {
            case "focus" or "switch" or "activate":
                Focus(window);
                break;
            case "minimize" or "minimise" or "hide":
                ShowWindow(window, ShowMinimized);
                break;
            case "maximize" or "maximise" or "fullscreen":
                ShowWindow(window, ShowMaximized);
                break;
            case "restore" or "unminimize":
                ShowWindow(window, ShowRestore);
                break;
            case "close" or "quit":
                // WM_CLOSE, not TerminateProcess: the app gets to save and to ask the user.
                PostMessage(window, CloseMessage, 0, 0);
                break;
            case "left":
                SnapTo(window, left: true);
                break;
            case "right":
                SnapTo(window, left: false);
                break;
            default:
                throw new DesktopActionFailedException($"I don't know how to {command} a window.");
        }

        Log.Information("Window {Command} on {App}", command, appName);
    }

    private static void Focus(nint window)
    {
        if (IsIconic(window))
        {
            ShowWindow(window, ShowRestore);
        }

        // Windows only honours this from a process with recent input. Winly always has some: the
        // user just held the activation key. If it is refused the window still comes back restored.
        SetForegroundWindow(window);
    }

    private static void SnapTo(nint window, bool left)
    {
        var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            throw new DesktopActionFailedException("I couldn't work out which screen that window is on.");
        }

        // The work area, not the monitor: this leaves the taskbar where it is.
        var area = info.WorkArea;
        var halfWidth = area.Width / 2;
        ShowWindow(window, ShowRestore);
        SetWindowPos(window, 0, left ? area.Left : area.Left + halfWidth, area.Top, halfWidth, area.Height, SetWindowPositionShowWindow);
    }

}
