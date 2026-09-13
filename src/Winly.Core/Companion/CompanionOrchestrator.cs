using System.Diagnostics;
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
    private readonly ILogger _log;
    private readonly Lock _gate = new();

    private CancellationTokenSource? _currentActivation;
    private TaskCompletionSource? _keyReleased;

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
        ConversationSession session,
        UserSettings settings,
        ILogger log)
    {
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
        _log = log;
        _reminders.Elapsed += OnReminderElapsed;
    }

    public CompanionStateMachine StateMachine { get; } = new();

    public ConversationSession Session { get; }

    public UserSettings Settings { get; set; }

    internal Task CurrentRun { get; private set; } = Task.CompletedTask;

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
        Post(text);
        var spoken = AnswerTextSanitizer.Sanitize(text);
        if (spoken.Length == 0)
        {
            return;
        }

        var audio = await _textToSpeech.Synthesize(spoken, cancellationToken);
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

            try
            {
                await WaitForKeyRelease(keyReleased, cancellationToken);
            }
            finally
            {
                await _microphone.StopCapture();
            }

            var releasedAt = Stopwatch.GetTimestamp();
            var transcript = (await transcription).Trim();
            TranscriptRecognized?.Invoke(transcript);
            if (transcript.Length == 0)
            {
                // Nothing intelligible was said: no request is issued (FR-006).
                Finish(activation);
                return;
            }

            Transition(activation, CompanionState.Working);

            if (!Settings.ScreenCaptureEnabled)
            {
                Post("Screen capture is turned off in settings, so I answered without looking at your screen.");
            }

            var accumulator = new SentenceAccumulator();
            var speechQueue = Channel.CreateUnbounded<Task<SpokenAudio>>();
            var queuedAnySpeech = false;

            void QueueSpeech(string sentence)
            {
                var spoken = AnswerTextSanitizer.Sanitize(sentence);
                if (spoken.Length == 0)
                {
                    return;
                }

                queuedAnySpeech = true;
                speechQueue.Writer.TryWrite(_textToSpeech.Synthesize(spoken, cancellationToken));
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
                            _log.Information("activation.latency {ElapsedMilliseconds} ms from key release to playback start",
                                Stopwatch.GetElapsedTime(releasedAt).TotalMilliseconds);
                        }

                        await _playback.Play(spokenAudio, cancellationToken);
                    }
                },
                cancellationToken);

            void OnDelta(string delta)
            {
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
            var (actionFreeNarration, action) = DesktopActionParser.Parse(narration, answer.Action);
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
            var followUp = action is null ? null : await RunDesktopAction(action, cancellationToken);

            await speaking;
            if (!string.IsNullOrWhiteSpace(followUp))
            {
                await Announce(followUp, cancellationToken);
            }

            Session.Append(new Exchange(transcript, spokenText, targetCapture is null ? null : target, DateTimeOffset.UtcNow));
            Finish(activation);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _log.Information("Activation abandoned by a newer activation");
        }
        catch (Exception failure)
        {
            _log.Error(failure, "Activation failed");
            Post(FailureMessages.For(failure));
            Finish(activation);
        }
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
        return DesktopActionParser.Parse(narration, answer.Action).SpokenText;
    }

    private async Task<string?> RunDesktopAction(DesktopAction action, CancellationToken cancellationToken)
    {
        if (!Settings.DesktopActionsEnabled)
        {
            Post("Desktop actions are turned off in settings, so I only answered.");
            return null;
        }

        try
        {
            if (action.Kind == DesktopActionKind.Timer)
            {
                // Kept here rather than in the platform layer: announcing it needs the voice.
                _reminders.Schedule(TimeSpan.FromSeconds(action.Amount), action.Target);
                _log.Information("Timer set for {Seconds}s: {Label}", action.Amount, action.Target);
                return null;
            }

            return await _actions.Run(action, cancellationToken);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // The answer is already being spoken; a failed action must not take it down (Principle VII).
            _log.Error(failure, "Desktop action {Kind} on {Target} failed", action.Kind, action.Target);
            Post(FailureMessages.For(failure));
            return null;
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
