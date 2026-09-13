using System.Text.RegularExpressions;

namespace Winly.Core.Companion;

/// <summary>
/// Decides whether to attach the web search tool to a question.
///
/// The tool's definition costs about 2,700 input tokens on every single turn it is attached to —
/// more than the rest of the prompt — and almost no question needs it. So it is left off unless
/// the wording is about something that changes, and the model can ask for it with a designation
/// if that turns out to be wrong, exactly as it can ask for the screen.
/// </summary>
public static partial class WebSearchHeuristic
{
    public static bool NeedsWebSearch(string transcript) => AsksAboutSomethingThatChanges().IsMatch(transcript);

    [GeneratedRegex(
        @"\b(weather|forecast|news|headlines|today|tonight|tomorrow|yesterday|right now|currently|latest|newest|recent|this (week|month|year)|price|stock|share price|exchange rate|score|who won|standings|fixtures|release date|when (is|does|did)|how much (is|does)|open now|opening hours|traffic|flight|delayed)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksAboutSomethingThatChanges();
}
