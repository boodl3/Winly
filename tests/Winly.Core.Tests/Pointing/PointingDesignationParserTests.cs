using Winly.Core.Pointing;

namespace Winly.Core.Tests.Pointing;

public class PointingDesignationParserTests
{
    [Fact]
    public void StructuredTargetPassesThroughAndResidualTagIsStripped()
    {
        var structured = new PointingTarget("m1", 10, 20, "Save");

        var (spoken, target) = PointingDesignationParser.Parse(
            "It's the Save button.\n@@POINT {\"monitorId\":\"m1\",\"x\":10,\"y\":20,\"label\":\"Save\"}@@", structured);

        Assert.Equal("It's the Save button.", spoken);
        Assert.Same(structured, target);
    }

    [Fact]
    public void NoDesignationLeavesNarrationUntouchedAndNoTarget()
    {
        var (spoken, target) = PointingDesignationParser.Parse("Nothing on screen relates to that.", null);

        Assert.Equal("Nothing on screen relates to that.", spoken);
        Assert.Null(target);
    }

    [Fact]
    public void NoPointTagIsStrippedAndYieldsNoTarget()
    {
        var (spoken, target) = PointingDesignationParser.Parse("Just an answer. @@NOPOINT@@", null);

        Assert.Equal("Just an answer.", spoken);
        Assert.Null(target);
    }

    [Fact]
    public void ResidualTagIsRecoveredWhenNoStructuredTargetArrived()
    {
        var (spoken, target) = PointingDesignationParser.Parse(
            "Look here. @@POINT {\"monitorId\":\"m2\",\"x\":300,\"y\":400,\"label\":\"Font size\"}@@", null);

        Assert.Equal("Look here.", spoken);
        Assert.Equal(new PointingTarget("m2", 300, 400, "Font size"), target);
    }

    [Fact]
    public void MalformedTagIsDroppedWithoutATarget()
    {
        var (spoken, target) = PointingDesignationParser.Parse("Look here. @@POINT {not json}@@", null);

        Assert.Equal("Look here.", spoken);
        Assert.Null(target);
    }
}
