namespace Winly.Core.Pointing;

/// <summary>
/// A pointing target resolved to the desktop. <see cref="MonitorDipX"/>/<see cref="MonitorDipY"/>
/// are device-independent pixels relative to that monitor's origin, which is the coordinate
/// space of the overlay window covering it (research.md §4).
/// </summary>
public sealed record DesktopLocation(
    string MonitorId,
    double DesktopPhysicalX,
    double DesktopPhysicalY,
    double MonitorDipX,
    double MonitorDipY,
    string Label);
