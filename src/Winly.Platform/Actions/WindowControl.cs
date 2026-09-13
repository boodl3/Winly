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

    /// <summary>
    /// Brings a window back and puts it in front, which is two separate problems.
    ///
    /// Un-minimising is easy. Being allowed to take the foreground is not: Windows grants that only
    /// to the process that owns the current foreground window or has just received input, and Winly
    /// is a tray app whose activation key is read through a global hook — the keystroke goes to
    /// whatever the user was in, not to Winly. A bare SetForegroundWindow is therefore refused, and
    /// a refusal does not look like one: the window is restored and its taskbar button flashes,
    /// which reads as "open did nothing".
    ///
    /// So the result is checked rather than assumed, and on a refusal the two input queues are
    /// joined for the length of one call, which is the documented way to ask on equal terms.
    /// </summary>
    private static void Focus(nint window)
    {
        if (IsIconic(window))
        {
            ShowWindow(window, ShowRestore);
        }

        if (SetForegroundWindow(window) && GetForegroundWindow() == window)
        {
            return;
        }

        var ours = GetCurrentThreadId();
        var theirs = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        if (theirs == 0 || theirs == ours || !AttachThreadInput(ours, theirs, true))
        {
            BringWindowToTop(window);
            return;
        }

        try
        {
            BringWindowToTop(window);
            SetForegroundWindow(window);
        }
        finally
        {
            // Never left attached: two processes sharing an input queue is not a state to live in.
            AttachThreadInput(ours, theirs, false);
        }
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
