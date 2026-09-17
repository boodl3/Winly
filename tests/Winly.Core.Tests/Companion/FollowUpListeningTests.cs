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
/// Winly asking the user something and then listening for the answer without the activation key.
///
/// Before this, an answer ending in a question went straight to Idle with the microphone shut, and
/// the only listen-after-speak path in the whole codebase was the confirmation gate — reachable only
/// from a consequential action, never from answer text.
/// </summary>
public class FollowUpListeningTests
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
    private readonly List<string> _asked = [];

    public FollowUpListeningTests()
    {
        _microphone.StartCapture(Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult<Stream>(new MemoryStream()));
        _textToSpeech.Synthesize(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new SpokenAudio(new MemoryStream(), 24000)));
        _displays.CaptureAll(Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<DisplayCapture>>([]));
    }

    /// <summary>The first answer asks a question; anything after it is an ordinary answer.</summary>
    private void AnswersWithAQuestionFirst() =>
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                call =>
                {
                    _asked.Add(call.Arg<ChatRequest>().Transcript);
                    return Task.FromResult(new ChatAnswer("Which one did you mean?", null, [], AwaitingReply: true));
                },
                call =>
                {
                    _asked.Add(call.Arg<ChatRequest>().Transcript);
                    return Task.FromResult(new ChatAnswer("The one on the left.", null));
                });

    private void Hears(string firstTranscript, string reply) =>
        _speechToText.Transcribe(Arg.Any<Stream>(), Arg.Any<CancellationToken>(), Arg.Any<Action<string>?>())
            .Returns(
                _ => Task.FromResult(firstTranscript),
                call =>
                {
                    // The decoder revises while the microphone is open; that is what ends the reply,
                    // since a reply has no key release to say the user is done.
                    call.Arg<Action<string>?>()?.Invoke(reply);
                    return Task.FromResult(reply);
                });

    [Fact]
    public async Task AnAnswerThatAsksAQuestionReopensTheMicrophoneAndAsksAgainWithTheReply()
    {
        AnswersWithAQuestionFirst();
        Hears(firstTranscript: "what is that", reply: "the blue one");

        await RunOneActivation();

        Assert.Equal(["what is that", "the blue one"], _asked);
        await _microphone.Received(2).StartCapture(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The second turn speaks too, which is only possible if the state machine left Speaking first:
    /// Speaking -> Speaking is illegal and would fail the activation instead.
    /// </summary>
    [Fact]
    public async Task TheSecondTurnIsSpokenRatherThanFailingOnAnIllegalTransition()
    {
        var failures = new List<string>();
        AnswersWithAQuestionFirst();
        Hears(firstTranscript: "what is that", reply: "the blue one");

        await RunOneActivation(message => failures.Add(message));

        await _playback.Received(2).Play(Arg.Any<SpokenAudio>(), Arg.Any<CancellationToken>());
        Assert.Empty(failures);
    }

    [Fact]
    public async Task AnOrdinaryAnswerLeavesTheMicrophoneShut()
    {
        _chat.Ask(Arg.Any<ChatRequest>(), Arg.Any<Action<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatAnswer("It's the settings panel.", null)));
        Hears(firstTranscript: "what is that", reply: "never asked for");

        await RunOneActivation();

        await _microphone.Received(1).StartCapture(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SayingNothingBackEndsTheTurnRatherThanAskingAgain()
    {
        AnswersWithAQuestionFirst();
        Hears(firstTranscript: "what is that", reply: "");

        await RunOneActivation();

        Assert.Equal(["what is that"], _asked);
    }

    private async Task RunOneActivation(Action<string>? onUserMessage = null)
    {
        var orchestrator = new CompanionOrchestrator(
            _keys, _microphone, _displays, _speechToText, _chat, _textToSpeech, _playback, _overlay,
            _actions, _reminders, _record, _session, new UserSettings(), Logger.None)
        {
            // The real ones are eight seconds and 1.2 s, which is a wall clock this test does not
            // need to spend: what is under test is the loop, not the silence threshold.
            ReplyWindow = TimeSpan.FromMilliseconds(600),
            QuietEndsAReply = TimeSpan.FromMilliseconds(1),
        };
        if (onUserMessage is not null)
        {
            orchestrator.UserMessagePosted += onUserMessage;
        }

        await orchestrator.Start(CancellationToken.None);

        _keys.KeyDown += Raise.Event<Action>();
        _keys.KeyUp += Raise.Event<Action>();
        await orchestrator.CurrentRun;
    }
}
