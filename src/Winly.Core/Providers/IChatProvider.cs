using Winly.Core.Actions;
using Winly.Core.Companion;
using Winly.Core.Pointing;

namespace Winly.Core.Providers;

/// <param name="Displays">Empty when the screenshots were withheld or capture is off.</param>
/// <param name="ScreenAvailable">
/// True when screenshots exist but were withheld to save tokens, which is what lets the model ask
/// for them. False when capture is off or failed, so it knows not to bother asking.
/// </param>
public sealed record ChatRequest(
    string Transcript,
    IReadOnlyList<DisplayCapture> Displays,
    IReadOnlyList<Exchange> History,
    NowPlaying? NowPlaying = null,
    bool ScreenAvailable = false,
    bool WebSearchAllowed = false);

/// <param name="NeedsScreen">The model declined to answer without the screenshots it was not sent.</param>
/// <param name="NeedsWebSearch">The model declined to answer without the web tool it was not given.</param>
public sealed record ChatAnswer(
    string Text,
    PointingTarget? PointingTarget,
    DesktopAction? Action = null,
    bool NeedsScreen = false,
    bool NeedsWebSearch = false);

public interface IChatProvider
{
    /// <summary>Streams answer text through <paramref name="onAnswerDelta"/> and returns the whole answer at the end.</summary>
    Task<ChatAnswer> Ask(ChatRequest request, Action<string> onAnswerDelta, CancellationToken cancellationToken);
}
