namespace Winly.Core.Pointing;

/// <summary>A position in one display's capture-pixel space and a short label for what is there.</summary>
public sealed record PointingTarget(string MonitorId, int XInCapturePx, int YInCapturePx, string Label)
{
    /// <summary>Constrains the position into <c>[0, WidthPx) x [0, HeightPx)</c> of the capture (FR-020).</summary>
    public PointingTarget ClampTo(DisplayCapture capture) => this with
    {
        XInCapturePx = Math.Clamp(XInCapturePx, 0, Math.Max(0, capture.WidthPx - 1)),
        YInCapturePx = Math.Clamp(YInCapturePx, 0, Math.Max(0, capture.HeightPx - 1)),
    };
}
