namespace Winly.Core.Pointing;

/// <summary>A monitor's rectangle in physical virtual-screen pixels plus its DPI scale (1.0 = 96 DPI).</summary>
public sealed record MonitorGeometry(int OriginXPx, int OriginYPx, int WidthPx, int HeightPx, double DpiScale);
