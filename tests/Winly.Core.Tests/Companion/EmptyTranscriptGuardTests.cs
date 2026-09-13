using NSubstitute;
using Serilog.Core;
using Winly.Core.Abstractions;
using Winly.Core.Actions;
using Winly.Core.Companion;
using Winly.Core.Pointing;
using Winly.Core.Providers;
using Winly.Core.Settings;

namespace Winly.Core.Tests.Companion;

public class EmptyTranscriptGuardTests
{
    private readonly IActivationKeyMonitor _keys = Substitute.For<IActivationKeyMonitor>();
    private readonly IMicrophoneCapture _microphone = Substitute.For<IMicrophoneCapture>();
    private readonly IDisplayCapture _displays = Substitute.For<IDisplayCapture>();
    private readonly ISpeechToTextProvider _speechToText = Substitute.For<ISpeechToTextProvider>();
    private readonly IChatProvider _chat = Substitute.For<IChatProvider>();
    private readonly ITextToSpeechProvider _textToSpeech = Substitute.For<ITextToSpeechProvider>();
    private readonly IAudioPlayback _playback = Substitute.For<IAudioPlayback>();
    private readonly ICompanionOverlay _overlay = Substitute.For<ICompanionOverlay>();
    private readonly IDesktopActions _actions = Substitute.For<IDesktopActions>();
    private readonly ReminderScheduler _reminders = new();
    private readonly IActionRecord _record = Substitute.For<IActionRecord>();
    private readonly ConversationSession _session = new();

    public EmptyTranscriptGuardTests()
    {
        _microphone.StartCapture(Arg.Any<CancellationToken>()).Returns(Task.FromResult<Stream>(new MemoryStream()));
        _textToSpeech.Synthesize(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new SpokenAudio(new MemoryStream(), 24000)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public async Task EmptyTranscriptIssuesNoRequestAndReturnsToIdle(string transcript)
    {
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(transcript));
        var orchestrator = await StartedOrchestrator();

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();
        await orchestrator.CurrentRun;

        Assert.Equal(CompanionState.Idle, orchestrator.StateMachine.State);
        // The displays may already have been snapshotted during listening; what FR-006 forbids is
        // sending anything, so nothing is asked and nothing is remembered.
        await _chat.DidNotReceiveWithAnyArgs().Ask(default!, default!, default);
        await _microphone.Received().StopCapture();
        Assert.Empty(_session.Exchanges);
    }

    [Fact]
    public async Task NonEmptyTranscriptRunsTheWholeLoopAndRecordsTheExchange()
    {
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("what is this button"));
        var capture = new DisplayCapture("m1", [], 1568, 882, true, new MonitorGeometry(0, 0, 3136, 1764, 2.0));
        _displays.CaptureAll(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DisplayCapture>>([capture]));
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatAnswer("It's the **Save** button.", new PointingTarget("m1", 100, 50, "Save"))));
        var orchestrator = await StartedOrchestrator();
        var states = new List<CompanionState>();
        orchestrator.StateMachine.StateChanged += states.Add;

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();
        await orchestrator.CurrentRun;

        Assert.Equal([CompanionState.Listening, CompanionState.Working, CompanionState.Speaking, CompanionState.Idle], states);
        var exchange = Assert.Single(_session.Exchanges);
        Assert.Equal("what is this button", exchange.Transcript);
        Assert.Equal("It's the Save button.", exchange.AnswerText);
        Assert.Equal("Save", exchange.PointingTarget?.Label);
        await _overlay.Received().PointTo(Arg.Is<DesktopLocation>(location => location.MonitorId == "m1" && location.MonitorDipX == 100 && location.MonitorDipY == 50), Arg.Any<CancellationToken>());
        await _playback.Received().Play(Arg.Any<SpokenAudio>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NewActivationAbandonsTheRequestInFlight()
    {
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("first question"));
        _displays.CaptureAll(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DisplayCapture>>([]));
        var chatInFlight = new TaskCompletionSource<ChatAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.Arg<CancellationToken>().Register(() => chatInFlight.TrySetCanceled());
                return chatInFlight.Task;
            });
        var orchestrator = await StartedOrchestrator();

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();
        var firstRun = orchestrator.CurrentRun;
        await WaitUntil(() => orchestrator.StateMachine.State == CompanionState.Working);

        _keys.KeyDown += Raise.Event<Action>();
        await firstRun;

        Assert.Equal(CompanionState.Listening, orchestrator.StateMachine.State);
        Assert.Empty(_session.Exchanges);
        await _playback.Received().Stop();
    }

    [Fact]
    public async Task ProviderFailureSurfacesAPlainMessageAndReturnsToIdle()
    {
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("hello"));
        _displays.CaptureAll(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DisplayCapture>>([]));
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns<ChatAnswer>(_ => throw new ProviderFailureException("provider_unavailable"));
        var orchestrator = await StartedOrchestrator();
        string? posted = null;
        orchestrator.UserMessagePosted += message => posted = message;

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();
        await orchestrator.CurrentRun;

        Assert.Equal(CompanionState.Idle, orchestrator.StateMachine.State);
        Assert.Equal(FailureMessages.For(new ProviderFailureException("provider_unavailable")), posted);
    }

    [Fact]
    public async Task SpeaksTheFirstSentenceBeforeTheAnswerFinishesStreaming()
    {
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("what is this button"));
        _displays.CaptureAll(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DisplayCapture>>([]));
        var answerFinished = new TaskCompletionSource<ChatAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.Arg<Action<string>>()("It saves your file. ");
                return answerFinished.Task;
            });
        var orchestrator = await StartedOrchestrator();

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();

        // The whole point of the pipeline: audio is playing while the model is still writing.
        await WaitUntil(() => orchestrator.StateMachine.State == CompanionState.Speaking);
        await _textToSpeech.Received().Synthesize("It saves your file.", Arg.Any<CancellationToken>());
        await _playback.Received().Play(Arg.Any<SpokenAudio>(), Arg.Any<CancellationToken>());

        answerFinished.SetResult(new ChatAnswer("It saves your file.", null));
        await orchestrator.CurrentRun;

        Assert.Equal(CompanionState.Idle, orchestrator.StateMachine.State);
        Assert.Single(_session.Exchanges);
    }

    [Fact]
    public async Task AnActionDesignationIsCarriedOutWhileTheAnswerIsSpoken()
    {
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("open spotify"));
        _displays.CaptureAll(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DisplayCapture>>([]));
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatAnswer("Opening Spotify.", null, [new DesktopAction(DesktopActionKind.Open, "Spotify")])));
        var orchestrator = await StartedOrchestrator();

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();
        await orchestrator.CurrentRun;

        await _actions.Received().Run(
            Arg.Is<DesktopAction>(action => action.Kind == DesktopActionKind.Open && action.Target == "Spotify"),
            Arg.Any<CancellationToken>());
        Assert.Equal("Opening Spotify.", Assert.Single(_session.Exchanges).AnswerText);
    }

    [Fact]
    public async Task AFailedActionSurfacesItsOwnSentenceAndStillFinishesTheAnswer()
    {
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("open spotty"));
        _displays.CaptureAll(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DisplayCapture>>([]));
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatAnswer("Opening Spotty.", null, [new DesktopAction(DesktopActionKind.Open, "Spotty")])));
        _actions.Run(Arg.Any<DesktopAction>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<string?>(new DesktopActionFailedException("I couldn't find an app called Spotty.")));
        var orchestrator = await StartedOrchestrator();
        string? posted = null;
        orchestrator.UserMessagePosted += message => posted = message;

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();
        await orchestrator.CurrentRun;

        Assert.Equal("I couldn't find an app called Spotty.", posted);
        Assert.Equal(CompanionState.Idle, orchestrator.StateMachine.State);
        Assert.Single(_session.Exchanges);
    }

    [Fact]
    public async Task WhatIsPlayingTravelsWithTheQuestion()
    {
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("pause the song"));
        _displays.CaptureAll(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DisplayCapture>>([]));
        _actions.CurrentlyPlaying(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<NowPlaying?>(new NowPlaying("Spotify", "Alright", "Kendrick Lamar", IsPlaying: true)));
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatAnswer("Pausing it.", null, [new DesktopAction(DesktopActionKind.Media, "pause")])));
        var orchestrator = await StartedOrchestrator();

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();
        await orchestrator.CurrentRun;

        // Without this the model has to guess which app to aim a playback control at.
        await _chat.Received().Ask(
            Arg.Is<ChatRequest>(request => request.NowPlaying!.Title == "Alright" && request.NowPlaying.AppId == "Spotify"),
            Arg.Any<Action<string>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACommandIsAskedWithoutScreenshotsAtAll()
    {
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("pause the song"));
        _displays.CaptureAll(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DisplayCapture>>([Capture()]));
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatAnswer("Pausing it.", null, [new DesktopAction(DesktopActionKind.Media, "pause")])));
        var orchestrator = await StartedOrchestrator();

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();
        await orchestrator.CurrentRun;

        // The screenshot is the whole non-cached cost of a turn and a pause command cannot use one.
        await _chat.Received(1).Ask(
            Arg.Is<ChatRequest>(request => request.Displays.Count == 0 && request.ScreenAvailable),
            Arg.Any<Action<string>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AModelThatAsksForTheScreenIsGivenItWithoutCapturingAgain()
    {
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult("open the thing"));
        _displays.CaptureAll(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DisplayCapture>>([Capture()]));
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Task.FromResult(new ChatAnswer(string.Empty, null, null, NeedsScreen: true)),
                _ => Task.FromResult(new ChatAnswer("That's the settings panel.", null)));
        var orchestrator = await StartedOrchestrator();

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();
        await orchestrator.CurrentRun;

        await _chat.Received(2).Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>());
        await _chat.Received(1).Ask(
            Arg.Is<ChatRequest>(request => request.Displays.Count == 1),
            Arg.Any<Action<string>>(),
            Arg.Any<CancellationToken>());
        // Captured once at key-down and reused, so the second attempt costs a round trip, not a snapshot.
        await _displays.Received(1).CaptureAll(Arg.Any<CancellationToken>());
    }

    private static DisplayCapture Capture() =>
        new("m1", [], 1356, 848, true, new MonitorGeometry(0, 0, 1920, 1200, 1.25));

    private async Task<CompanionOrchestrator> StartedOrchestrator()
    {
        var orchestrator = new CompanionOrchestrator(
            _keys, _microphone, _displays, _speechToText, _chat, _textToSpeech, _playback, _overlay,
            _actions, _reminders, _record, _session, new UserSettings(), Logger.None);
        await orchestrator.Start(CancellationToken.None);
        return orchestrator;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition was not met in time.");
            await Task.Delay(10);
        }
    }
}
