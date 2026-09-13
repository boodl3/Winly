namespace Winly.Core.Actions;

/// <summary>
/// What became of one action in a sequence.
///
/// <see cref="Failed"/> and <see cref="Declined"/> are deliberately distinct even though both
/// stop the sequence and both leave the user with nothing having happened: the action record has
/// to tell them apart (FR-023), because "I couldn't find Spotify" and "you said no" are different
/// events that happen to look the same from outside.
/// </summary>
public enum ActionOutcomeStatus
{
    /// <summary>Carried out.</summary>
    Completed,

    /// <summary>Could not be carried out, including exceeding its own time bound.</summary>
    Failed,

    /// <summary>The user refused, or the confirmation window passed unanswered (FR-012b).</summary>
    Declined,

    /// <summary>Never ran, because an earlier action stopped the sequence.</summary>
    Abandoned,

    /// <summary>Never ran, because acting is turned off in settings (FR-018, FR-019).</summary>
    Blocked,
}

/// <summary>One action and what became of it.</summary>
/// <param name="UserFacingReason">
/// Already user-facing: plain language, no error codes and no exception names (FR-008). Empty when
/// the action completed.
/// </param>
/// <param name="FollowUpSpeech">What the action wants said afterward, or null when it speaks for itself.</param>
public sealed record ActionOutcome(
    DesktopAction Action,
    ActionOutcomeStatus Status,
    string UserFacingReason = "",
    string? FollowUpSpeech = null)
{
    /// <summary>
    /// Whether reaching this outcome means nothing later in the sequence may run (FR-007).
    /// <see cref="ActionOutcomeStatus.Abandoned"/> is not a stopping outcome: it is what the
    /// remaining actions are marked with once something else has already stopped them.
    /// </summary>
    public bool StopsSequence => Status is ActionOutcomeStatus.Failed
        or ActionOutcomeStatus.Declined
        or ActionOutcomeStatus.Blocked;

    public static ActionOutcome Completed(DesktopAction action, string? followUpSpeech = null) =>
        new(action, ActionOutcomeStatus.Completed, string.Empty, followUpSpeech);

    public static ActionOutcome Failed(DesktopAction action, string userFacingReason) =>
        new(action, ActionOutcomeStatus.Failed, userFacingReason);

    public static ActionOutcome Declined(DesktopAction action, string userFacingReason) =>
        new(action, ActionOutcomeStatus.Declined, userFacingReason);

    public static ActionOutcome Abandoned(DesktopAction action) =>
        new(action, ActionOutcomeStatus.Abandoned, "it was not reached");

    public static ActionOutcome Blocked(DesktopAction action) =>
        new(action, ActionOutcomeStatus.Blocked, "Desktop actions are turned off in settings, so I only answered.");
}
