namespace Winly.Core.Actions;

/// <summary>
/// What Winly can be asked to do. Each verb documents what <see cref="DesktopAction.Target"/> and
/// <see cref="DesktopAction.Argument"/> mean for it, because they carry different things per verb.
/// </summary>
public enum DesktopActionKind
{
    /// <summary>Target: an installed app's name, or an http(s) URL.</summary>
    Open,

    /// <summary>Target: a song, artist or album in plain words. Plays it on Spotify.</summary>
    Play,

    /// <summary>Target: a song, artist or album in plain words. Adds it to the Spotify queue.</summary>
    Queue,

    /// <summary>Target: playpause, play, pause, next, previous, stop, shuffle or repeat.</summary>
    Media,

    /// <summary>Target: an app's name, "spotify", or empty for the whole system. Uses Amount/Relative.</summary>
    Volume,

    /// <summary>Target: an app's name. Argument: focus, minimize, maximize, restore, close, left or right.</summary>
    Window,

    /// <summary>Target: lock, sleep, darkmode, lightmode, brightness, mute, wifi, bluetooth or showdesktop.</summary>
    System,

    /// <summary>Target: literal text to type into whatever the user is focused on.</summary>
    Type,

    /// <summary>Target: read or copy.</summary>
    Clipboard,

    /// <summary>Target: a file or folder to find under the user's profile and open.</summary>
    OpenPath,

    /// <summary>Target: the visible label of an on-screen control. Argument: the app it is in, or empty for the foreground one.</summary>
    Click,

    /// <summary>Target: what the timer is for. Amount: how many seconds from now.</summary>
    Timer,
}

/// <summary>One thing the model asked Winly to do on the desktop, from an <c>@@DO …@@</c> designation.</summary>
/// <param name="Amount">
/// A volume percentage, a brightness percentage, or a number of seconds, depending on the verb.
/// When <paramref name="Relative"/> is set it is a signed step to add to the current value instead
/// — "turn it down" cannot be expressed any other way, because the model cannot read the level.
/// </param>
public sealed record DesktopAction(
    DesktopActionKind Kind,
    string Target,
    string Argument = "",
    int Amount = 0,
    bool Relative = false);

/// <summary>What a media player reports about itself, from Spotify or the system media transport.</summary>
/// <param name="AppId">The player's app-user-model id or name, e.g. <c>Spotify</c>.</param>
/// <param name="VolumePercent">The player's own volume, or -1 when it does not report one.</param>
public sealed record NowPlaying(
    string AppId,
    string Title,
    string Artist,
    bool IsPlaying,
    int VolumePercent = -1,
    string DeviceName = "")
{
    /// <summary>One short line for the model, or empty when there is nothing worth saying.</summary>
    public string Describe()
    {
        if (Title.Length == 0)
        {
            return string.Empty;
        }

        var who = Artist.Length > 0 ? $" by {Artist}" : string.Empty;
        var where = DeviceName.Length > 0 ? $" on {DeviceName}" : string.Empty;
        var loudness = VolumePercent >= 0 ? $" Its own volume is {VolumePercent}%." : string.Empty;
        return $"{(IsPlaying ? "Playing" : "Paused")} in {AppId}{where}: \"{Title}\"{who}.{loudness}";
    }
}

/// <summary>An action that could not be carried out. The message is already user-facing (FR-031).</summary>
public sealed class DesktopActionFailedException(string userMessage) : Exception(userMessage);

public interface IDesktopActions
{
    /// <summary>Returns text to speak after the answer, or null when the action speaks for itself.</summary>
    /// <exception cref="DesktopActionFailedException">The action could not be carried out.</exception>
    Task<string?> Run(DesktopAction action, CancellationToken cancellationToken);

    /// <summary>Never throws: no player, or no permission to ask, is simply nothing playing.</summary>
    Task<NowPlaying?> CurrentlyPlaying(CancellationToken cancellationToken);

    /// <summary>
    /// Waits until an application launched earlier in the same request can actually be acted on,
    /// which is not the same as its process existing (FR-005). Returns false when
    /// <paramref name="timeout"/> passes without it becoming ready; never throws for that case,
    /// because "it never finished starting" is an ordinary outcome rather than an error.
    /// </summary>
    Task<bool> WaitForApplicationReady(string appName, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>
    /// Records where the user's attention was at the moment they started speaking, so a later
    /// <see cref="DesktopActionKind.Type"/> can verify it is still there before sending anything
    /// (FR-016). Called at activation start, before Winly speaks or acts — that instant is the only
    /// one that corresponds to what the user meant by "here".
    /// </summary>
    void RememberTypingTarget();
}
