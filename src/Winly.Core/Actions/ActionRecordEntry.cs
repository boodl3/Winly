namespace Winly.Core.Actions;

/// <summary>
/// One line of the record the user can review, independently of whatever was spoken at the time
/// (FR-022). Metadata only: what it was, what it acted on, what became of it, and when.
///
/// Nothing captured ever lands here — no screen images, no audio, no transcript, no clipboard
/// contents, and never the text of a <see cref="DesktopActionKind.Type"/> action (FR-026). The
/// record exists to answer "why did that happen", which needs the verb and not the payload; a file
/// on disk listing everything the user dictated would be a worse problem than the one it solves.
/// </summary>
public sealed record ActionRecordEntry(
    DateTimeOffset TimestampUtc,
    string Verb,
    string Target,
    string Argument,
    string Status,
    string Reason)
{
    /// <summary>Stands in for typed text, which is never written down.</summary>
    public const string RedactedText = "(text)";

    public static ActionRecordEntry From(ActionOutcome outcome, DateTimeOffset at) => new(
        at,
        outcome.Action.Kind.ToString(),
        outcome.Action.Kind == DesktopActionKind.Type ? RedactedText : outcome.Action.Target,
        outcome.Action.Argument,
        outcome.Status.ToString(),
        outcome.UserFacingReason);
}

/// <summary>
/// The durable record of what Winly did. Implementations must never throw from
/// <see cref="Append"/>: failing to write a line is not a reason to fail the action it describes,
/// or to take down an answer already being spoken (Principle VII).
/// </summary>
public interface IActionRecord
{
    void Append(ActionRecordEntry entry);

    /// <summary>Newest first, so the panel shows what just happened without scrolling.</summary>
    IReadOnlyList<ActionRecordEntry> Read();

    void Clear();
}
