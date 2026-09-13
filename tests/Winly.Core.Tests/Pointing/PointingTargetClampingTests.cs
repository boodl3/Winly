using Winly.Core.Pointing;

namespace Winly.Core.Tests.Pointing;

public class PointingTargetClampingTests
{
    private static readonly DisplayCapture Capture = new("m", [], 1568, 882, true, new MonitorGeometry(0, 0, 1568, 882, 1.0));

    [Theory]
    [InlineData(-50, 2000, 0, 881)]
    [InlineData(99999, -1, 1567, 0)]
    [InlineData(1568, 882, 1567, 881)]
    [InlineData(10, 20, 10, 20)]
    public void OutOfBoundsPositionsAreConstrainedIntoTheDisplay(int x, int y, int expectedX, int expectedY)
    {
        var clamped = new PointingTarget("m", x, y, "thing").ClampTo(Capture);

        Assert.Equal(expectedX, clamped.XInCapturePx);
        Assert.Equal(expectedY, clamped.YInCapturePx);
        Assert.Equal("thing", clamped.Label);
    }

    [Fact]
    public void MappingClampsBeforeConverting()
    {
        var location = CaptureToDesktopCoordinateMapper.Map(new PointingTarget("m", -400, 5000, "off-screen"), Capture);

        Assert.Equal(0, location.MonitorDipX);
        Assert.Equal(881, location.MonitorDipY);
    }
}
