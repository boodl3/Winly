namespace Winly.Core.Pointing;

/// <summary>
/// The sole place capture-pixel coordinates become desktop coordinates, as three separately
/// testable steps (research.md §5).
/// </summary>
public static class CaptureToDesktopCoordinateMapper
{
    public static DesktopLocation Map(PointingTarget target, DisplayCapture capture)
    {
        var clamped = target.ClampTo(capture);
        var monitorPixel = CapturePixelToMonitorPhysicalPixel(clamped, capture);
        var desktopPixel = MonitorPhysicalPixelToDesktopPhysicalPixel(monitorPixel, capture.Monitor);
        var deviceIndependent = DesktopPhysicalPixelToMonitorDeviceIndependentPixel(desktopPixel, capture.Monitor);
        return new DesktopLocation(
            capture.MonitorId,
            desktopPixel.X,
            desktopPixel.Y,
            deviceIndependent.X,
            deviceIndependent.Y,
            clamped.Label);
    }

    /// <summary>Step 1: undo the capture downscale (research.md §2) to reach the monitor's own physical pixels.</summary>
    public static (double X, double Y) CapturePixelToMonitorPhysicalPixel(PointingTarget target, DisplayCapture capture) =>
        (target.XInCapturePx * (double)capture.Monitor.WidthPx / capture.WidthPx,
         target.YInCapturePx * (double)capture.Monitor.HeightPx / capture.HeightPx);

    /// <summary>Step 2: add the monitor's virtual-screen origin.</summary>
    public static (double X, double Y) MonitorPhysicalPixelToDesktopPhysicalPixel((double X, double Y) monitorPixel, MonitorGeometry monitor) =>
        (monitorPixel.X + monitor.OriginXPx, monitorPixel.Y + monitor.OriginYPx);

    /// <summary>
    /// Step 3: divide by the monitor's DPI scale. WPF has no single DIP space across mixed-DPI
    /// monitors, and each overlay window covers exactly one monitor, so the result is relative
    /// to that monitor's origin.
    /// </summary>
    public static (double X, double Y) DesktopPhysicalPixelToMonitorDeviceIndependentPixel((double X, double Y) desktopPixel, MonitorGeometry monitor) =>
        ((desktopPixel.X - monitor.OriginXPx) / monitor.DpiScale,
         (desktopPixel.Y - monitor.OriginYPx) / monitor.DpiScale);
}
