using Winly.Core.Providers;

namespace Winly.Core.Abstractions;

public interface IAudioPlayback
{
    /// <summary>Plays audio to completion, or until cancelled or stopped.</summary>
    /// <exception cref="PlaybackDeviceUnavailableException">No output device is available.</exception>
    Task Play(SpokenAudio audio, CancellationToken cancellationToken);

    /// <summary>Idempotent; must not throw.</summary>
    Task Stop();
}
