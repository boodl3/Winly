using System.Text.RegularExpressions;

namespace Winly.Core.Actions;

/// <summary>
/// Picks which on-screen control the user meant, out of the labels an app's accessibility tree
/// offers.
///
/// Same split as <see cref="SearchResultMatcher"/> and for the same reason (Constitution Principle
/// V): walking the tree needs the window open, but choosing between labels does not, so the choice
/// is the half that gets tested.
///
/// The scoring is deliberately unlike the music one. There, a title with words the request never
/// asked for is evidence against — that is where "[Karaoke Version]" lives. Here it is the
/// opposite: a video is titled "Procreate Tutorial for Beginners — Full Walkthrough" and a
/// sidebar chip is titled exactly "Procreate", so penalising spare words hands the request to the
/// chip. Extra words only ever break a tie.
/// </summary>
public static partial class ScreenElementMatcher
{
    /// <summary>
    /// The index of the control to press, or -1 when nothing shares a word with the request.
    ///
    /// Never falls back to "the first plausible thing": pressing the wrong control on someone's
    /// screen is not recoverable the way playing the wrong song is, so no match means no click and
    /// the user is told so.
    /// </summary>
    public static int Choose(string spokenLabel, IReadOnlyList<string> labels)
    {
        var wanted = Squash(spokenLabel);
        var asked = Words(wanted);
        if (asked.Count == 0)
        {
            return -1;
        }

        var best = -1;
        var bestScore = 0;
        for (var index = 0; index < labels.Count; index++)
        {
            var squashed = Squash(labels[index]);
            var words = Words(squashed);
            var matched = words.Count(asked.Contains);
            if (matched == 0)
            {
                continue;
            }

            // Matched words dominate; the rest only separates two controls that matched equally.
            // The spare-word penalty is capped below ten so it can never outweigh one more word.
            var score = (100 * matched)
                + (squashed == wanted ? 50 : 0)
                + (squashed.Contains(wanted, StringComparison.Ordinal) ? 25 : 0)
                - Math.Min(words.Count - matched, 9);

            if (score > bestScore)
            {
                (best, bestScore) = (index, score);
            }
        }

        return best;
    }

    /// <summary>
    /// Words that say nothing about which control is meant. Shorter than the music list on
    /// purpose: "play" and "next" are what buttons are actually called, so dropping them here
    /// would throw away the most identifying word in "click the play button".
    /// </summary>
    private static readonly HashSet<string> Connectors =
        ["the", "a", "an", "of", "on", "please", "click", "press", "tap", "select", "that", "this", "it"];

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotAWord();

    private static string Squash(string text) => NotAWord().Replace(text.ToLowerInvariant(), " ").Trim();

    private static HashSet<string> Words(string squashed) =>
        squashed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => !Connectors.Contains(word))
            .ToHashSet();
}
