using Winly.Core.Actions;

namespace Winly.Core.Providers;

/// <param name="LinkUrl">Where the user signs in, when <paramref name="IsLinked"/> is false.</param>
public sealed record MusicLinkStatus(bool IsLinked, Uri? LinkUrl, bool IsConfigured);

/// <summary>The account is reachable but has not been connected to Winly yet.</summary>
public sealed class MusicNotLinkedException() : Exception("I'm not connected to your Spotify yet. Open the Winly panel and press Connect Spotify.");

/// <summary>Linked, but the request could not be carried out. The message is user-facing.</summary>
public sealed class MusicRequestFailedException(string userMessage) : Exception(userMessage);

/// <summary>
/// Control of the user's own music account, proxied through the backend so the service's app
/// credentials and the user's tokens both stay off this machine (Constitution Principle I).
///
/// This is what separates changing Spotify's own volume from turning Spotify down in the Windows
/// mixer: the mixer attenuates whatever the app sends, the account API moves the app's own slider.
/// </summary>
public interface IMusicService
{
    Task<MusicLinkStatus> Status(CancellationToken cancellationToken);

    /// <summary>Null when nothing is playing, or when the account is not linked.</summary>
    Task<NowPlaying?> Playing(CancellationToken cancellationToken);

    /// <summary>Starts the best match for a spoken name, or adds it to the queue instead.</summary>
    Task<string> Play(string spokenQuery, bool queueOnly, CancellationToken cancellationToken);

    /// <summary>play, pause, playpause, next, previous, shuffle or repeat.</summary>
    Task Transport(string command, CancellationToken cancellationToken);

    /// <summary>An absolute percentage, or a signed step away from the player's current volume.</summary>
    Task<int> SetVolume(int amount, bool relative, CancellationToken cancellationToken);
}
