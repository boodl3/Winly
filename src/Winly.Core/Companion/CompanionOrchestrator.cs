using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using Serilog;
using Winly.Core.Abstractions;
using Winly.Core.Actions;
using Winly.Core.Pointing;
using Winly.Core.Providers;
using Winly.Core.Settings;

namespace Winly.Core.Companion;

/// <summary>
/// The core loop: key hold → listen → transcribe → capture displays → ask → speak → point → act.
/// Talks only to interfaces, so it runs headless under test (Constitution Principle V).
/// </summary>
public sealed class CompanionOrchestrator : IAsyncDisposable
{
    public static readonly TimeSpan MaxHoldDuration = TimeSpan.FromSeconds(30);

    /// <summary>How many past turns travel with a new question. Enough for "and the one next to it?".</summary>
    public const int HistoryExchangesSent = 6;

    private const string NoAnswerText = "I don't have an answer for that.";

    /// <summary>
    /// How long the transcriber gets to hand back a confirmation answer after the microphone has
    /// closed. The audio is already in; this is the round trip, not the listening.
    /// </summary>
    private static readonly TimeSpan TranscriptionGrace = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How long the microphone stays open after Winly has asked the user something, when nothing is
    /// said at all. Longer than a confirmation window because the answer here is a sentence rather
    /// than one word, and someone who has just been asked to repeat themselves takes a moment.
    /// </summary>
    private static readonly TimeSpan DefaultReplyWindow = TimeSpan.FromSeconds(8);

    /// <summary>
    /// How much quiet after the user has clearly started speaking ends the reply. Push-to-talk has
    /// a key release to say "I am done" and this path has nothing, so the silence has to say it —
    /// the transcriber will not, since its own turn detection is deliberately pushed to never.
    /// </summary>
    private static readonly TimeSpan DefaultQuietEndsAReply = TimeSpan.FromMilliseconds(1200);

    /// <summary>How often the reply window checks whether the user has stopped talking.</summary>
    private static readonly TimeSpan ReplyPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly IActivationKeyMonitor _activationKeys;
    private readonly IMicrophoneCapture _microphone;
    private readonly IDisplayCapture _displays;
    private readonly ISpeechToTextProvider _speechToText;
    private readonly IChatProvider _chat;
    private readonly ITextToSpeechProvider _textToSpeech;
    private readonly IAudioPlayback _playback;
    private readonly ICompanionOverlay _overlay;
    private readonly IDesktopActions _actions;
    private readonly ReminderScheduler _reminders;
    private readonly DesktopActionSequenceRunner _sequenceRunner;
    private readonly IActionRecord _record;
    private readonly ILogger _log;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _currentActivation;
    private TaskCompletionSource? _keyReleased;

    /// <summary>
    /// Playback of the answer currently being spoken, so anything Winly wants to say *about* that
    /// answer can wait for it instead of talking over it. One output device is shared by the whole
    /// app and each Play stops the last, so two overlapping speakers do not mix — they chop each
    /// other into fragments, which is what a confirmation question sounded like.
    /// </summary>
    private Task _answerSpeech = Task.CompletedTask;

    public CompanionOrchestrator(
        IActivationKeyMonitor activationKeys,
        IMicrophoneCapture microphone,
        IDisplayCapture displays,
        ISpeechToTextProvider speechToText,
        IChatProvider chat,
        ITextToSpeechProvider textToSpeech,
        IAudioPlayback playback,
        ICompanionOverlay overlay,
        IDesktopActions actions,
        ReminderScheduler reminders,
        IActionRecord record,
        ConversationSession session,
        UserSettings settings,
        ILogger log)
    {
        _record = record;
        _activationKeys = activationKeys;
        _microphone = microphone;
        _displays = displays;
        _speechToText = speechToText;
        _chat = chat;
        _textToSpeech = textToSpeech;
        _playback = playback;
        _overlay = overlay;
        _actions = actions;
        _reminders = reminders;
        Session = session;
        Settings = settings;
        // Reads the property rather than the parameter: settings are replaced wholesale when the
        // user saves the panel, and the runner must see the current value, not the startup one.
        _sequenceRunner = new DesktopActionSequenceRunner(
            actions,
            reminders,
            ActionBounds.Default,
            ConfirmAloud,
            () => Settings.DesktopActionsEnabled);
        _log = log;
        _reminders.Elapsed += OnReminderElapsed;
    }

    public CompanionStateMachine StateMachine { get; } = new();

    public ConversationSession Session { get; }

    public UserSettings Settings { get; set; }

    internal Task CurrentRun { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// How long the microphone stays open for a spoken confirmation (FR-012b). Settable so the test
    /// does not have to spend five real seconds per confirmation.
    /// </summary>
    internal TimeSpan ConfirmationWindow { get; init; } = SpokenConfirmation.Window;

    /// <summary>
    /// How long the microphone stays open after Winly has asked a question. Settable for the same
    /// reason <see cref="ConfirmationWindow"/> is: a test must not spend the real window per case.
    /// </summary>
    internal TimeSpan ReplyWindow { get; init; } = DefaultReplyWindow;

    /// <summary>How much quiet ends a reply, once the user has clearly started speaking.</summary>
    internal TimeSpan QuietEndsAReply { get; init; } = DefaultQuietEndsAReply;

    public event Action<string>? TranscriptRecognized;

    public event Action<string>? AnswerDeltaReceived;

    /// <summary>Plain-language status or failure text for the user (FR-028, FR-031).</summary>
    public event Action<string>? UserMessagePosted;

    public async Task Start(CancellationToken cancellationToken)
    {
        _activationKeys.KeyDown += OnActivationKeyDown;
        _activationKeys.KeyUp += OnActivationKeyUp;
        await _activationKeys.Start(KeyCombination.Parse(Settings.ActivationKeyCombination), cancellationToken);
    }

    public async Task ApplyActivationKey(string keyCombinationDescriptor)
    {
        await _activationKeys.Stop();
        await _activationKeys.Start(KeyCombination.Parse(keyCombinationDescriptor), CancellationToken.None);
    }

    /// <summary>Speaks something Winly initiated rather than answered, such as a timer coming due.</summary>
    public async Task Announce(string text, CancellationToken cancellationToken)
    {
        var spoken = AnswerTextSanitizer.Sanitize(text);
        if (spoken.Length == 0)
        {
            Post(text);
            return;
        }

        // Every announcement is something Winly starts saying on its own — a confirmation
        // question, a failed action, a timer — and all three can land while an answer is still
        // playing. Waiting is the whole fix for the confirmation prompt "spasming": it was spoken
        // over the answer, and each kept stopping the other's device mid-word. The answer's own
        // failures belong to the path that owns it, so they are swallowed rather than reported
        // twice.
        try
        {
            await _answerSpeech;
        }
        catch (Exception failure)
        {
            _log.Debug(failure, "The answer being spoken ended badly; announcing anyway");
        }

        // Covers the cancellation the catch above just swallowed: an abandoned activation
        // announces nothing.
        cancellationToken.ThrowIfCancellationRequested();
        Post(text);

        // Whole text in one go, so there is no earlier chunk for the voice to carry on from.
        var audio = await _textToSpeech.Synthesize(spoken, null, cancellationToken);
        await _playback.Play(audio, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _reminders.Elapsed -= OnReminderElapsed;
        _activationKeys.KeyDown -= OnActivationKeyDown;
        _activationKeys.KeyUp -= OnActivationKeyUp;
        _currentActivation?.Cancel();
        await _playback.Stop();
        await _activationKeys.Stop();
    }

    private void OnReminderElapsed(string label)
    {
        // Fired on a pool thread with nothing to catch it; a timer must never take the app down.
        _ = Task.Run(async () =>
        {
            try
            {
                await Announce(label.Length == 0 ? "Your timer is up." : $"Your timer is up: {label}.", CancellationToken.None);
            }
            catch (Exception failure)
            {
                _log.Error(failure, "A reminder could not be announced");
            }
        });
    }

    private void OnActivationKeyDown()
    {
        CancellationTokenSource activation;
        Task keyReleased;
        try
        {
            lock (_gate)
            {
                if (StateMachine.State == CompanionState.Listening)
                {
                    return;
                }

                // A new activation abandons whatever is in flight (FR-015).
                _currentActivation?.Cancel();
                activation = new CancellationTokenSource();
                _currentActivation = activation;
                _keyReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                keyReleased = _keyReleased.Task;
                StateMachine.TransitionTo(CompanionState.Listening);
            }

            _overlay.SetState(CompanionState.Listening);
            _ = _playback.Stop();
            CurrentRun = Task.Run(() => RunActivation(activation, keyReleased));
        }
        catch (Exception failure)
        {
            _log.Error(failure, "Activation key press could not be handled");
        }
    }

    private void OnActivationKeyUp() => _keyReleased?.TrySetResult();

    private async Task RunActivation(CancellationTokenSource activation, Task keyReleased)
    {
        var cancellationToken = activation.Token;
        try
        {
            if (!Settings.MicrophoneCaptureEnabled)
            {
                Post("Microphone capture is turned off in settings, so I can't listen right now.");
                await WaitForKeyRelease(keyReleased, cancellationToken);
                Finish(activation);
                return;
            }

            // Before Winly speaks or acts: where the user is looking right now is what they mean by
            // "here" if this turn out to be a typing request (FR-016).
            _actions.RememberTypingTarget();

            // The activation this one just replaced may still hold the device — a confirmation
            // window is the normal way that happens — and StartCapture throws on a second opener.
            // Idempotent when nothing is open, so it costs nothing on the ordinary path.
            await _microphone.StopCapture();

            var audio = await _microphone.StartCapture(cancellationToken);
            var transcription = _speechToText.Transcribe(audio, cancellationToken);

            // Snapshotting the displays while the user is still talking takes the slowest step off
            // the critical path: by key release the screenshots are usually already encoded (SC-001).
            // They are captured whether or not they end up being sent, so that if the model does ask
            // for them the second attempt costs a round trip and not another capture.
            var capturing = Settings.ScreenCaptureEnabled
                ? _displays.CaptureAll(cancellationToken)
                : Task.FromResult<IReadOnlyList<DisplayCapture>>([]);
            Observe(capturing);

            // Knowing what is already playing is what lets "pause the song" reach the right app.
            var nowPlaying = _actions.CurrentlyPlaying(cancellationToken);
            Observe(nowPlaying);

            long releasedAt;
            try
            {
                await WaitForKeyRelease(keyReleased, cancellationToken);
            }
            finally
            {
                // Stamped at key release, before the microphone closes rather than after. Closing it
                // now waits for the recorder to hand back the frames it had already captured, and
                // stamping afterwards hid that wait in an unmeasured gap — the transcript stage read
                // 0.09 ms on a real turn, which is not a fast transcriber, it is a stopwatch started
                // late. SC-001 measures key release to first spoken word, so this is where it starts.
                releasedAt = Stopwatch.GetTimestamp();
                await _microphone.StopCapture();
            }

            // Before the transcript, not after it: the microphone is already closed, so leaving the
            // Listening indicator up would keep showing a capture badge for something that is no
            // longer being captured (FR-027) — and it is the stretch the user reads as a freeze,
            // because waiting on the transcript is the longest silent step of the whole turn.
            Transition(activation, CompanionState.Working);

            var transcript = (await transcription).Trim();
            var transcriptAt = Stopwatch.GetTimestamp();
            TranscriptRecognized?.Invoke(transcript);
            if (transcript.Length == 0)
            {
                // Nothing intelligible was said: no request is issued (FR-006).
                Finish(activation);
                return;
            }

            // One hold can be more than one turn now: an answer that asks a question reopens the
            // microphone rather than making the user press the key again.
            var turnTranscript = transcript;
            for (var turn = 0; ; turn++)
            {
                // ponytail: the follow-up turn reuses this hold's screenshots rather than capturing
                // again — a reply lands seconds later and answers a question about what is already
                // there. Re-capture per turn if that ever stops being true.
                var awaitingReply = await RunTurn(activation, turnTranscript, capturing, nowPlaying, releasedAt, transcriptAt, cancellationToken);

                // ponytail: one reply per hold. A longer exchange is what pressing the key is for,
                // and a loop with no floor is a microphone that reopens itself indefinitely.
                if (!awaitingReply || turn >= 1 || !Settings.MicrophoneCaptureEnabled)
                {
                    break;
                }

                var reply = await ListenForReply(activation, cancellationToken);
                if (reply.Text.Length == 0)
                {
                    _log.Information("Nothing came back after the question, so the turn ends here");
                    break;
                }

                TranscriptRecognized?.Invoke(reply.Text);
                (turnTranscript, releasedAt, transcriptAt) = (reply.Text, reply.ReleasedAt, reply.TranscriptAt);
            }

            Finish(activation);
        }
        catch (Exception failure) when (cancellationToken.IsCancellationRequested)
        {
            // Any exception, not just OperationCanceledException. Superseding an activation aborts
            // a websocket and an audio device mid-flight, and those layers surface the teardown as
            // whatever they happen to throw — the transcriber's send and receive share one socket,
            // so whichever notices the abort second reports a WebSocketException, not a
            // cancellation. That was logged as "Activation failed" and told the user "I can't
            // reach the answer service" about a request they had already replaced, while the
            // replacement was running perfectly well behind the message.
            _log.Information(failure, "Activation abandoned by a newer activation");
        }
        catch (Exception failure)
        {
            _log.Error(failure, "Activation failed");
            Post(FailureMessages.For(failure));
            Finish(activation);
        }
    }

    /// <summary>
    /// Reopens the microphone after Winly has asked the user a question, and returns what they said.
    ///
    /// Nothing heard is an empty string, which ends the exchange rather than asking again. The
    /// question is spoken to completion first, for the same reason a confirmation is: Winly's own
    /// voice is coming out of the speakers, and capturing over it transcribes the question as its
    /// own answer. And the window bounds the *capture*, never the transcription — a transcriber
    /// does not finalize a turn until the audio stream ends, so cancelling it throws the answer away
    /// instead of reading it.
    /// </summary>
    private async Task<(string Text, long ReleasedAt, long TranscriptAt)> ListenForReply(
        CancellationTokenSource activation,
        CancellationToken cancellationToken)
    {
        try
        {
            await _answerSpeech;
        }
        catch (Exception failure)
        {
            _log.Debug(failure, "The answer being spoken ended badly; listening anyway");
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var listening = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        listening.CancelAfter(ReplyWindow + TranscriptionGrace);
        try
        {
            // A real transition, unlike a confirmation's: this is the top of a second turn, and the
            // machine has to leave Speaking or the next turn's Speaking transition is illegal and
            // takes the whole activation down with "Activation failed". Speaking -> Listening ->
            // Working is legal from either state turn one can end in.
            Transition(activation, CompanionState.Listening);
            await _microphone.StopCapture();
            var audio = await _microphone.StartCapture(listening.Token);

            var lastPartialAt = Stopwatch.GetTimestamp();
            var heardSomething = false;
            var transcribing = _speechToText.Transcribe(
                audio,
                listening.Token,
                partial =>
                {
                    if (partial.Trim().Length == 0)
                    {
                        return;
                    }

                    heardSomething = true;
                    lastPartialAt = Stopwatch.GetTimestamp();
                });

            // ponytail: a 200 ms poll over the partials rather than real voice activity detection.
            // It only has to tell "still talking" from "finished", and the alternative is a second
            // turn detector that would then disagree with the transcriber's own.
            var deadline = Stopwatch.GetTimestamp();
            while (Stopwatch.GetElapsedTime(deadline) < ReplyWindow)
            {
                await Task.Delay(ReplyPollInterval, listening.Token);
                if (heardSomething && Stopwatch.GetElapsedTime(lastPartialAt) > QuietEndsAReply)
                {
                    break;
                }
            }

            var releasedAt = Stopwatch.GetTimestamp();
            await _microphone.StopCapture();
            var text = (await transcribing).Trim();
            _log.Information("Reply heard after the question: {Heard}", text.Length == 0 ? "nothing" : text);
            return (text, releasedAt, Stopwatch.GetTimestamp());
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.Information(failure, "Nothing usable was heard after the question");
            return (string.Empty, 0, 0);
        }
        finally
        {
            await _microphone.StopCapture();
            try
            {
                if (StateMachine.State == CompanionState.Listening)
                {
                    Transition(activation, CompanionState.Working);
                }
            }
            catch (OperationCanceledException)
            {
                // Superseded while the microphone was open; the newer activation owns the state.
            }
        }
    }

    /// <summary>
    /// One question and its answer: ask, speak, point, act, remember. Returns whether the answer
    /// asked the user something it needs a reply to before it can go on.
    /// </summary>
    private async Task<bool> RunTurn(
        CancellationTokenSource activation,
        string transcript,
        Task<IReadOnlyList<DisplayCapture>> capturing,
        Task<NowPlaying?> nowPlaying,
        long releasedAt,
        long transcriptAt,
        CancellationToken cancellationToken)
    {
        if (!Settings.ScreenCaptureEnabled)
        {
            Post("Screen capture is turned off in settings, so I answered without looking at your screen.");
        }

        var accumulator = new SentenceAccumulator();
        var speechQueue = Channel.CreateUnbounded<Task<SpokenAudio>>();
        var queuedAnySpeech = false;
        long firstDeltaAt = 0;

        // Everything already handed to synthesis, so each chunk knows what the voice just said
        // and carries its delivery across the seam instead of opening a fresh announcement.
        // Only ever touched from the SSE loop's own thread, so a plain local is enough.
        var spokenSoFar = new StringBuilder();

        void QueueSpeech(string sentence)
        {
            var spoken = AnswerTextSanitizer.Sanitize(sentence);
            if (spoken.Length == 0)
            {
                return;
            }

            queuedAnySpeech = true;
            var previous = spokenSoFar.Length == 0 ? null : spokenSoFar.ToString();
            spokenSoFar.Append(spoken).Append(' ');
            speechQueue.Writer.TryWrite(_textToSpeech.Synthesize(spoken, previous, cancellationToken));
        }

        // Synthesis of later sentences overlaps playback of earlier ones, so the answer
        // starts being spoken before the model has finished writing it (SC-001).
        var playbackStarted = false;
        var speaking = Task.Run(
            async () =>
            {
                await foreach (var synthesis in speechQueue.Reader.ReadAllAsync(cancellationToken))
                {
                    var spokenAudio = await synthesis;
                    if (!playbackStarted)
                    {
                        playbackStarted = true;
                        Transition(activation, CompanionState.Speaking);
                        // Split by stage, not just the total: three providers sit between the key
                        // and the first word, and without the split "make it faster" is a guess
                        // about which one to go at. -1 means the answer never streamed a delta.
                        _log.Information(
                            "activation.latency {ElapsedMilliseconds} ms from key release to playback start (transcript {TranscriptMs} ms, first token {FirstTokenMs} ms, speech {SpeechMs} ms)",
                            Stopwatch.GetElapsedTime(releasedAt).TotalMilliseconds,
                            Stopwatch.GetElapsedTime(releasedAt, transcriptAt).TotalMilliseconds,
                            firstDeltaAt == 0 ? -1 : Stopwatch.GetElapsedTime(transcriptAt, firstDeltaAt).TotalMilliseconds,
                            firstDeltaAt == 0 ? -1 : Stopwatch.GetElapsedTime(firstDeltaAt).TotalMilliseconds);
                    }

                    await _playback.Play(spokenAudio, cancellationToken);
                }
            },
            cancellationToken);
        _answerSpeech = speaking;

        void OnDelta(string delta)
        {
            if (firstDeltaAt == 0)
            {
                firstDeltaAt = Stopwatch.GetTimestamp();
            }

            AnswerDeltaReceived?.Invoke(delta);
            foreach (var sentence in accumulator.Append(delta))
            {
                QueueSpeech(sentence);
            }
        }

        ChatAnswer answer;
        IReadOnlyList<DisplayCapture> displays;
        try
        {
            (answer, displays) = await Ask(transcript, capturing, await nowPlaying, OnDelta, cancellationToken);

            var trailingText = accumulator.Flush();
            if (trailingText.Length > 0)
            {
                QueueSpeech(trailingText);
            }

            if (!queuedAnySpeech)
            {
                // A provider that returned the answer without streaming any delta: speak it whole.
                QueueSpeech(SpokenNarration(answer));
            }

            if (!queuedAnySpeech)
            {
                QueueSpeech(NoAnswerText);
            }
        }
        finally
        {
            speechQueue.Writer.TryComplete();
        }

        var (narration, target) = PointingDesignationParser.Parse(answer.Text, answer.PointingTarget);
        var (actionFreeNarration, actions) = DesktopActionParser.Parse(narration, answer.Actions);
        var spokenText = AnswerTextSanitizer.Sanitize(actionFreeNarration);
        if (spokenText.Length == 0)
        {
            spokenText = NoAnswerText;
        }

        var targetCapture = target is null ? null : displays.FirstOrDefault(display => display.MonitorId == target.MonitorId);
        if (target is not null && targetCapture is not null)
        {
            _ = _overlay.PointTo(CaptureToDesktopCoordinateMapper.Map(target, targetCapture), cancellationToken);
        }
        else
        {
            _overlay.ReturnToRest();
        }

        // Started before the answer finishes playing, so the app is opening as Winly says so.
        var sequence = await RunDesktopActions(actions, cancellationToken);

        await speaking;
        foreach (var outcome in sequence.Outcomes)
        {
            if (!string.IsNullOrWhiteSpace(outcome.FollowUpSpeech))
            {
                await Announce(outcome.FollowUpSpeech, cancellationToken);
            }
        }

        // Spoken, not just posted to the panel. The answer has already said "playing it" or
        // "opening it" out loud, so an action that then failed leaves the user with a claim and
        // no way to hear it retracted — the panel is not open, and this is the whole reason
        // "it says it is playing the video but it does not" looked like a lie rather than a
        // failure. Fail loud (Principle VII): whatever contradicts the answer is said aloud too.
        var unfinished = FailureMessages.ForSequence(sequence);
        if (unfinished.Length > 0)
        {
            await Announce(unfinished, cancellationToken);
        }

        // Same rule as a failed action: the answer has already said it is writing that down, so an
        // answer too broken to type is a claim the user has heard and nothing to contradict it.
        Session.Append(new Exchange(transcript, spokenText, targetCapture is null ? null : target, DateTimeOffset.UtcNow));
        return answer.AwaitingReply;
    }

    /// <summary>
    /// Asks once with only what the wording calls for, and again with whatever the model says it
    /// actually needed. The screenshots are the whole non-cached cost of a turn and the web tool's
    /// definition costs more than the prompt, so the common commands — pause, next, turn it down —
    /// pay almost nothing, and a wrong guess costs one extra round trip rather than a wrong answer.
    /// </summary>
    private async Task<(ChatAnswer Answer, IReadOnlyList<DisplayCapture> Displays)> Ask(
        string transcript,
        Task<IReadOnlyList<DisplayCapture>> capturing,
        NowPlaying? nowPlaying,
        Action<string> onDelta,
        CancellationToken cancellationToken)
    {
        var history = Session.Recent(HistoryExchangesSent);
        var canSeeScreen = Settings.ScreenCaptureEnabled;
        var sendScreen = canSeeScreen && ScreenContextHeuristic.NeedsScreen(transcript);
        var allowWeb = WebSearchHeuristic.NeedsWebSearch(transcript);

        var displays = sendScreen ? await capturing : [];
        var answer = await _chat.Ask(
            new ChatRequest(transcript, displays, history, nowPlaying, canSeeScreen && !sendScreen, allowWeb),
            onDelta,
            cancellationToken);

        var wantsScreen = answer.NeedsScreen && canSeeScreen && !sendScreen;
        var wantsWeb = answer.NeedsWebSearch && !allowWeb;
        if (!wantsScreen && !wantsWeb)
        {
            return (answer, displays);
        }

        _log.Information("Asking again with {What}", wantsScreen && wantsWeb ? "the screen and the web" : wantsScreen ? "the screen" : "the web");
        displays = wantsScreen ? await capturing : displays;
        var second = await _chat.Ask(
            new ChatRequest(transcript, displays, history, nowPlaying, ScreenAvailable: false, WebSearchAllowed: allowWeb || wantsWeb),
            onDelta,
            cancellationToken);
        return (second, displays);
    }

    /// <summary>The answer with every designation tag removed — what the user actually hears.</summary>
    private static string SpokenNarration(ChatAnswer answer)
    {
        var (narration, _) = PointingDesignationParser.Parse(answer.Text, answer.PointingTarget);
        return DesktopActionParser.Parse(narration, answer.Actions).SpokenText;
    }

    /// <summary>
    /// Asks about one consequential action and listens for the answer (FR-012).
    ///
    /// The question is spoken to completion *before* the microphone opens. Winly's own voice is
    /// coming out of the user's speakers, so capturing while it is still playing transcribes Winly
    /// asking the question — and the most likely thing to be transcribed is the prompt's own
    /// closing words, which is how "Should I go ahead?" ends up answering itself.
    /// </summary>
    private async Task<bool> ConfirmAloud(DesktopAction action, CancellationToken cancellationToken)
    {
        await Announce(ConsequentialActionClassifier.Ask(action), cancellationToken);

        if (!Settings.MicrophoneCaptureEnabled)
        {
            Post("I can't ask you about that with the microphone turned off, so I left it alone.");
            return false;
        }

        // Bounds the whole exchange rather than the answer. The window itself closes the microphone
        // below; this only stops a wedged transcriber from holding the sequence open forever.
        using var listening = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        listening.CancelAfter(ConfirmationWindow + TranscriptionGrace);
        try
        {
            // The overlay alone, not the state machine: this is a brief sub-step inside one
            // activation rather than a lifecycle change, and the indicator is what Principle II
            // actually requires while the microphone is open.
            _overlay.SetState(CompanionState.Listening);
            await _microphone.StopCapture();
            var audio = await _microphone.StartCapture(listening.Token);

            // "Yes" is decidable from the first partial the transcriber revises, so the window
            // stops being a wait and becomes only a deadline: the answer lands about as fast as
            // the user says it, instead of four seconds after. Without this the user says yes,
            // hears nothing, and says it again into a microphone that is still counting down.
            var decided = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var transcribing = _speechToText.Transcribe(
                audio,
                listening.Token,
                partial =>
                {
                    if (SpokenConfirmation.Decide(partial) is bool answer)
                    {
                        decided.TrySetResult(answer);
                    }
                });

            // The window ends the *capture*, exactly as releasing the key does on a normal turn —
            // it must not cancel the transcription. Cancelling threw the answer away instead of
            // reading it, and the transcriber only finalizes a turn once the audio stream ends, so
            // every confirmation timed out and every spoken "yes" was recorded as a refusal.
            var window = Task.Delay(ConfirmationWindow, listening.Token);
            Observe(window);
            await Task.WhenAny(decided.Task, window);
            await _microphone.StopCapture();

            if (decided.Task.IsCompletedSuccessfully)
            {
                // Heard it outright; the finalized transcript would only say the same thing a
                // round trip later. Cancelling here is what closes the socket that is still open.
                var heard = decided.Task.Result;
                _log.Information("Confirmation for {Kind} heard as {Answer}", action.Kind, heard ? "yes" : "no");
                listening.Cancel();
                Observe(transcribing);
                return heard;
            }

            // Nothing decidable inside the window: silence or something unrecognisable, both
            // refusals, which is what `== true` collapses them into (FR-012b). The transcript goes
            // in the log because a refusal and a misheard yes look identical from the outside.
            var answer = await transcribing;
            _log.Information("Confirmation for {Kind} heard as {Answer}", action.Kind, answer);
            return SpokenConfirmation.Decide(answer) == true;
        }
        catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Silence, an unusable microphone, or nothing recognisable: all refusals (FR-012b).
            _log.Information(failure, "No confirmation was heard for {Kind}", action.Kind);
            return false;
        }
        finally
        {
            await _microphone.StopCapture();
            _overlay.SetState(CompanionState.Working);
        }
    }

    private async Task<ActionSequenceResult> RunDesktopActions(
        IReadOnlyList<DesktopAction> actions,
        CancellationToken cancellationToken)
    {
        if (actions.Count == 0)
        {
            return ActionSequenceResult.Nothing;
        }

        try
        {
            // The acting permission is enforced inside the runner, per action, so that revoking it
            // mid-sequence is caught (FR-019) and so that refused attempts still reach the record
            // rather than vanishing before one exists (FR-022).
            var sequence = await _sequenceRunner.Run(actions, cancellationToken);
            foreach (var outcome in sequence.Outcomes)
            {
                _log.Information(
                    "Desktop action {Kind} on {Target}: {Status} {Reason}",
                    outcome.Action.Kind,
                    outcome.Action.Target,
                    outcome.Status,
                    outcome.UserFacingReason);
                _record.Append(ActionRecordEntry.From(outcome, DateTimeOffset.UtcNow));
            }

            return sequence;
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The answer is already being spoken; a failed action must not take it down (Principle VII).
            _log.Error(failure, "Desktop action sequence failed");
            Post(FailureMessages.For(failure));
            return ActionSequenceResult.Nothing;
        }
    }

    /// <summary>
    /// Awaiting later still rethrows; this only keeps a task abandoned by an early return from
    /// raising TaskScheduler.UnobservedTaskException.
    /// </summary>
    private static void Observe(Task task) =>
        _ = task.ContinueWith(static faulted => _ = faulted.Exception, TaskContinuationOptions.OnlyOnFaulted);

    private static async Task WaitForKeyRelease(Task keyReleased, CancellationToken cancellationToken)
    {
        // Key release or the 30 s cap, whichever comes first (FR-004).
        await Task.WhenAny(keyReleased, Task.Delay(MaxHoldDuration, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
    }

    private void Transition(CancellationTokenSource activation, CompanionState next)
    {
        lock (_gate)
        {
            if (_currentActivation != activation)
            {
                throw new OperationCanceledException(activation.Token);
            }

            StateMachine.TransitionTo(next);
        }

        _overlay.SetState(next);
    }

    private void Finish(CancellationTokenSource activation)
    {
        lock (_gate)
        {
            if (_currentActivation != activation || StateMachine.State == CompanionState.Idle)
            {
                return;
            }

            StateMachine.TransitionTo(CompanionState.Idle);
        }

        _overlay.SetState(CompanionState.Idle);
        _overlay.ReturnToRest();
    }

    private void Post(string message)
    {
        _log.Information("User message: {Message}", message);
        UserMessagePosted?.Invoke(message);
    }
}
