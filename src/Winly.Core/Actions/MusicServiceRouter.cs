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
    // Longest names first: "youtube music" has to win over "youtube", which is a substring of it.
    private static readonly (string Name, string SpokenName, string SearchUrl)[] Services =
    [
        ("youtube music", "YouTube Music", "https://music.youtube.com/search?q={0}"),
        ("ytmusic", "YouTube Music", "https://music.youtube.com/search?q={0}"),
        ("apple music", "Apple Music", "https://music.apple.com/search?term={0}"),
        ("amazon music", "Amazon Music", "https://music.amazon.com/search/{0}"),
        ("soundcloud", "SoundCloud", "https://soundcloud.com/search/sounds?q={0}"),
        ("bandcamp", "Bandcamp", "https://bandcamp.com/search?q={0}"),
        ("deezer", "Deezer", "https://www.deezer.com/search/{0}"),
        ("tidal", "Tidal", "https://tidal.com/search?q={0}"),
        ("youtube", "YouTube", "https://www.youtube.com/results?search_query={0}"),
        ("yt", "YouTube", "https://www.youtube.com/results?search_query={0}"),
    ];

    /// <summary>
    /// Where to send <paramref name="spokenQuery"/> for the service the user named, or null when
    /// they named Spotify, named nothing, or named something not on the list — all three of which
    /// mean "the default", because a service Winly has never heard of is not a reason to refuse a
    /// request that would have worked.
    /// </summary>
    public static (string SpokenName, string Url)? Route(string? service, string spokenQuery)
    {
        var named = (service ?? string.Empty).Trim().ToLowerInvariant();
        if (named.Length == 0)
        {
            return null;
        }

        foreach (var (name, spokenName, searchUrl) in Services)
        {
            if (named.Contains(name, StringComparison.Ordinal))
            {
                return (spokenName, string.Format(searchUrl, Uri.EscapeDataString(spokenQuery)));
            }
        }

        return null;
    }
}
