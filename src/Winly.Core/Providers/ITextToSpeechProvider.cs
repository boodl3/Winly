namespace Winly.Core.Providers;

/// <summary>
/// Synthesized speech. A non-null <see cref="PcmSampleRateHz"/> means raw mono PCM16 that plays
/// as it arrives; null means an encoded container (MP3) the player has to buffer before it starts.
/// </summary>
public sealed record SpokenAudio(Stream Audio, int? PcmSampleRateHz);

public interface ITextToSpeechProvider
{
    /// <summary>Returns audio for already-sanitized spoken text (FR-011, FR-012).</summary>
    Task<SpokenAudio> Synthesize(string spokenText, CancellationToken cancellationToken);
}
