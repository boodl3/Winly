using Winly.Core.Pointing;

namespace Winly.Core.Tests.Pointing;

public class CaptureToDesktopCoordinateMapperTests
{
    // A 4K primary at 200 % and a 1440p secondary at 150 % to its left (negative origin), both captured at ≤ 1568 px.
    private static readonly DisplayCapture Primary4K = new("primary", [], 1568, 882, true, new MonitorGeometry(0, 0, 3840, 2160, 2.0));
    private static readonly DisplayCapture SecondaryLeft = new("left", [], 1568, 882, false, new MonitorGeometry(-2560, 0, 2560, 1440, 1.5));
    private static readonly DisplayCapture Offset125 = new("right", [], 1568, 882, false, new MonitorGeometry(3840, 200, 1920, 1080, 1.25));

    [Fact]
    public void Step1UndoesTheCaptureDownscale()
    {
        var (x, y) = CaptureToDesktopCoordinateMapper.CapturePixelToMonitorPhysicalPixel(new PointingTarget("primary", 784, 441, ""), Primary4K);

        Assert.Equal(1920, x, precision: 6);
        Assert.Equal(1080, y, precision: 6);
    }

    [Fact]
    public void Step2AddsTheMonitorOrigin()
    {
        var (x, y) = CaptureToDesktopCoordinateMapper.MonitorPhysicalPixelToDesktopPhysicalPixel((1280, 720), SecondaryLeft.Monitor);

        Assert.Equal(-1280, x);
        Assert.Equal(720, y);
    }

    [Fact]
    public void Step3DividesByTheMonitorScaleRelativeToItsOrigin()
    {
        var (x, y) = CaptureToDesktopCoordinateMapper.DesktopPhysicalPixelToMonitorDeviceIndependentPixel((-1280, 720), SecondaryLeft.Monitor);

        Assert.Equal(853.3333, x, precision: 3);
        Assert.Equal(480, y, precision: 6);
    }

    [Fact]
    public void MixedScaleFactorsMapTheCentreOfEachDisplayCorrectly()
    {
        var onPrimary = CaptureToDesktopCoordinateMapper.Map(new PointingTarget("primary", 784, 441, "centre"), Primary4K);
        var onSecondary = CaptureToDesktopCoordinateMapper.Map(new PointingTarget("left", 784, 441, "centre"), SecondaryLeft);

        Assert.Equal((1920, 1080), (onPrimary.DesktopPhysicalX, onPrimary.DesktopPhysicalY));
        Assert.Equal((960, 540), (onPrimary.MonitorDipX, onPrimary.MonitorDipY));

        Assert.Equal((-1280, 720), (onSecondary.DesktopPhysicalX, onSecondary.DesktopPhysicalY));
        Assert.Equal(853.3333, onSecondary.MonitorDipX, precision: 3);
        Assert.Equal(480, onSecondary.MonitorDipY, precision: 6);
    }

    [Fact]
    public void OffsetMonitorWithFractionalScaleMapsCornersInsideItself()
    {
        var topLeft = CaptureToDesktopCoordinateMapper.Map(new PointingTarget("right", 0, 0, ""), Offset125);
        var bottomRight = CaptureToDesktopCoordinateMapper.Map(new PointingTarget("right", 1567, 881, ""), Offset125);

        Assert.Equal((3840, 200), (topLeft.DesktopPhysicalX, topLeft.DesktopPhysicalY));
        Assert.Equal((0, 0), (topLeft.MonitorDipX, topLeft.MonitorDipY));
        Assert.InRange(bottomRight.MonitorDipX, 0, 1920 / 1.25);
        Assert.InRange(bottomRight.MonitorDipY, 0, 1080 / 1.25);
        Assert.Equal("right", bottomRight.MonitorId);
    }
}
