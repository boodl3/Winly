namespace Winly.Core.Pointing;

/// <summary>
/// One display's JPEG snapshot. <see cref="WidthPx"/>/<see cref="HeightPx"/> are the
/// (downscaled) image dimensions; <see cref="Monitor"/> describes the physical display so a
/// position in the image can be mapped back to the desktop. Held only for the request (FR-029).
/// </summary>
public sealed record DisplayCapture(
    string MonitorId,
    byte[] ImageBytes,
    int WidthPx,
    int HeightPx,
    bool IsPrimary,
    MonitorGeometry Monitor);
