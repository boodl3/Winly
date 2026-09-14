namespace Winly.Core.Actions;

/// <summary>
/// Which service a spoken "…on YouTube" means, and where its search lives.
///
/// Spotify is the one service Winly drives from the inside, through the accessibility tree of an
/// app that is already signed in. Nothing else offers that for free: a second music app is a
/// second window layout to learn, and the web player is the one surface every service actually
/// has. So everything else opens that service's own search in the user's default browser — which
/// is what a person without the login would do — and Winly says so rather than pretending the
/// track is playing.
///
/// Lives in Core because deciding which service was named is decidable with nothing running,
/// unlike opening it (Constitution Principle V).
/// </summary>
public static class MusicServiceRouter
{
    // Matched on the spoken name itself: the Worker's prompt enumerates exactly these, and the
    // model copies one into the argument. Longest first, so "YouTube Music" beats "YouTube".
    private static readonly (string SpokenName, string SearchUrl)[] Services =
    [
        ("YouTube Music", "https://music.youtube.com/search?q={0}"),
        ("Apple Music", "https://music.apple.com/search?term={0}"),
        ("Amazon Music", "https://music.amazon.com/search/{0}"),
        ("SoundCloud", "https://soundcloud.com/search/sounds?q={0}"),
        ("Bandcamp", "https://bandcamp.com/search?q={0}"),
        ("Deezer", "https://www.deezer.com/search/{0}"),
        ("Tidal", "https://tidal.com/search?q={0}"),
        ("YouTube", "https://www.youtube.com/results?search_query={0}"),
    ];

    /// <summary>
    /// Where to send <paramref name="spokenQuery"/> for the service the user named, or null when
    /// they named Spotify, named nothing, or named something not on the list — all three of which
    /// mean "the default", because a service Winly has never heard of is not a reason to refuse a
    /// request that would have worked.
    /// </summary>
    public static (string SpokenName, string Url)? Route(string? service, string spokenQuery)
    {
        var named = (service ?? string.Empty).Trim();
        if (named.Length == 0)
        {
            return null;
        }

        foreach (var (spokenName, searchUrl) in Services)
        {
            if (named.Contains(spokenName, StringComparison.OrdinalIgnoreCase))
            {
                return (spokenName, string.Format(searchUrl, Uri.EscapeDataString(spokenQuery)));
            }
        }

        return null;
    }
}
