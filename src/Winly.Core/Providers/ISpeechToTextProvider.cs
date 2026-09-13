namespace Winly.Core.Providers;

public interface ISpeechToTextProvider
{
    /// <summary>
    /// Streams 16 kHz mono PCM16 frames from <paramref name="pcm16MonoAudio"/> until it ends,
    /// then completes with the final transcript once the utterance is finalized (FR-005).
    /// </summary>
    /// <param name="onPartial">
    /// Called with the transcript so far each time the provider revises it, while the microphone
    /// is still open. Only the confirmation loop uses it: a yes/no is decidable from the first
    /// partial, and waiting out the fixed window instead cost four seconds per confirmation.
    /// Best-effort — a provider that never revises simply never calls it.
    /// </param>
    Task<string> Transcribe(Stream pcm16MonoAudio, CancellationToken cancellationToken, Action<string>? onPartial = null);
}
