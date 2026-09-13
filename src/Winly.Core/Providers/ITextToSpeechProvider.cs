namespace Winly.Core.Providers;

/// <summary>
/// Synthesized speech. A non-null <see cref="PcmSampleRateHz"/> means raw mono PCM16 that plays
/// as it arrives; null means an encoded container (MP3) the player has to buffer before it starts.
/// </summary>
public sealed record SpokenAudio(Stream Audio, int? PcmSampleRateHz);

public interface ITextToSpeechProvider
{
    /// <summary>Returns audio for already-sanitized spoken text (FR-011, FR-012).</summary>
    /// <param name="previousText">
    /// What was already spoken in this answer, or null for the first chunk of one. An answer is
    /// synthesized a sentence at a time so playback can start early, and without this the voice
    /// restarts its intonation at every seam — three sentences read as three announcements.
    /// </param>
    Task<SpokenAudio> Synthesize(string spokenText, string? previousText, CancellationToken cancellationToken);
}
