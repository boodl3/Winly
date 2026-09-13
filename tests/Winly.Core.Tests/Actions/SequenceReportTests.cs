using Winly.Core.Actions;
using Winly.Core.Companion;

namespace Winly.Core.Tests.Actions;

/// <summary>
/// What the user is told when a sequence did not finish everything (FR-008). The hard requirement
/// is that none of it leaks an error code, an exception name or a provider name.
/// </summary>
public class SequenceReportTests
{
    private static DesktopAction Media(string what) => new(DesktopActionKind.Media, what);

    private static ActionSequenceResult Sequence(params ActionOutcome[] outcomes) => new(outcomes, false);

    [Fact]
    public void AFullyCompletedSequenceSaysNothingExtra()
    {
        var report = FailureMessages.ForSequence(Sequence(
            ActionOutcome.Completed(Media("next")),
            ActionOutcome.Completed(Media("pause"))));

        Assert.Equal(string.Empty, report);
    }

    /// <summary>A one-action request must read exactly as it did before sequences existed (FR-006).</summary>
    [Fact]
    public void ASingleFailedActionIsReportedAsItsOwnSentenceAndNothingElse()
    {
        var report = FailureMessages.ForSequence(Sequence(
            ActionOutcome.Failed(Media("next"), "I couldn't find an app called Spotty.")));

        Assert.Equal("I couldn't find an app called Spotty.", report);
    }

    [Fact]
    public void PartialProgressNamesHowMuchGotDoneAndWhatStoppedIt()
    {
        var report = FailureMessages.ForSequence(Sequence(
            ActionOutcome.Completed(Media("next")),
            ActionOutcome.Failed(Media("pause"), "Spotify didn't finish starting, so I stopped there."),
            ActionOutcome.Abandoned(Media("previous"))));

        Assert.Equal("I got the first part done, but Spotify didn't finish starting, so I stopped there.", report);
    }

    [Fact]
    public void MoreThanOneCompletedPartIsCountedInThePlural()
    {
        var report = FailureMessages.ForSequence(Sequence(
            ActionOutcome.Completed(Media("next")),
            ActionOutcome.Completed(Media("pause")),
            ActionOutcome.Failed(Media("previous"), "it took too long, so I stopped there.")));

        Assert.Equal("I got the first 2 parts done, but it took too long, so I stopped there.", report);
    }

    [Fact]
    public void ReachingTheActionLimitSaysTheTaskIsUnfinished()
    {
        var completed = Enumerable.Range(0, 5).Select(index => ActionOutcome.Completed(Media($"next{index}"))).ToArray();

        var report = FailureMessages.ForSequence(new ActionSequenceResult(completed, StoppedAtActionLimit: true));

        Assert.Contains("more than I can do in one go", report, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclinedActionIsReportedInTheUsersOwnTerms()
    {
        var report = FailureMessages.ForSequence(Sequence(
            ActionOutcome.Completed(Media("next")),
            ActionOutcome.Declined(Media("pause"), "you said no to closing Word")));

        Assert.Equal("I got the first part done, but you said no to closing Word.", report);
    }

    [Theory]
    [InlineData("Exception")]
    [InlineData("Error")]
    [InlineData("HResult")]
    [InlineData("0x")]
    public void NoReportEverCarriesAnErrorCodeOrAnExceptionName(string forbidden)
    {
        var reports = new[]
        {
            FailureMessages.ForSequence(Sequence(ActionOutcome.Failed(Media("next"), "it took too long, so I stopped there."))),
            FailureMessages.ForSequence(Sequence(
                ActionOutcome.Completed(Media("next")),
                ActionOutcome.Failed(Media("pause"), "Spotify didn't finish starting, so I stopped there."))),
            FailureMessages.ForSequence(Sequence(
                ActionOutcome.Completed(Media("next")),
                ActionOutcome.Blocked(Media("pause")))),
        };

        Assert.All(reports, report => Assert.DoesNotContain(forbidden, report, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheReasonsOwnFullStopIsNotDoubledUp()
    {
        var report = FailureMessages.ForSequence(Sequence(
            ActionOutcome.Completed(Media("next")),
            ActionOutcome.Failed(Media("pause"), "I couldn't find it.")));

        Assert.DoesNotContain("..", report, StringComparison.Ordinal);
    }
}
