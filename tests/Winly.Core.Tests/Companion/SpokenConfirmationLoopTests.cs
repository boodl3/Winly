using NSubstitute;
using Serilog.Core;
using Winly.Core.Abstractions;
using Winly.Core.Actions;
using Winly.Core.Companion;
using Winly.Core.Pointing;
using Winly.Core.Providers;
using Winly.Core.Settings;

namespace Winly.Core.Tests.Companion;

/// <summary>
/// The round trip behind "are you sure?" — spoken question, microphone, answer, action.
///
/// It existed and could never succeed: the confirmation window cancelled the transcription rather
/// than closing the microphone, and the transcriber only finalizes a turn once the audio stream
/// ends, so every spoken "yes" was thrown away and recorded as a refusal. Cheap to break the same
/// way again — anything that cancels the transcript token on the window fails these two.
/// </summary>
public class SpokenConfirmationLoopTests
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

    /// <summary>Closing an app is irreversible, so it is the classifier's own example of a confirm.</summary>
    private static readonly DesktopAction CloseChrome = new(DesktopActionKind.Window, "Chrome", "close");

    /// <summary>
    /// Completed when the confirmation capture is closed. The fake transcriber below waits on it,
    /// because that is the real provider's contract: it streams audio until the stream ends and
    /// only then does the turn finalize. A fake that answers instantly hides this whole bug — it
    /// passes just as happily against the code that cancelled the transcription at the window.
    /// </summary>
    private readonly TaskCompletionSource _confirmationCaptureClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _capturesStarted;

    public SpokenConfirmationLoopTests()
    {
        _microphone.StartCapture(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            Interlocked.Increment(ref _capturesStarted);
            return Task.FromResult<Stream>(new MemoryStream());
        });

        // Counted by starts rather than stops: a stop also runs before each start, to release a
        // device an activation being replaced may still hold, so counting stops would call the
        // confirmation closed before it had opened.
        _microphone.When(microphone => microphone.StopCapture()).Do(_ =>
        {
            if (Volatile.Read(ref _capturesStarted) >= 2)
            {
                _confirmationCaptureClosed.TrySetResult();
            }
        });
        _textToSpeech.Synthesize(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new SpokenAudio(new MemoryStream(), 24000)));
        _displays.CaptureAll(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DisplayCapture>>([]));
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatAnswer("Closing Chrome.", null, [CloseChrome])));
    }

    [Theory]
    [InlineData("Yes.")]
    [InlineData("yeah go ahead")]
    public async Task AnAnswerOfYesCarriesTheActionOut(string answer)
    {
        await Confirm(firstTranscript: "close chrome", confirmationAnswer: answer);

        await _actions.Received().Run(
            Arg.Is<DesktopAction>(action => action.Kind == DesktopActionKind.Window && action.Argument == "close"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("No, leave it.")]
    [InlineData("")]
    public async Task AnythingThatIsNotAYesLeavesItAlone(string answer)
    {
        await Confirm(firstTranscript: "close chrome", confirmationAnswer: answer);

        await _actions.DidNotReceive().Run(Arg.Any<DesktopAction>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Once for the request, once for the confirmation — and the second one gets closed.</summary>
    [Fact]
    public async Task TheWindowClosesTheMicrophoneRatherThanCancellingTheTranscript()
    {
        await Confirm(firstTranscript: "close chrome", confirmationAnswer: "yes");

        await _microphone.Received(2).StartCapture(Arg.Any<CancellationToken>());
        Assert.True(_confirmationCaptureClosed.Task.IsCompletedSuccessfully);
    }

    /// <summary>
    /// The window is a deadline now, not a wait: a yes or no is acted on as soon as the decoder
    /// revises the transcript into one. Five real seconds per confirmation was long enough that
    /// the user answered twice, the second answer landing in a microphone that had already closed.
    /// </summary>
    [Fact]
    public async Task AClearAnswerIsActedOnWithoutWaitingOutTheWindow()
    {
        var window = TimeSpan.FromSeconds(5);
        var started = System.Diagnostics.Stopwatch.StartNew();

        await Confirm(firstTranscript: "close chrome", confirmationAnswer: "yes", confirmationWindow: window);

        Assert.True(started.Elapsed < window, $"waited {started.Elapsed} of a {window} window");
        await _actions.Received().Run(Arg.Any<DesktopAction>(), Arg.Any<CancellationToken>());
    }

    private async Task Confirm(string firstTranscript, string confirmationAnswer, TimeSpan? confirmationWindow = null)
    {
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>(), Arg.Any<Action<string>?>())
            .Returns(
                _ => Task.FromResult(firstTranscript),
                async call =>
                {
                    // The decoder revises the transcript while the microphone is still open, which
                    // is what the fast path reads. An answer it cannot decide yields no partial and
                    // falls through to the stream-end path below, same as silence does.
                    call.Arg<Action<string>?>()?.Invoke(confirmationAnswer);

                    // No transcript until the audio stream ends, exactly as the real one behaves.
                    await _confirmationCaptureClosed.Task.WaitAsync(call.Arg<CancellationToken>());
                    return confirmationAnswer;
                });
        await RunOneActivation(confirmationWindow);
    }

    private async Task RunOneActivation(TimeSpan? confirmationWindow = null)
    {
        var orchestrator = new CompanionOrchestrator(
            _keys, _microphone, _displays, _speechToText, _chat, _textToSpeech, _playback, _overlay,
            _actions, _reminders, _record, _session, new UserSettings(), Logger.None)
        {
            // The real one is five seconds, which is five seconds per test case.
            ConfirmationWindow = confirmationWindow ?? TimeSpan.FromMilliseconds(20),
        };
        await orchestrator.Start(CancellationToken.None);

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();
        await orchestrator.CurrentRun;
    }
}
