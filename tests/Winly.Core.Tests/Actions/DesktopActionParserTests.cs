using Winly.Core.Actions;

namespace Winly.Core.Tests.Actions;

public class DesktopActionParserTests
{
    [Fact]
    public void StructuredActionIsKeptAndTheInBandTagNeverReachesSpeech()
    {
        var structured = new DesktopAction(DesktopActionKind.Open, "Spotify");

        var (spoken, action) = DesktopActionParser.Parse(
            "Opening Spotify. @@DO {\"action\":\"open\",\"target\":\"Spotify\"}@@", structured);

        Assert.Equal("Opening Spotify.", spoken);
        Assert.Same(structured, action);
    }

    [Fact]
    public void AnInBandTagIsRecoveredWhenTheStructuredActionIsMissing()
    {
        var (spoken, action) = DesktopActionParser.Parse(
            "Turning Spotify down.\n@@DO {\"action\":\"volume\",\"target\":\"spotify\",\"amount\":-20,\"relative\":true}@@",
            structuredAction: null);

        Assert.Equal("Turning Spotify down.", spoken);
        Assert.Equal(new DesktopAction(DesktopActionKind.Volume, "spotify", Amount: -20, Relative: true), action);
    }

    [Fact]
    public void PlayCarriesTheSpokenNameSoTheClientCanLookItUp()
    {
        Assert.Equal(
            new DesktopAction(DesktopActionKind.Play, "How Much Is The Weed Dominic Fike"),
            DesktopActionParser.TryParseTag("{\"action\":\"play\",\"target\":\"How Much Is The Weed Dominic Fike\"}"));
    }

    [Fact]
    public void AWindowCommandKeepsBothTheAppAndWhatToDoToIt()
    {
        Assert.Equal(
            new DesktopAction(DesktopActionKind.Window, "Chrome", "left"),
            DesktopActionParser.TryParseTag("{\"action\":\"window\",\"target\":\"Chrome\",\"argument\":\"left\"}"));
    }

    [Fact]
    public void ATimerKeepsItsSecondsUntouchedByTheVolumeClamp()
    {
        Assert.Equal(
            new DesktopAction(DesktopActionKind.Timer, "stretch", Amount: 1200),
            DesktopActionParser.TryParseTag("{\"action\":\"timer\",\"target\":\"stretch\",\"amount\":1200}"));
    }

    [Fact]
    public void VolumeForTheWholeSystemNeedsNoTarget()
    {
        Assert.Equal(new DesktopAction(DesktopActionKind.Volume, string.Empty, Amount: 50),
            DesktopActionParser.TryParseTag("{\"action\":\"volume\",\"amount\":50}"));
    }

    [Theory]
    [InlineData("{\"action\":\"launch\",\"target\":\"Spotify\"}")]         // not an action Winly knows
    [InlineData("{\"action\":\"open\",\"target\":\"\"}")]                  // nothing to open
    [InlineData("{\"action\":\"play\"}")]                                  // nothing to play
    [InlineData("{\"action\":\"media\"}")]                                 // no playback command
    [InlineData("{\"action\":\"volume\",\"target\":\"spotify\"}")]         // no level to set
    [InlineData("{\"action\":\"window\",\"target\":\"Chrome\"}")]          // no window command
    [InlineData("{\"action\":\"system\",\"target\":\"brightness\"}")]      // brightness with no level
    [InlineData("{\"action\":\"timer\",\"target\":\"tea\"}")]              // a timer with no duration
    [InlineData("{\"action\":\"timer\",\"target\":\"tea\",\"amount\":0}")] // ... or a zero one
    [InlineData("{\"action\":\"timer\",\"target\":\"x\",\"amount\":90000}")] // ... or longer than a day
    [InlineData("{\"target\":\"Spotify\"}")]                               // no action at all
    [InlineData("{ not json ")]
    public void UnusableTagsProduceNoAction(string json) => Assert.Null(DesktopActionParser.TryParseTag(json));

    [Theory]
    [InlineData(250, false, 100)]
    [InlineData(-40, false, 0)]    // an absolute level below zero is nonsense, not a step down
    [InlineData(-250, true, -100)] // a relative step keeps its sign, clamped to a full sweep
    [InlineData(-20, true, -20)]
    public void VolumeAmountsAreClampedToWhatTheFlagAllows(int given, bool relative, int expected) =>
        Assert.Equal(expected, DesktopActionParser.TryParseTag(
            $"{{\"action\":\"volume\",\"amount\":{given},\"relative\":{(relative ? "true" : "false")}}}")!.Amount);

    [Fact]
    public void AMalformedTagIsStrippedEvenThoughNothingCanBeDoneWithIt()
    {
        var (spoken, action) = DesktopActionParser.Parse("Sure thing. @@DO {oops}@@", structuredAction: null);

        Assert.Equal("Sure thing.", spoken);
        Assert.Null(action);
    }

    [Theory]
    [InlineData("Spotify", "Alright", "Kendrick Lamar", true, -1, "", "Playing in Spotify: \"Alright\" by Kendrick Lamar.")]
    [InlineData("Spotify", "Alright", "", false, -1, "", "Paused in Spotify: \"Alright\".")]
    [InlineData("Spotify", "Alright", "Kendrick Lamar", true, 35, "Desktop", "Playing in Spotify on Desktop: \"Alright\" by Kendrick Lamar. Its own volume is 35%.")]
    [InlineData("Spotify", "", "", true, -1, "", "")]
    public void NowPlayingReadsAsOneLineOrNothingAtAll(
        string app, string title, string artist, bool playing, int volume, string device, string expected) =>
        Assert.Equal(expected, new NowPlaying(app, title, artist, playing, volume, device).Describe());
}
