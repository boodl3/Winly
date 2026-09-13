using Winly.Core.Actions;

namespace Winly.Core.Tests.Actions;

public class SearchResultMatcherTests
{
    /// <summary>The real results for "the dark dominic fike", in the order Spotify ranked them.</summary>
    private static readonly SearchResultRow[] Results =
    [
        new("Dark", "Dominic Fike", IsTrack: true),
        new("studying like oppenheimer", "carolina <3", IsTrack: false),
        new("Rocket", "Dominic Fike", IsTrack: false),
        new("The Dark", "Chelsea Wolfe", IsTrack: true),
        new("The Dark Prince", "Mac DeMarco", IsTrack: true),
    ];

    [Fact]
    public void TheArtistDecidesBetweenTwoSimilarTitles()
    {
        // Title-only scoring picks Chelsea Wolfe's "The Dark" here, which is the bug this exists for.
        Assert.Equal(0, SearchResultMatcher.Choose("the dark by dominic fike", Results, allowTopSongFallback: true));
    }

    [Fact]
    public void MatchesOnTitleAloneWhenNoArtistWasNamed()
    {
        Assert.Equal(3, SearchResultMatcher.Choose("play the dark by chelsea wolfe", Results, allowTopSongFallback: true));
    }

    [Fact]
    public void NeverChoosesAnAlbumOrPlaylist()
    {
        // "Rocket" is an album row and the only thing sharing a word with the request.
        Assert.Equal(0, SearchResultMatcher.Choose("rocket", Results, allowTopSongFallback: true));
    }

    [Fact]
    public void FallsBackToTheTopRankedSongWhenNothingMatches()
    {
        // "put on some jazz" shares no word with any title; the app already did the ranking.
        Assert.Equal(0, SearchResultMatcher.Choose("some jazz", Results, allowTopSongFallback: true));
    }

    [Fact]
    public void PrefersTheTighterTitleOnATie()
    {
        SearchResultRow[] rows =
        [
            new("Dark Times", "Someone", IsTrack: true),
            new("Dark", "Someone", IsTrack: true),
        ];

        Assert.Equal(1, SearchResultMatcher.Choose("play dark", rows, allowTopSongFallback: true));
    }

    [Fact]
    public void ReportsNothingToPlayWhenNoRowIsASong()
    {
        SearchResultRow[] rows = [new("Chill Mix", "Spotify", IsTrack: false)];

        Assert.Equal(-1, SearchResultMatcher.Choose("chill mix", rows, allowTopSongFallback: true));
    }

    [Fact]
    public void ACoverCannotWinByRestatingTheRequestInItsTitle()
    {
        // The real results for "bohemian rhapsody by queen": the karaoke row's title contains every
        // word of the request, "by" and "queen" included, while Queen's own spends "queen" on the
        // artist. Counting connectors hands it to the karaoke version.
        SearchResultRow[] rows =
        [
            new("Bohemian Rhapsody (Originally Performed By Queen) [Karaoke Version]", "Paris Music", IsTrack: true),
            new("Bohemian Rhapsody", "Queen", IsTrack: true),
        ];

        Assert.Equal(1, SearchResultMatcher.Choose("bohemian rhapsody by queen", rows, allowTopSongFallback: true));
    }

    [Fact]
    public void ACoverCannotWinByNamingTheOriginalArtistInItsTitle()
    {
        // The real results for "3 nights by dominic fike". The chiptune cover matches every word of
        // the request in its title; the real track spends half of them on the artist field.
        SearchResultRow[] rows =
        [
            new("3 Nights (8-Bit Dominic Fike Emulation)", "8-Bit Arcade", IsTrack: true),
            new("3 Nights", "Dominic Fike", IsTrack: true),
        ];

        Assert.Equal(1, SearchResultMatcher.Choose("3 nights by dominic fike", rows, allowTopSongFallback: true));
    }

    [Fact]
    public void WithoutTheFallbackAResultsPageThatHasNotCaughtUpIsRejected()
    {
        // Results still showing the previous request. Taking the top song here is how "play 3 Nights"
        // ends up playing whatever the last request asked for.
        SearchResultRow[] stale = [new("Weightless", "Marconi Union", IsTrack: true)];

        Assert.Equal(-1, SearchResultMatcher.Choose("3 nights by dominic fike", stale, allowTopSongFallback: false));
        Assert.Equal(0, SearchResultMatcher.Choose("3 nights by dominic fike", stale, allowTopSongFallback: true));
    }

    [Fact]
    public void AMusicVideoRowIsPlayableButLosesToTheAudioRowOfTheSameSong()
    {
        SearchResultRow[] both =
        [
            new("3 Nights", "Dominic Fike", IsTrack: true, IsAudio: false),
            new("3 Nights", "Dominic Fike", IsTrack: true, IsAudio: true),
        ];
        Assert.Equal(1, SearchResultMatcher.Choose("3 nights dominic fike", both, allowTopSongFallback: false));

        // ...but when the video row is all Spotify offers, it beats playing a different song.
        SearchResultRow[] videoOnly =
        [
            new("7 Hours", "Dominic Fike", IsTrack: true, IsAudio: true),
            new("3 Nights", "Dominic Fike", IsTrack: true, IsAudio: false),
        ];
        Assert.Equal(1, SearchResultMatcher.Choose("3 nights dominic fike", videoOnly, allowTopSongFallback: false));
    }
}
