using Winly.Core.Pointing;

namespace Winly.Core.Companion;

/// <summary>One user utterance and the spoken answer produced for it (designation already stripped).</summary>
public sealed record Exchange(
    string Transcript,
    string AnswerText,
    PointingTarget? PointingTarget,
    DateTimeOffset CreatedAtUtc);
