using Winly.Core.Companion;

namespace Winly.Core.Tests.Companion;

/// <summary>
/// The screenshot is the whole non-cached cost of a turn, so skipping it matters — but a wrong
/// skip costs the user a round trip. These pin the two directions: commands must skip, and
/// anything pointing at something visible must not.
/// </summary>
public class ScreenContextHeuristicTests
{
    [Theory]
    [InlineData("pause the song")]
    [InlineData("pause the song that's playing")]   // "that" alone must not drag the screen in
    [InlineData("lower the volume from the spotify app")]
    [InlineData("play how much is the weed by dominic fike")]
    [InlineData("skip this track")]                 // nor "this"
    [InlineData("turn it down")]
    [InlineData("open notepad")]
    [InlineData("lock my pc")]
    [InlineData("put chrome on the left")]
    [InlineData("remind me in twenty minutes to stretch")]
    [InlineData("what song is this")]               // answered by the now-playing line
    [InlineData("who is the artist")]
    public void CommandsAndMusicTalkGoWithoutTheScreen(string transcript) =>
        Assert.False(ScreenContextHeuristic.NeedsScreen(transcript));

    [Theory]
    [InlineData("what is this button")]
    [InlineData("what does this button do")]
    [InlineData("read me the error message")]
    [InlineData("what's on my screen")]
    [InlineData("summarise this page")]
    [InlineData("translate what it says")]
    [InlineData("where is the save icon")]
    [InlineData("close the dialog that just appeared")]  // a command, but it names something visible
    [InlineData("play the video that's showing")]        // likewise
    public void AnythingPointingAtSomethingVisibleKeepsTheScreen(string transcript) =>
        Assert.True(ScreenContextHeuristic.NeedsScreen(transcript));

    [Theory]
    [InlineData("what is the capital of france")]
    [InlineData("how long should i boil an egg")]
    [InlineData("")]
    public void AnythingElseErrsTowardsSendingIt(string transcript) =>
        Assert.True(ScreenContextHeuristic.NeedsScreen(transcript));
}
