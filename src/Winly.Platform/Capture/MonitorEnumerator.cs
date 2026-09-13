using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Winly.Core.Pointing;

namespace Winly.Platform.Capture;

public sealed record MonitorDescription(string MonitorId, nint Handle, MonitorGeometry Geometry, bool ContainsCursor);

/// <summary>Lists connected monitors with per-monitor DPI and flags the one under the cursor (FR-008).</summary>
public static unsafe partial class MonitorEnumerator
{
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0; // MDT_EFFECTIVE_DPI

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
        public fixed char DeviceName[32];
    }

    public static IReadOnlyList<MonitorDescription> Enumerate()
    {
        var handles = new List<nint>();
        var handlesHandle = GCHandle.Alloc(handles);
        try
        {
            if (!EnumDisplayMonitors(0, null, &CollectMonitor, GCHandle.ToIntPtr(handlesHandle)))
            {
                throw new Win32Exception();
            }
        }
        finally
        {
            handlesHandle.Free();
        }

        GetCursorPos(out var cursor);
        var cursorMonitor = MonitorFromPoint(cursor, MonitorDefaultToNearest);
        return handles.Select(handle => Describe(handle, handle == cursorMonitor)).ToArray();
    }

    /// <summary>Cursor position in physical virtual-screen pixels. Unlike <see cref="Enumerate"/> this is
    /// a single syscall, so it is cheap enough to call once per rendered frame. False on the secure
    /// desktop, where the caller should keep its last known position.</summary>
    public static bool TryGetCursorPositionPx(out int x, out int y)
    {
        var ok = GetCursorPos(out var point);
        (x, y) = (point.X, point.Y);
        return ok;
    }

    private static MonitorDescription Describe(nint handle, bool containsCursor)
    {
        var info = new MonitorInfoEx { Size = (uint)sizeof(MonitorInfoEx) };
        if (!GetMonitorInfoW(handle, &info))
        {
            throw new Win32Exception();
        }

        uint dpiX = 96, dpiY = 96;
        if (GetDpiForMonitor(handle, EffectiveDpi, out var effectiveDpiX, out var effectiveDpiY) == 0)
        {
            (dpiX, dpiY) = (effectiveDpiX, effectiveDpiY);
        }

        _ = dpiY;
        var geometry = new MonitorGeometry(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right - info.Monitor.Left,
            info.Monitor.Bottom - info.Monitor.Top,
            dpiX / 96.0);
        return new MonitorDescription(new string(info.DeviceName), handle, geometry, containsCursor);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int CollectMonitor(nint monitor, nint deviceContext, NativeRect* clip, nint data)
    {
        ((List<nint>)GCHandle.FromIntPtr(data).Target!).Add(monitor);
        return 1;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumDisplayMonitors(nint deviceContext, NativeRect* clip, delegate* unmanaged[Stdcall]<nint, nint, NativeRect*, nint, int> callback, nint data);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(nint monitor, MonitorInfoEx* info);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out NativePoint point);

    [LibraryImport("user32.dll")]
    private static partial nint MonitorFromPoint(NativePoint point, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
