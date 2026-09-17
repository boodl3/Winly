using NSubstitute;
using Winly.Core.Actions;
using Winly.Core.Companion;

namespace Winly.Core.Tests.Actions;

public class DesktopActionSequenceRunnerTests
{
    private readonly IDesktopActions _actions = Substitute.For<IDesktopActions>();
    private readonly ReminderScheduler _reminders = new();
    private readonly List<DesktopAction> _ran = [];

    public DesktopActionSequenceRunnerTests()
    {
        _actions.Run(Arg.Any<DesktopAction>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _ran.Add(call.Arg<DesktopAction>());
                return Task.FromResult<string?>(null);
            });
        _actions.WaitForApplicationReady(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(true));
    }

    private DesktopActionSequenceRunner Runner(
        ActionBounds? bounds = null,
        Func<DesktopAction, CancellationToken, Task<bool>>? confirm = null,
        Func<bool>? actingEnabled = null) =>
        new(
            _actions,
            _reminders,
            bounds ?? ActionBounds.Default,
            confirm ?? ((_, _) => Task.FromResult(true)),
            actingEnabled);

    private static DesktopAction CloseWord() => new(DesktopActionKind.Window, "Word", "close");

    private static DesktopAction Open(string app) => new(DesktopActionKind.Open, app);

    private static DesktopAction Play(string what) => new(DesktopActionKind.Play, what);

    private static DesktopAction Media(string what) => new(DesktopActionKind.Media, what);

    [Fact]
    public async Task EveryActionRunsInTheOrderTheModelDeclaredThem()
    {
        var result = await Runner().Run(
            [Open("Spotify"), Play("jazz"), new DesktopAction(DesktopActionKind.Volume, "", Amount: -20, Relative: true)],
            CancellationToken.None);

        Assert.Equal(
            [DesktopActionKind.Open, DesktopActionKind.Play, DesktopActionKind.Volume],
            _ran.Select(action => action.Kind));
        Assert.True(result.EverythingCompleted);
        Assert.All(result.Outcomes, outcome => Assert.Equal(ActionOutcomeStatus.Completed, outcome.Status));
    }

    [Fact]
    public async Task ASingleActionSequenceStillRunsExactlyOnce()
    {
        var result = await Runner().Run([Media("next")], CancellationToken.None);

        Assert.Equal([Media("next")], _ran);
        Assert.True(result.EverythingCompleted);
        Assert.False(result.StoppedAtActionLimit);
    }

    [Fact]
    public async Task NoActionsAtAllRunsNothing()
    {
        var result = await Runner().Run([], CancellationToken.None);

        Assert.Empty(_ran);
        Assert.Empty(result.Outcomes);
        Assert.True(result.EverythingCompleted);
    }

    [Fact]
    public async Task MoreActionsThanOneRequestMayCarryStopsAtTheLimitAndReportsTheTaskUnfinished()
    {
        var overLimit = Enumerable.Range(0, DesktopActionSequenceRunner.MaxActionsPerRequest + 1)
            .Select(index => Media($"next{index}"))
            .ToArray();

        var result = await Runner().Run(overLimit, CancellationToken.None);

        Assert.Equal(DesktopActionSequenceRunner.MaxActionsPerRequest, _ran.Count);
        Assert.True(result.StoppedAtActionLimit);
        Assert.False(result.EverythingCompleted);
    }

    [Fact]
    public async Task ANewerActivationAbandonsWhatIsLeftAtTheNextBoundary()
    {
        using var activation = new CancellationTokenSource();
        _actions.Run(Arg.Any<DesktopAction>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _ran.Add(call.Arg<DesktopAction>());
                activation.Cancel();
                return Task.FromResult<string?>(null);
            });

        var result = await Runner().Run([Media("next"), Media("previous"), Media("pause")], activation.Token);

        Assert.Single(_ran);
        Assert.Equal(ActionOutcomeStatus.Completed, result.Outcomes[0].Status);
        Assert.Equal(ActionOutcomeStatus.Abandoned, result.Outcomes[1].Status);
        Assert.Equal(ActionOutcomeStatus.Abandoned, result.Outcomes[2].Status);
    }

    [Fact]
    public async Task AnActionFollowingALaunchWaitsForThatApplicationToBeReadyFirst()
    {
        await Runner().Run([Open("Spotify"), Play("jazz")], CancellationToken.None);

        await _actions.Received(1).WaitForApplicationReady("Spotify", ActionBounds.Default.ApplicationReady, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnApplicationThisRequestDidNotLaunchIsNotWaitedFor()
    {
        await Runner().Run([Play("jazz")], CancellationToken.None);

        await _actions.DidNotReceive().WaitForApplicationReady(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReadinessNeverArrivingFailsThatActionAndStopsTheSequence()
    {
        _actions.WaitForApplicationReady(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        var result = await Runner().Run([Open("Spotify"), Play("jazz"), Media("next")], CancellationToken.None);

        Assert.Equal([Open("Spotify")], _ran);
        Assert.Equal(ActionOutcomeStatus.Completed, result.Outcomes[0].Status);
        Assert.Equal(ActionOutcomeStatus.Failed, result.Outcomes[1].Status);
        Assert.Equal(ActionOutcomeStatus.Abandoned, result.Outcomes[2].Status);
        Assert.Contains("Spotify", result.Outcomes[1].UserFacingReason);
    }

    [Fact]
    public async Task AFailingActionStopsTheSequenceAndEverythingAfterItIsAbandoned()
    {
        _actions.Run(Arg.Is<DesktopAction>(action => action.Kind == DesktopActionKind.Open), Arg.Any<CancellationToken>())
            .Returns<Task<string?>>(_ => throw new DesktopActionFailedException("I couldn't find an app called Spotify."));

        var result = await Runner().Run([Open("Spotify"), Play("jazz"), Media("next")], CancellationToken.None);

        Assert.Empty(_ran);
        Assert.Equal(ActionOutcomeStatus.Failed, result.Outcomes[0].Status);
        Assert.Equal("I couldn't find an app called Spotify.", result.Outcomes[0].UserFacingReason);
        Assert.Equal(ActionOutcomeStatus.Abandoned, result.Outcomes[1].Status);
        Assert.Equal(ActionOutcomeStatus.Abandoned, result.Outcomes[2].Status);
    }

    [Fact]
    public async Task AnActionPastItsOwnTimeBoundIsTreatedAsAFailureRatherThanWaitedOut()
    {
        _actions.Run(Arg.Any<DesktopAction>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                return (string?)null;
            });
        var bounds = ActionBounds.Default with { SingleAction = TimeSpan.FromMilliseconds(50) };

        var result = await Runner(bounds).Run([Media("next"), Media("previous")], CancellationToken.None);

        Assert.Equal(ActionOutcomeStatus.Failed, result.Outcomes[0].Status);
        Assert.Equal(ActionOutcomeStatus.Abandoned, result.Outcomes[1].Status);
        Assert.DoesNotContain("Exception", result.Outcomes[0].UserFacingReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AUrlIsNotTreatedAsALaunchedApplicationToWaitFor()
    {
        await Runner().Run([Open("https://example.test"), Media("next")], CancellationToken.None);

        await _actions.DidNotReceive().WaitForApplicationReady(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AConsequentialActionIsNotRunUntilTheUserHasAgreed()
    {
        var askedBeforeRunning = false;
        var runner = Runner(confirm: (_, _) =>
        {
            askedBeforeRunning = _ran.Count == 0;
            return Task.FromResult(true);
        });

        await runner.Run([CloseWord()], CancellationToken.None);

        Assert.True(askedBeforeRunning);
        Assert.Equal([CloseWord()], _ran);
    }

    [Fact]
    public async Task RefusingStopsTheSequenceThereAndNothingAfterItRuns()
    {
        var runner = Runner(confirm: (_, _) => Task.FromResult(false));

        var result = await runner.Run([CloseWord(), Media("next")], CancellationToken.None);

        Assert.Empty(_ran);
        Assert.Equal(ActionOutcomeStatus.Declined, result.Outcomes[0].Status);
        Assert.Equal(ActionOutcomeStatus.Abandoned, result.Outcomes[1].Status);
        Assert.Contains("Word", result.Outcomes[0].UserFacingReason, StringComparison.Ordinal);
    }

    /// <summary>Agreement covers exactly the action it was given for (FR-013).</summary>
    [Fact]
    public async Task AgreeingToOneActionDoesNotCoverTheNextOne()
    {
        var asked = new List<DesktopAction>();
        var runner = Runner(confirm: (action, _) =>
        {
            asked.Add(action);
            return Task.FromResult(true);
        });

        await runner.Run([CloseWord(), new DesktopAction(DesktopActionKind.System, "lock")], CancellationToken.None);

        Assert.Equal(2, asked.Count);
    }

    [Fact]
    public async Task ActionsThatAreNotConsequentialAreNeverAskedAbout()
    {
        var asked = 0;
        var runner = Runner(confirm: (_, _) =>
        {
            asked++;
            return Task.FromResult(true);
        });

        await runner.Run([Media("next"), new DesktopAction(DesktopActionKind.Volume, "", Amount: 40)], CancellationToken.None);

        Assert.Equal(0, asked);
        Assert.Equal(2, _ran.Count);
    }

    /// <summary>
    /// No way to ask is not the same as permission. A runner built without a confirmation route
    /// must refuse rather than act.
    /// </summary>
    [Fact]
    public async Task WithNoWayToAskAConsequentialActionIsDeclinedRatherThanRun()
    {
        var runner = new DesktopActionSequenceRunner(_actions, _reminders, ActionBounds.Default);

        var result = await runner.Run([CloseWord()], CancellationToken.None);

        Assert.Empty(_ran);
        Assert.Equal(ActionOutcomeStatus.Declined, result.Outcomes[0].Status);
    }

    /// <summary>
    /// The user's own thinking time must not count against the action's bound, or a slow answer
    /// would fail the very action it just approved.
    /// </summary>
    [Fact]
    public async Task TimeSpentWaitingForAnAnswerDoesNotCountAgainstTheActionsBound()
    {
        var bounds = ActionBounds.Default with { SingleAction = TimeSpan.FromMilliseconds(400) };
        var runner = Runner(bounds, confirm: async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(700));
            return true;
        });

        var result = await runner.Run([CloseWord()], CancellationToken.None);

        Assert.Equal(ActionOutcomeStatus.Completed, result.Outcomes[0].Status);
    }

    [Fact]
    public async Task WithActingTurnedOffNothingIsCarriedOut()
    {
        var runner = Runner(actingEnabled: () => false);

        var result = await runner.Run([Media("next"), Media("pause")], CancellationToken.None);

        Assert.Empty(_ran);
        Assert.Equal(ActionOutcomeStatus.Blocked, result.Outcomes[0].Status);
        Assert.Equal(ActionOutcomeStatus.Abandoned, result.Outcomes[1].Status);
    }

    /// <summary>
    /// Read again before every action, so revoking the permission part way through takes effect at
    /// the next boundary instead of after the whole request (FR-019).
    /// </summary>
    [Fact]
    public async Task TurningActingOffPartWayThroughStopsTheSequenceAtTheNextAction()
    {
        var enabled = true;
        var runner = Runner(actingEnabled: () => enabled);
        _actions.Run(Arg.Any<DesktopAction>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _ran.Add(call.Arg<DesktopAction>());
                enabled = false;
                return Task.FromResult<string?>(null);
            });

        var result = await runner.Run([Media("next"), Media("pause"), Media("previous")], CancellationToken.None);

        Assert.Single(_ran);
        Assert.Equal(ActionOutcomeStatus.Completed, result.Outcomes[0].Status);
        Assert.Equal(ActionOutcomeStatus.Blocked, result.Outcomes[1].Status);
        Assert.Equal(ActionOutcomeStatus.Abandoned, result.Outcomes[2].Status);
    }

    [Fact]
    public async Task ATimerIsScheduledWithoutReachingTheDesktop()
    {
        var result = await Runner().Run([new DesktopAction(DesktopActionKind.Timer, "stretch", Amount: 1200)], CancellationToken.None);

        Assert.Empty(_ran);
        Assert.Equal(1, _reminders.PendingCount);
        Assert.True(result.EverythingCompleted);
    }

    [Fact]
    public async Task TypingWaitsForTheApplicationThisRequestJustOpened()
    {
        var result = await Runner().Run(
            [Open("Notepad"), new DesktopAction(DesktopActionKind.Type, "the summary")],
            CancellationToken.None);

        await _actions.Received(1).WaitForApplicationReady("Notepad", Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        Assert.True(result.EverythingCompleted);
    }

    /// <summary>
    /// Typing is never confirmed now, wherever it is aimed — into an app this request opened, into a
    /// field it clicked, or into whatever the user already had in front of them. The guard that
    /// replaced the question lives in UserInputControl, which refuses if the focus has moved.
    /// </summary>
    [Fact]
    public async Task TypingIsCarriedOutWithoutBeingAskedAbout()
    {
        var asked = new List<DesktopAction>();

        var result = await Runner(confirm: (action, _) =>
        {
            asked.Add(action);
            return Task.FromResult(true);
        }).Run(
            [
                new DesktopAction(DesktopActionKind.Type, "dear Sam"),
                Open("Notepad"),
                new DesktopAction(DesktopActionKind.Type, "the summary"),
                Open("https://mail.google.com/mail/u/0/?view=cm&fs=1"),
                new DesktopAction(DesktopActionKind.Click, "Message Body", "Chrome"),
                new DesktopAction(DesktopActionKind.Type, "the draft"),
            ],
            CancellationToken.None);

        Assert.Empty(asked);
        Assert.True(result.EverythingCompleted);
        Assert.Equal(6, _ran.Count);
    }

}
