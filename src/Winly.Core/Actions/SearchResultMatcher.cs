using System.Text.RegularExpressions;

namespace Winly.Core.Actions;

/// <summary>
/// One row of a music app's search results, reduced to what choosing between them needs.
///
/// <paramref name="IsTrack"/> separates a single piece of music from an album, playlist or artist
/// row. <paramref name="IsAudio"/> then separates a plain track from the music-video row of the
/// same song: both play it, so both are eligible, but the audio one is preferred when they tie.
/// Treating video rows as ineligible is what used to make "play 3 Nights" play a different Dominic
/// Fike song entirely — Spotify offers that track as a music video and nothing else.
/// </summary>
public sealed record SearchResultRow(string Title, string Artist, bool IsTrack, bool IsAudio = true);

/// <summary>
/// Picks which search result the user meant.
///
/// This is the only part of driving another app's window that is decidable without that window
/// being open, so it is the only part that is unit-tested (Constitution Principle V).
/// </summary>
public static partial class SearchResultMatcher
{
    /// <summary>
    /// How much more an artist word counts than a title word.
    ///
    /// Covers advertise the original in their own title — "3 Nights (8-Bit Dominic Fike Emulation)"
    /// by 8-Bit Arcade — so on title words alone they match a request *better* than the real
    /// recording, which spends "dominic fike" on the artist field where a title-only score cannot
    /// see it. Whoever the track is actually by is the high-signal half of the request.
    /// </summary>
    private const int ArtistWeight = 3;

    /// <summary>
    /// The index of the row to play, or -1 when there are no songs to choose between.
    ///
    /// Scores the artist as well as the title, which is the whole difference between right and
    /// wrong: "the dark by dominic fike" against a list holding both his "Dark" and Chelsea Wolfe's
    /// "The Dark" matches one title word and two artist words on the first, and two title words and
    /// nothing else on the second. Title-only scoring confidently plays the wrong song.
    ///
    /// <paramref name="allowTopSongFallback"/> decides what happens when nothing shares a word with
    /// the request. It is off while the caller is still waiting for results to arrive, because a
    /// page that has not caught up yet looks exactly like a request nothing matches — and taking its
    /// top song plays the *previous* request's answer. Once the wait is spent it is turned on, and
    /// "put on some jazz" gets the app's own top-ranked song rather than a refusal.
    /// </summary>
    public static int Choose(string spokenQuery, IReadOnlyList<SearchResultRow> rows, bool allowTopSongFallback)
    {
        var asked = Words(spokenQuery);
        var best = -1;
        var bestScore = int.MinValue;
        var firstSong = -1;

        for (var index = 0; index < rows.Count; index++)
        {
            if (!rows[index].IsTrack)
            {
                // Albums, playlists and artist rows all carry a play control too, and playing a
                // whole album because its name matched is not what was asked for.
                continue;
            }

            firstSong = firstSong < 0 ? index : firstSong;

            var title = Words(rows[index].Title);
            var titleMatched = title.Count(asked.Contains);
            var artistMatched = Words(rows[index].Artist).Count(asked.Contains);
            if (titleMatched + artistMatched == 0)
            {
                continue;
            }

            // Words in the title the request never accounted for are evidence against, not neutral:
            // they are where "(8-Bit … Emulation)" and "[Karaoke Version]" live.
            var spare = title.Count - titleMatched;
            // The +1 breaks a tie between a song and its own music video in the audio row's favour.
            var score = titleMatched + (ArtistWeight * artistMatched) - spare + (rows[index].IsAudio ? 1 : 0);
            if (score > bestScore)
            {
                best = index;
                bestScore = score;
            }
        }

        return best >= 0 ? best : allowTopSongFallback ? firstSong : -1;
    }

    /// <summary>
    /// Words that carry no identity, dropped from both sides before scoring.
    ///
    /// "by" is the load-bearing one. Asked for "bohemian rhapsody by queen", a row actually titled
    /// "Bohemian Rhapsody (Originally Performed By Queen) [Karaoke Version]" matches the request
    /// word for word — including "by" and "queen" — and beats Queen's own recording, which has to
    /// spend "queen" on the artist instead. Counting connectors rewards titles for restating the
    /// question.
    /// </summary>
    private static readonly HashSet<string> Connectors =
        ["by", "the", "a", "an", "of", "on", "some", "play", "song", "track", "please"];

    /// <summary>A run of letters or digits. Anything else — punctuation, spacing, the bullet in a
    /// row's subtitle — separates one word from the next rather than belonging to either.</summary>
    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    /// <summary>Lowercased alphanumeric words, minus the connectors: punctuation and filler differ
    /// between what is said and what is displayed.</summary>
    private static HashSet<string> Words(string text) =>
        Word().Matches(text.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(word => !Connectors.Contains(word))
            .ToHashSet();
}
