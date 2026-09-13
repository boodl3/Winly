using Winly.Core.Companion;

namespace Winly.Core.Tests.Companion;

public class SentenceAccumulatorTests
{
    [Fact]
    public void ReleasesASentenceOnlyOnceItIsTerminatedAndFollowedByWhitespace()
    {
        var accumulator = new SentenceAccumulator();

        Assert.Empty(accumulator.Append("That button saves"));
        Assert.Empty(accumulator.Append(" your file."));
        Assert.Equal(["That button saves your file."], accumulator.Append(" Then"));
    }

    [Fact]
    public void SplitsWordsArrivingAcrossDeltaBoundaries()
    {
        var accumulator = new SentenceAccumulator();

        Assert.Empty(accumulator.Append("This loo"));
        Assert.Empty(accumulator.Append("ks like a sett"));
        Assert.Equal(["This looks like a settings panel."], accumulator.Append("ings panel. "));
    }

    [Fact]
    public void ReleasesSeveralSentencesFromOneDelta()
    {
        var accumulator = new SentenceAccumulator();

        Assert.Equal(["First one.", "Second one!"], accumulator.Append("First one. Second one! Third"));
        Assert.Equal("Third", accumulator.Flush());
    }

    [Fact]
    public void KeepsDecimalNumbersIntact()
    {
        var accumulator = new SentenceAccumulator();

        Assert.Empty(accumulator.Append("It costs 3.5"));
        Assert.Equal(["It costs 3.5 credits."], accumulator.Append(" credits. "));
    }

    [Fact]
    public void KeepsClosingPunctuationWithItsSentence()
    {
        var accumulator = new SentenceAccumulator();

        Assert.Equal(["He said \"go.\""], accumulator.Append("He said \"go.\" Next"));
    }

    [Fact]
    public void FlushReturnsUnterminatedTextAndEmptiesTheBuffer()
    {
        var accumulator = new SentenceAccumulator();
        accumulator.Append("No terminator here");

        Assert.Equal("No terminator here", accumulator.Flush());
        Assert.Equal(string.Empty, accumulator.Flush());
    }
}
