namespace Winly.Core.Providers;

public interface ISpeechToTextProvider
{
    /// <summary>
    /// Streams 16 kHz mono PCM16 frames from <paramref name="pcm16MonoAudio"/> until it ends,
    /// then completes with the final transcript once the utterance is finalized (FR-005).
    /// </summary>
    Task<string> Transcribe(Stream pcm16MonoAudio, CancellationToken cancellationToken);
}
