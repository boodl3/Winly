using System.Text.RegularExpressions;

namespace Winly.Core.Companion;

/// <summary>
/// Decides whether a question needs the screenshots attached to it.
///
/// The screenshot is the whole non-cached cost of a turn, and a command like "pause the song"
/// gains nothing from one. Being wrong is cheap but not free: the model can answer with a
/// "show me the screen" designation and the orchestrator asks again with the images, which costs
/// one extra round trip. So this errs towards sending — it only skips when the wording is
/// recognisably about the machine and mentions nothing that has to be looked at.
/// </summary>
public static partial class ScreenContextHeuristic
{
    public static bool NeedsScreen(string transcript)
    {
        var words = transcript.ToLowerInvariant();

        // Anything that points at something visible settles it, even inside a command.
        if (LooksAtSomething().IsMatch(words))
        {
            return true;
        }

        return !AsksTheMachineToDoSomething().IsMatch(words) && !TalksAboutPlayback().IsMatch(words);
    }

    // Deliberately excludes bare "this"/"that": "pause the song that's playing" is a command, not a
    // question about the screen. "window" is excluded too, since closing one is a window command.
    [GeneratedRegex(
        @"\b(screen|monitor|display|desktop|showing|shown|visible|see|seeing|look|looking|read|reading|button|icon|tab|dialog|popup|menu|toolbar|highlight\w*|selected|cursor|webpage|web page|this page|this says|it says|error message|what does this|what is this|what's this|point at|point to|translate|summari[sz]e|explain this|describe)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex LooksAtSomething();

    [GeneratedRegex(
        @"\b(play|pause|resume|stop|skip|next|previous|rewind|shuffle|repeat|queue|mute|unmute|louder|quieter|volume|turn (it |the )?(up|down)|open|launch|start|close|quit|minimi[sz]e|maximi[sz]e|restore|snap|focus|switch to|lock|sleep|shut down|brightness|dark mode|light mode|night light|wi-?fi|bluetooth|timer|remind|type|clipboard|copy that|paste|on the (left|right)|(left|right) half)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex AsksTheMachineToDoSomething();

    // "what song is this" needs the now-playing line, never a screenshot.
    [GeneratedRegex(@"\b(song|track|music|artist|album|playlist|spotify|playing)\b", RegexOptions.IgnoreCase)]
    private static partial Regex TalksAboutPlayback();
}
