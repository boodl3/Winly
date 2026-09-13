using Winly.Core.Actions;

namespace Winly.Core.Tests.Actions;

/// <summary>
/// "Play X on YouTube" opened Spotify and played something else. Which service a spoken name means
/// is the half of that decidable with nothing running, so it is the half worth pinning here.
/// </summary>
public class MusicServiceRouterTests
{
    [Theory]
    [InlineData("YouTube", "youtube.com/results")]
    [InlineData("youtube music", "music.youtube.com")]
    [InlineData("on YouTube Music", "music.youtube.com")]
    [InlineData("Apple Music", "music.apple.com")]
    [InlineData("SoundCloud", "soundcloud.com")]
    public void ANamedServiceGetsItsOwnSearch(string service, string expectedHost)
    {
        var route = MusicServiceRouter.Route(service, "bohemian rhapsody");

        Assert.NotNull(route);
        Assert.Contains(expectedHost, route!.Value.Url, StringComparison.Ordinal);
    }

    /// <summary>The substring trap: "youtube" is inside "youtube music", and the longer wins.</summary>
    [Fact]
    public void YouTubeMusicIsNotReadAsYouTube()
    {
        var route = MusicServiceRouter.Route("YouTube Music", "jazz");

        Assert.Contains("music.youtube.com", route!.Value.Url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Spotify")]
    [InlineData("some player nobody has heard of")]
    public void EverythingElseFallsBackToTheDefaultPath(string service) =>
        Assert.Null(MusicServiceRouter.Route(service, "jazz"));

    [Fact]
    public void TheSpokenQueryIsEscapedRatherThanPastedIntoTheUrl()
    {
        var route = MusicServiceRouter.Route("YouTube", "sunflower & vibes");

        Assert.DoesNotContain(" ", route!.Value.Url, StringComparison.Ordinal);
        Assert.DoesNotContain("&", route.Value.Url[route.Value.Url.IndexOf('=')..], StringComparison.Ordinal);
    }
}
