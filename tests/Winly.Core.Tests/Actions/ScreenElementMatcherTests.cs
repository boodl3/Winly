using Winly.Core.Actions;

namespace Winly.Core.Tests.Actions;

public class ScreenElementMatcherTests
{
    /// <summary>
    /// The bug this verb was added for. A short navigation chip shares the one distinctive word
    /// with the request, so any scoring that penalises spare words hands it the click and the
    /// actual video is never pressed.
    /// </summary>
    [Fact]
    public void PrefersTheLongVideoTitleOverAShortChipSharingOneWord()
    {
        string[] labels = ["Procreate", "Search", "Procreate Tutorial for Beginners — Full Walkthrough"];

        Assert.Equal(2, ScreenElementMatcher.Choose("Procreate Tutorial for Beginners — Full Walkthrough", labels));
    }

    [Fact]
    public void MatchesThroughPunctuationAndCasing()
    {
        string[] labels = ["Sign in", "play bohemian rhapsody"];

        Assert.Equal(1, ScreenElementMatcher.Choose("Play Bohemian Rhapsody!", labels));
    }

    [Fact]
    public void PrefersTheClosestOfSeveralOverlappingLabels()
    {
        string[] labels = ["Save as draft", "Save", "Save and close"];

        Assert.Equal(1, ScreenElementMatcher.Choose("Save", labels));
    }

    /// <summary>
    /// No fallback to "the first plausible thing": pressing the wrong control on someone's screen
    /// is not recoverable the way playing the wrong song is.
    /// </summary>
    [Fact]
    public void ChoosesNothingWhenNoLabelSharesAWord()
    {
        string[] labels = ["Sign in", "Settings", "Home"];

        Assert.Equal(-1, ScreenElementMatcher.Choose("Procreate tutorial", labels));
    }

    [Fact]
    public void ChoosesNothingWhenThereIsNothingToChooseFrom() =>
        Assert.Equal(-1, ScreenElementMatcher.Choose("Accept", []));

    /// <summary>"Click the play button" must still be able to find a control named "Play".</summary>
    [Fact]
    public void DroppingFillerWordsDoesNotDropTheVerbTheControlIsNamedAfter()
    {
        string[] labels = ["Pause", "Play"];

        Assert.Equal(1, ScreenElementMatcher.Choose("click the Play button", labels));
    }

    [Fact]
    public void ChoosesNothingWhenTheRequestIsAllFiller() =>
        Assert.Equal(-1, ScreenElementMatcher.Choose("click that", ["Play", "Pause"]));
}
