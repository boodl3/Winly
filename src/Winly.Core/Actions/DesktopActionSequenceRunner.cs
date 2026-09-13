using Winly.Core.Companion;

namespace Winly.Core.Actions;

/// <summary>
/// How long a single action may take before it is treated as having failed.
///
/// These are tuning values rather than design invariants (FR-005b): changing them must not
/// require changing how a sequence is carried out. There is deliberately no aggregate bound on
/// the sequence — a request that legitimately needs two cold application launches must not be cut
/// off because the launches were slow, and a bound per action detects something genuinely stuck
/// sooner than a sequence-wide budget would.
/// </summary>
public sealed record ActionBounds(TimeSpan ApplicationReady, TimeSpan SingleAction)
{
    public static readonly ActionBounds Default = new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10));
}

/// <param name="StoppedAtActionLimit">
/// The request asked for more actions than one request may carry, so the tail was never attempted
/// and the user has to be told the task is unfinished (FR-003).
/// </param>
public sealed record ActionSequenceResult(IReadOnlyList<ActionOutcome> Outcomes, bool StoppedAtActionLimit)
{
    public static readonly ActionSequenceResult Nothing = new([], false);

    /// <summary>Everything that was asked for happened.</summary>
    public bool EverythingCompleted => !StoppedAtActionLimit
        && Outcomes.All(outcome => outcome.Status == ActionOutcomeStatus.Completed);

    /// <summary>The outcome that stopped the sequence, or null when nothing did.</summary>
    public ActionOutcome? Stopper => Outcomes.FirstOrDefault(outcome => outcome.StopsSequence);
}

/// <summary>
/// Carries out every action of one request, in the order the model declared them.
///
/// This is a separate type rather than a loop inside <see cref="CompanionOrchestrator"/> because
/// ordering, bounding, readiness-waiting and outcome collection are the part of this feature that
/// most needs tests, and the orchestrator is the part hardest to test.
/// </summary>
/// <param name="confirm">
/// Asks the user about one consequential action and returns whether they agreed. Injected rather
/// than built in, so the runner can be exercised headless: in the application it speaks the
/// question and listens, and in tests it is a function returning true or false. Null means nothing
/// can be asked, which is treated as a refusal rather than as permission.
/// </param>
/// <param name="actingEnabled">
/// Read again before every action rather than once per request, so turning acting off part way
/// through a sequence stops it at the next boundary (FR-019).
/// </param>
public sealed class DesktopActionSequenceRunner(
    IDesktopActions desktopActions,
    ReminderScheduler reminders,
    ActionBounds bounds,
    Func<DesktopAction, CancellationToken, Task<bool>>? confirm = null,
    Func<bool>? actingEnabled = null)
{
    /// <summary>
    /// One request may carry at most this many actions (FR-003). The Worker caps its own output
    /// too, but this is the cap that counts: nothing the model sends is trusted about its length.
    ///
    /// Raised from five because one hold is one utterance and people list several jobs in it —
    /// "open Spotify, put on some jazz, throw Chrome on the left and turn dark mode on" is already
    /// four. It stays a runaway guard rather than a design limit: each action is still bounded
    /// individually, and going past this tells the user the tail did not happen rather than
    /// silently dropping it.
    /// </summary>
    public const int MaxActionsPerRequest = 10;

    private const string SpotifyAppName = "Spotify";

    public Task<ActionSequenceResult> Run(IReadOnlyList<DesktopAction> requested, CancellationToken cancellationToken) =>
        requested.Count == 0
            ? Task.FromResult(ActionSequenceResult.Nothing)
            : RunPlanned(requested, cancellationToken);

    private async Task<ActionSequenceResult> RunPlanned(IReadOnlyList<DesktopAction> requested, CancellationToken cancellationToken)
    {
        var stoppedAtActionLimit = requested.Count > MaxActionsPerRequest;
        var planned = stoppedAtActionLimit ? requested.Take(MaxActionsPerRequest).ToArray() : requested;

        var outcomes = new List<ActionOutcome>(planned.Count);
        var launchedInThisSequence = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stopped = false;

        foreach (var action in planned)
        {
            // A newer activation abandons what is left of this sequence, checked between actions so
            // nothing is interrupted halfway through (consistent with 001's FR-015).
            if (stopped || cancellationToken.IsCancellationRequested)
            {
                stopped = true;
                outcomes.Add(ActionOutcome.Abandoned(action));
                continue;
            }

            // Checked here rather than once at the top, so revoking acting mid-sequence takes
            // effect before the next action instead of after the whole request (FR-019).
            if (actingEnabled is not null && !actingEnabled())
            {
                stopped = true;
                outcomes.Add(ActionOutcome.Blocked(action));
                continue;
            }

            var outcome = await RunOne(action, launchedInThisSequence, cancellationToken);
            outcomes.Add(outcome);

            if (outcome.StopsSequence)
            {
                stopped = true;
                continue;
            }

            if (action.Kind == DesktopActionKind.Open && !LooksLikeUrl(action.Target))
            {
                launchedInThisSequence.Add(action.Target);
            }
        }

        return new ActionSequenceResult(outcomes, stoppedAtActionLimit);
    }

    private async Task<ActionOutcome> RunOne(
        DesktopAction action,
        HashSet<string> launchedInThisSequence,
        CancellationToken cancellationToken)
    {
        var dependency = ApplicationThisActionNeeds(action);
        if (dependency.Length > 0 && launchedInThisSequence.Contains(dependency))
        {
            var ready = await desktopActions.WaitForApplicationReady(dependency, bounds.ApplicationReady, cancellationToken);
            if (!ready)
            {
                return ActionOutcome.Failed(action, $"{dependency} didn't finish starting, so I stopped there");
            }
        }

        // Asked before the bound below starts, because the time the user takes to answer is their
        // clock and not Winly's. Agreement covers this action only and is never carried to the next
        // one, nor remembered past this sequence (FR-013).
        if (ConsequentialActionClassifier.IsConsequential(action))
        {
            if (confirm is null)
            {
                return ActionOutcome.Declined(action, $"I couldn't check with you about {ConsequentialActionClassifier.Describe(action)}, so I left it");
            }

            if (!await confirm(action, cancellationToken))
            {
                return ActionOutcome.Declined(action, $"you said no to {ConsequentialActionClassifier.Describe(action)}");
            }
        }

        if (action.Kind == DesktopActionKind.Timer)
        {
            // Kept here rather than in the platform layer: a timer is announced with the voice,
            // and there is nothing on the desktop to change.
            reminders.Schedule(TimeSpan.FromSeconds(action.Amount), action.Target);
            return ActionOutcome.Completed(action);
        }

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(bounds.SingleAction);
        try
        {
            return ActionOutcome.Completed(action, await desktopActions.Run(action, bounded.Token));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ActionOutcome.Abandoned(action);
        }
        catch (OperationCanceledException)
        {
            return ActionOutcome.Failed(action, "it took too long, so I stopped there");
        }
        catch (Exception failure)
        {
            return ActionOutcome.Failed(action, FailureMessages.For(failure));
        }
    }

    /// <summary>
    /// The application an action cannot be carried out without, when there is one. Only used to
    /// decide whether to wait for something this same request launched — an app that was already
    /// running needs no wait.
    /// </summary>
    private static string ApplicationThisActionNeeds(DesktopAction action) => action.Kind switch
    {
        DesktopActionKind.Window or DesktopActionKind.Volume => action.Target,
        // A click names its app in the argument, and may name no app at all.
        DesktopActionKind.Click => action.Argument,
        DesktopActionKind.Play or DesktopActionKind.Queue or DesktopActionKind.Media => SpotifyAppName,
        _ => string.Empty,
    };

    /// <summary>A URL opens in the browser and launches no app worth waiting for by name.</summary>
    private static bool LooksLikeUrl(string target) =>
        target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
