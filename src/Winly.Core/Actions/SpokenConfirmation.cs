using System.Text.RegularExpressions;

namespace Winly.Core.Actions;

/// <summary>
/// Reads a spoken yes or no to a confirmation prompt (FR-012).
///
/// Anything that is not recognisably an agreement is a refusal — silence, a half-sentence, someone
/// else in the room, the television. That asymmetry is the whole point: this is the only gate in
/// front of actions that cannot be undone, so a false refusal costs a repeated sentence and a
/// false agreement costs unsaved work.
/// </summary>
public static partial class SpokenConfirmation
{
    /// <summary>
    /// How long the microphone stays open waiting for the answer (FR-012b). Short, because it is
    /// open on the user's desktop and because someone who is going to answer answers quickly.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private static readonly string[] Agreements =
    [
        "yes", "yeah", "yep", "yup", "sure", "okay", "ok", "alright",
        "go ahead", "go for it", "do it", "confirm", "please do", "that's fine",
    ];

    private static readonly string[] Refusals =
    [
        "no", "nope", "nah", "dont", "do not", "cancel", "stop",
        "never mind", "nevermind", "leave it", "forget it", "wait",
    ];

    /// <summary>True only for a clear yes. Everything else, including nothing at all, is a no.</summary>
    public static bool IsAgreement(string? transcript)
    {
        var words = Normalise(transcript);
        if (words.Length == 0)
        {
            return false;
        }

        // Refusals are tested first because a refusal often contains an agreement phrase inside it:
        // "no, don't do it" ends with "do it".
        return !ContainsAny(words, Refusals) && ContainsAny(words, Agreements);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    /// <summary>
    /// Lower-cased words with punctuation removed, so "Yes!" and "yes," both read as agreement and
    /// "now" is never mistaken for "no".
    /// </summary>
    private static string[] Normalise(string? transcript) =>
        Word().Matches((transcript ?? string.Empty).ToLowerInvariant()).Select(match => match.Value).ToArray();

    /// <summary>Whole-word matching, so a phrase matches only as consecutive whole words.</summary>
    private static bool ContainsAny(string[] words, string[] phrases)
    {
        foreach (var phrase in phrases)
        {
            var phraseWords = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var start = 0; start + phraseWords.Length <= words.Length; start++)
            {
                if (words.Skip(start).Take(phraseWords.Length).SequenceEqual(phraseWords, StringComparer.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
